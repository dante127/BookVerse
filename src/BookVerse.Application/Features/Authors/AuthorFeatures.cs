using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Domain.Entities.Authors;
using BookVerse.Application.Common.Validation;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Authors;

public record AuthorSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Country,
    string? ProfileImageUrl,
    int FollowersCount,
    int BooksCount);

public record AuthorDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string? Biography,
    DateOnly? BirthDate,
    string? Country,
    string? WebsiteUrl,
    string? ProfileImageUrl,
    int FollowersCount,
    IReadOnlyList<AuthorBookItemDto> Books);

public record AuthorBookItemDto(
    Guid BookId,
    string Title,
    string? CoverImageUrl,
    decimal AverageRating,
    int RatingsCount);

// --- Get Authors ---
public record GetAuthorsQuery(
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<AuthorSummaryDto>>;

public class GetAuthorsQueryHandler : IRequestHandler<GetAuthorsQuery, PagedResult<AuthorSummaryDto>>
{
    private readonly IApplicationDbContext _context;

    public GetAuthorsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<AuthorSummaryDto>> Handle(GetAuthorsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Pagination.Normalize(request.Page, request.PageSize);
        var query = _context.Authors.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(a => a.Name.Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.FollowersCount)
            .ThenBy(a => a.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuthorSummaryDto(
                a.Id,
                a.Name,
                a.Slug,
                a.Country,
                a.ProfileImageUrl,
                a.FollowersCount,
                _context.BookAuthors.Count(ba => ba.AuthorId == a.Id)))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuthorSummaryDto>(items, totalCount, page, pageSize);
    }
}

// --- Get Author By Id ---
public record GetAuthorByIdQuery(Guid Id) : IRequest<AuthorDetailDto>;

public class GetAuthorByIdQueryHandler : IRequestHandler<GetAuthorByIdQuery, AuthorDetailDto>
{
    private readonly IApplicationDbContext _context;

    public GetAuthorByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AuthorDetailDto> Handle(GetAuthorByIdQuery request, CancellationToken cancellationToken)
    {
        var author = await _context.Authors
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

        if (author == null) throw new NotFoundException("Author", request.Id);

        var books = await _context.BookAuthors
            .AsNoTracking()
            .Where(ba => ba.AuthorId == request.Id)
            .Select(ba => new AuthorBookItemDto(
                ba.Book.Id,
                ba.Book.Title,
                ba.Book.CoverImageUrl,
                ba.Book.AverageRating,
                ba.Book.RatingsCount))
            .ToListAsync(cancellationToken);

        return new AuthorDetailDto(
            author.Id,
            author.Name,
            author.Slug,
            author.Biography,
            author.BirthDate,
            author.Country,
            author.WebsiteUrl,
            author.ProfileImageUrl,
            author.FollowersCount,
            books);
    }
}

// --- Create Author ---
public record CreateAuthorCommand(
    string Name,
    string? Biography = null,
    DateOnly? BirthDate = null,
    string? Country = null,
    string? WebsiteUrl = null,
    string? ProfileImageUrl = null) : IRequest<Guid>;

public class CreateAuthorCommandValidator : AbstractValidator<CreateAuthorCommand>
{
    public CreateAuthorCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Biography).MaximumLength(4000);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.WebsiteUrl).IsValidWebUrl().MaximumLength(2000);
        RuleFor(x => x.ProfileImageUrl).IsValidWebUrl().MaximumLength(2000);
    }
}

public class CreateAuthorCommandHandler : IRequestHandler<CreateAuthorCommand, Guid>
{
    private readonly IApplicationDbContext _context;

    public CreateAuthorCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> Handle(CreateAuthorCommand request, CancellationToken cancellationToken)
    {
        var baseSlug = request.Name.Trim().ToLowerInvariant().Replace(' ', '-');
        var slug = baseSlug;
        var counter = 1;

        while (await _context.Authors.AnyAsync(a => a.Slug == slug, cancellationToken))
        {
            slug = $"{baseSlug}-{counter++}";
        }

        var author = Author.Create(
            request.Name,
            slug,
            request.Biography,
            request.BirthDate,
            request.Country,
            request.WebsiteUrl,
            request.ProfileImageUrl);

        _context.Authors.Add(author);
        await _context.SaveChangesAsync(cancellationToken);

        return author.Id;
    }
}

// --- Follow Author ---
public record FollowAuthorCommand(Guid AuthorId) : IRequest<bool>;

public class FollowAuthorCommandHandler : IRequestHandler<FollowAuthorCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public FollowAuthorCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<bool> Handle(FollowAuthorCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;

        var author = await _context.Authors.FindAsync([request.AuthorId], cancellationToken);
        if (author == null) throw new NotFoundException("Author", request.AuthorId);

        var alreadyFollowing = await _context.AuthorFollowers
            .AnyAsync(af => af.UserId == userId && af.AuthorId == request.AuthorId, cancellationToken);

        if (alreadyFollowing) return true;

        var follower = new AuthorFollower(userId, request.AuthorId);
        _context.AuthorFollowers.Add(follower);
        author.IncrementFollowers();

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

// --- Unfollow Author ---
public record UnfollowAuthorCommand(Guid AuthorId) : IRequest<bool>;

public class UnfollowAuthorCommandHandler : IRequestHandler<UnfollowAuthorCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UnfollowAuthorCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<bool> Handle(UnfollowAuthorCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;

        var follower = await _context.AuthorFollowers
            .FirstOrDefaultAsync(af => af.UserId == userId && af.AuthorId == request.AuthorId, cancellationToken);

        if (follower == null) return true;

        var author = await _context.Authors.FindAsync([request.AuthorId], cancellationToken);
        if (author != null)
        {
            author.DecrementFollowers();
        }

        _context.AuthorFollowers.Remove(follower);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
