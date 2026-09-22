using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Domain.Entities.Genres;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Genres;

public record GenreItemDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid? ParentGenreId,
    IReadOnlyList<GenreItemDto> SubGenres);

// --- Get Genres Hierarchy ---
public record GetGenresQuery : IRequest<IReadOnlyList<GenreItemDto>>;

public class GetGenresQueryHandler : IRequestHandler<GetGenresQuery, IReadOnlyList<GenreItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;

    public GetGenresQueryHandler(IApplicationDbContext context, ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    public async Task<IReadOnlyList<GenreItemDto>> Handle(GetGenresQuery request, CancellationToken cancellationToken)
    {
        const string cacheKey = "genres:taxonomy";
        var cached = await _cacheService.GetAsync<List<GenreItemDto>>(cacheKey, cancellationToken);
        if (cached != null) return cached;

        var allGenres = await _context.Genres.AsNoTracking().ToListAsync(cancellationToken);

        List<GenreItemDto> BuildTree(Guid? parentId)
        {
            return allGenres
                .Where(g => g.ParentGenreId == parentId)
                .OrderBy(g => g.Name)
                .Select(g => new GenreItemDto(
                    g.Id,
                    g.Name,
                    g.Slug,
                    g.Description,
                    g.ParentGenreId,
                    BuildTree(g.Id)))
                .ToList();
        }

        var tree = BuildTree(null);

        await _cacheService.SetAsync(cacheKey, tree, TimeSpan.FromHours(1), cancellationToken);

        return tree;
    }
}

// --- Create Genre ---
public record CreateGenreCommand(
    string Name,
    string? Description = null,
    Guid? ParentGenreId = null) : IRequest<Guid>;

public class CreateGenreCommandValidator : AbstractValidator<CreateGenreCommand>
{
    public CreateGenreCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class CreateGenreCommandHandler : IRequestHandler<CreateGenreCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;

    public CreateGenreCommandHandler(IApplicationDbContext context, ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    public async Task<Guid> Handle(CreateGenreCommand request, CancellationToken cancellationToken)
    {
        var slug = request.Name.Trim().ToLowerInvariant().Replace(' ', '-');

        if (await _context.Genres.AnyAsync(g => g.Slug == slug, cancellationToken))
        {
            throw new ConflictException($"A genre with slug '{slug}' already exists.");
        }

        if (request.ParentGenreId.HasValue)
        {
            var parentExists = await _context.Genres.AnyAsync(g => g.Id == request.ParentGenreId.Value, cancellationToken);
            if (!parentExists) throw new NotFoundException("Parent Genre", request.ParentGenreId.Value);
        }

        var genre = Genre.Create(request.Name, slug, request.Description, request.ParentGenreId);
        _context.Genres.Add(genre);

        await _context.SaveChangesAsync(cancellationToken);
        await _cacheService.RemoveAsync("genres:taxonomy", cancellationToken);

        return genre.Id;
    }
}
