using BookVerse.Domain.Common;
using BookVerse.Domain.Enums;
using BookVerse.Domain.Exceptions;

namespace BookVerse.Domain.Entities.Identity;

public class User : AuditableEntity<Guid>
{
    private readonly List<UserRole> _userRoles = [];
    private readonly List<RefreshToken> _refreshTokens = [];
    private readonly List<UserSession> _userSessions = [];

    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string PasswordSalt { get; private set; } = string.Empty;
    public UserStatus Status { get; private set; } = UserStatus.Active;
    public int FailedLoginCount { get; private set; } = 0;
    public DateTimeOffset? LockoutUntil { get; private set; }

    public IReadOnlyCollection<UserRole> UserRoles => _userRoles.AsReadOnly();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();
    public IReadOnlyCollection<UserSession> UserSessions => _userSessions.AsReadOnly();

    private User() { } // For EF Core

    public static User Create(string email, string passwordHash, string passwordSalt)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new IdentityDomainException("Email cannot be empty.");

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            PasswordSalt = passwordSalt,
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutUntil.HasValue && LockoutUntil.Value > now;

    /// <summary>
    /// Records a failed login; when the consecutive-failure threshold is reached the
    /// account enters a temporary lockout window. Returns true if this failure locked it.
    /// </summary>
    public bool RegisterLoginFailure(int maxAttempts, TimeSpan lockoutDuration, DateTimeOffset now)
    {
        FailedLoginCount++;
        if (FailedLoginCount < maxAttempts) return false;

        FailedLoginCount = 0;
        LockoutUntil = now + lockoutDuration;
        return true;
    }

    public void ResetLoginFailures()
    {
        FailedLoginCount = 0;
        LockoutUntil = null;
    }

    public void AddRole(Role role)
    {
        if (_userRoles.Any(ur => ur.RoleId == role.Id)) return;
        _userRoles.Add(new UserRole(Id, role.Id));
    }

    public RefreshToken AddRefreshToken(string tokenHash, string jwtId, DateTimeOffset expiresAt, string? ipAddress)
    {
        var token = RefreshToken.Create(Id, tokenHash, jwtId, expiresAt, ipAddress);
        _refreshTokens.Add(token);
        return token;
    }

    public void RevokeAllRefreshTokens(string? ipAddress, string reason = "Security Revocation")
    {
        foreach (var token in _refreshTokens.Where(t => t.IsActive))
        {
            token.Revoke(ipAddress, reason);
        }
    }
}

public class Role : Entity<Guid>
{
    private readonly List<RolePermission> _rolePermissions = [];

    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    public IReadOnlyCollection<RolePermission> RolePermissions => _rolePermissions.AsReadOnly();

    private Role() { }

    public Role(Guid id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }

    public void AddPermission(Permission permission)
    {
        if (_rolePermissions.Any(rp => rp.PermissionId == permission.Id)) return;
        _rolePermissions.Add(new RolePermission(Id, permission.Id));
    }
}

public class Permission : Entity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    private Permission() { }

    public Permission(Guid id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }
}

public class RolePermission
{
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }

    public Role Role { get; private set; } = null!;
    public Permission Permission { get; private set; } = null!;

    private RolePermission() { }

    public RolePermission(Guid roleId, Guid permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }
}

public class UserRole
{
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }

    public User User { get; private set; } = null!;
    public Role Role { get; private set; } = null!;

    private UserRole() { }

    public UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }
}

public class RefreshToken : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public string JwtId { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public string? CreatedByIp { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedByIp { get; private set; }
    public string? ReplacedByToken { get; private set; }
    public string? RevocationReason { get; private set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;

    private RefreshToken() { }

    public static RefreshToken Create(Guid userId, string tokenHash, string jwtId, DateTimeOffset expiresAt, string? ipAddress)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            JwtId = jwtId,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByIp = ipAddress
        };
    }

    public void Revoke(string? ipAddress, string? reason = null, string? replacedBy = null)
    {
        RevokedAt = DateTimeOffset.UtcNow;
        RevokedByIp = ipAddress;
        RevocationReason = reason;
        ReplacedByToken = replacedBy;
    }
}

public class UserSession : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public string Device { get; private set; } = string.Empty;
    public string IpAddress { get; private set; } = string.Empty;
    public string? UserAgent { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; private set; } = true;

    private UserSession() { }

    public static UserSession Create(Guid userId, string device, string ipAddress, string? userAgent)
    {
        return new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Device = device,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            LastActivityAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
    }

    public void Touch()
    {
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    public void Invalidate()
    {
        IsActive = false;
    }
}
