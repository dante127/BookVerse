using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Services;
using BookVerse.Application.Common.Extensions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Validation;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Users;

public record UserProfileDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    string PreferredLanguage,
    IReadOnlyList<string> FavoriteGenreIds,
    IReadOnlyList<string> PreferredAuthorIds,
    IReadOnlyList<string> Roles);

// --- Get Current User ---
public record GetCurrentUserQuery : IRequest<UserProfileDto>;

public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, UserProfileDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetCurrentUserQueryHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<UserProfileDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.RequireUserId();

        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException("User", userId);
        }

        var profile = await _context.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();

        var favGenres = string.IsNullOrWhiteSpace(profile?.FavoriteGenreIdsJson)
            ? new List<string>()
            : profile.FavoriteGenreIdsJson.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        var prefAuthors = string.IsNullOrWhiteSpace(profile?.PreferredAuthorIdsJson)
            ? new List<string>()
            : profile.PreferredAuthorIdsJson.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        return new UserProfileDto(
            user.Id,
            user.Email,
            profile?.DisplayName ?? user.Email,
            profile?.Bio,
            profile?.AvatarUrl,
            profile?.PreferredLanguage ?? "en",
            favGenres,
            prefAuthors,
            roles);
    }
}

// --- Update Profile ---
public record UpdateUserProfileCommand(
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    string PreferredLanguage) : IRequest<UserProfileDto>;

public class UpdateUserProfileCommandValidator : AbstractValidator<UpdateUserProfileCommand>
{
    public UpdateUserProfileCommandValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Bio).MaximumLength(1000);
        RuleFor(x => x.AvatarUrl).IsValidWebUrl().MaximumLength(2000);
        RuleFor(x => x.PreferredLanguage).NotEmpty().MaximumLength(10);
    }
}

public class UpdateUserProfileCommandHandler : IRequestHandler<UpdateUserProfileCommand, UserProfileDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateUserProfileCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<UserProfileDto> Handle(UpdateUserProfileCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.RequireUserId();
        var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile == null)
        {
            profile = Domain.Entities.Profiles.UserProfile.Create(
                userId,
                request.DisplayName,
                request.Bio,
                request.AvatarUrl,
                request.PreferredLanguage);
            _context.UserProfiles.Add(profile);
        }
        else
        {
            profile.UpdateProfile(request.DisplayName, request.Bio, request.AvatarUrl, request.PreferredLanguage);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return new UserProfileDto(
            userId,
            user?.Email ?? string.Empty,
            profile.DisplayName,
            profile.Bio,
            profile.AvatarUrl,
            profile.PreferredLanguage,
            [],
            [],
            []);
    }
}

// --- Update Preferences ---
public record UpdateReadingPreferencesCommand(
    IReadOnlyList<string> FavoriteGenreIds,
    IReadOnlyList<string> PreferredAuthorIds) : IRequest<bool>;

public class UpdateReadingPreferencesCommandHandler : IRequestHandler<UpdateReadingPreferencesCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICacheService _cacheService;

    public UpdateReadingPreferencesCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICacheService cacheService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _cacheService = cacheService;
    }

    public async Task<bool> Handle(UpdateReadingPreferencesCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.RequireUserId();
        var profile = await _context.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile == null)
            throw new NotFoundException("Profile for user", userId);

        var genresCsv = string.Join(',', request.FavoriteGenreIds);
        var authorsCsv = string.Join(',', request.PreferredAuthorIds);

        profile.SetPreferences(genresCsv, authorsCsv);
        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate user recommendations cache
        await _cacheService.RemoveAsync(CacheKeys.UserRecommendations(userId), cancellationToken);

        return true;
    }
}
