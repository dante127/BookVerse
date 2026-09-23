using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Domain.Entities.Identity;
using BookVerse.Domain.Entities.Profiles;
using BookVerse.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Auth;

public record AuthResponseDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

// --- Register ---
public record RegisterCommand(string Email, string Password, string DisplayName) : IRequest<AuthResponseDto>;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).WithMessage("Password must be at least 8 characters long.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
    }
}

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResponseDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public RegisterCommandHandler(IApplicationDbContext context, IPasswordHasher passwordHasher, ITokenService tokenService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<AuthResponseDto> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _context.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (existingUser)
        {
            throw new ConflictException("A user with this email address already exists.");
        }

        var (hash, salt) = _passwordHasher.HashPassword(request.Password);
        var user = User.Create(normalizedEmail, hash, salt);

        // Assign Reader role by default
        var readerRole = await _context.Roles
            .Include(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.Name == "Reader", cancellationToken);

        if (readerRole != null)
        {
            user.AddRole(readerRole);
        }

        var profile = UserProfile.Create(user.Id, request.DisplayName);

        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        var hashedRefreshToken = _tokenService.HashToken(rawRefreshToken);
        var jwtId = Guid.NewGuid().ToString();
        var refreshTokenExpiry = DateTimeOffset.UtcNow + _tokenService.RefreshTokenLifetime;

        user.AddRefreshToken(hashedRefreshToken, jwtId, refreshTokenExpiry, null);

        _context.Users.Add(user);
        _context.UserProfiles.Add(profile);

        await _context.SaveChangesAsync(cancellationToken);

        var permissions = readerRole?.RolePermissions.Select(rp => rp.Permission.Name).ToList() ?? [];
        var accessToken = _tokenService.GenerateAccessToken(user, profile, permissions);

        return new AuthResponseDto(
            user.Id,
            user.Email,
            profile.DisplayName,
            accessToken,
            rawRefreshToken,
            refreshTokenExpiry,
            readerRole != null ? [readerRole.Name] : [],
            permissions);
    }
}

// --- Login ---
public record LoginCommand(string Email, string Password) : IRequest<AuthResponseDto>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResponseDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ICurrentUserService _currentUserService;

    public LoginCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _currentUserService = currentUserService;
    }

    public async Task<AuthResponseDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .ThenInclude(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (user.Status != UserStatus.Active)
        {
            throw new ForbiddenException($"User account is {user.Status}.");
        }

        var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        var hashedRefreshToken = _tokenService.HashToken(rawRefreshToken);
        var jwtId = Guid.NewGuid().ToString();
        var refreshTokenExpiry = DateTimeOffset.UtcNow + _tokenService.RefreshTokenLifetime;

        var refreshToken = user.AddRefreshToken(hashedRefreshToken, jwtId, refreshTokenExpiry, _currentUserService.IpAddress);
        _context.RefreshTokens.Add(refreshToken);

        await _context.SaveChangesAsync(cancellationToken);

        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToList();

        var accessToken = _tokenService.GenerateAccessToken(user, profile, permissions);

        return new AuthResponseDto(
            user.Id,
            user.Email,
            profile?.DisplayName ?? user.Email,
            accessToken,
            rawRefreshToken,
            refreshTokenExpiry,
            roles,
            permissions);
    }
}

// --- Refresh Token (Rotation & Replay Detection) ---
public record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResponseDto>;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResponseDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly ICurrentUserService _currentUserService;

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        ITokenService tokenService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tokenService = tokenService;
        _currentUserService = currentUserService;
    }

    public async Task<AuthResponseDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var hashedPresentedToken = _tokenService.HashToken(request.RefreshToken);

        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hashedPresentedToken, cancellationToken);

        if (storedToken == null)
        {
            throw new UnauthorizedException("Invalid refresh token.");
        }

        var user = await _context.Users
            .Include(u => u.RefreshTokens)
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .ThenInclude(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == storedToken.UserId, cancellationToken);

        if (user == null || user.Status != UserStatus.Active)
        {
            throw new UnauthorizedException("User account not eligible for token refresh.");
        }

        // Replay Attack Detection: If token is revoked, compromise detected! Revoke entire family!
        if (storedToken.IsRevoked)
        {
            user.RevokeAllRefreshTokens(_currentUserService.IpAddress, "Compromised Token Family Detected");
            await _context.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException("Security violation: revoked token presented. Token family revoked.");
        }

        if (storedToken.IsExpired)
        {
            throw new UnauthorizedException("Refresh token has expired.");
        }

        // Rotate token
        var newRawRefreshToken = _tokenService.GenerateRefreshToken();
        var newHashedRefreshToken = _tokenService.HashToken(newRawRefreshToken);
        var newJwtId = Guid.NewGuid().ToString();
        var newExpiry = DateTimeOffset.UtcNow + _tokenService.RefreshTokenLifetime;

        storedToken.Revoke(_currentUserService.IpAddress, "Rotated by client", newHashedRefreshToken);
        var newRefreshToken = user.AddRefreshToken(newHashedRefreshToken, newJwtId, newExpiry, _currentUserService.IpAddress);
        _context.RefreshTokens.Add(newRefreshToken);

        await _context.SaveChangesAsync(cancellationToken);

        var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToList();

        var newAccessToken = _tokenService.GenerateAccessToken(user, profile, permissions);

        return new AuthResponseDto(
            user.Id,
            user.Email,
            profile?.DisplayName ?? user.Email,
            newAccessToken,
            newRawRefreshToken,
            newExpiry,
            roles,
            permissions);
    }
}

// --- Revoke Token ---
public record RevokeTokenCommand(string RefreshToken) : IRequest<bool>;

public class RevokeTokenCommandHandler : IRequestHandler<RevokeTokenCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly ICurrentUserService _currentUserService;

    public RevokeTokenCommandHandler(
        IApplicationDbContext context,
        ITokenService tokenService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tokenService = tokenService;
        _currentUserService = currentUserService;
    }

    public async Task<bool> Handle(RevokeTokenCommand request, CancellationToken cancellationToken)
    {
        var hashedToken = _tokenService.HashToken(request.RefreshToken);
        var token = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hashedToken, cancellationToken);

        if (token == null || !token.IsActive) return false;

        token.Revoke(_currentUserService.IpAddress, "User explicit revocation");
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
