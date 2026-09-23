using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;

namespace BookVerse.Application.Common.Extensions;

/// <summary>
/// CQ-02: the auth guard was copy-pasted in 20+ handlers next to a nullable unwrap.
/// Handlers reach for RequireUserId() instead; the anonymous-fallback call sites keep
/// checking IsAuthenticated directly on purpose.
/// </summary>
public static class CurrentUserExtensions
{
    public static Guid RequireUserId(this ICurrentUserService currentUserService)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.UserId == null)
            throw new UnauthorizedException();

        return currentUserService.UserId.Value;
    }
}
