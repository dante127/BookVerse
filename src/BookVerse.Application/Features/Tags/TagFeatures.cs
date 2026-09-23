using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Services;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Domain.Entities.Tags;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Tags;

public record TagDto(Guid Id, string Name, string Slug);

public record GetTagsQuery : IRequest<IReadOnlyList<TagDto>>;

public class GetTagsQueryHandler : IRequestHandler<GetTagsQuery, IReadOnlyList<TagDto>>
{
    private readonly IApplicationDbContext _context;

    public GetTagsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TagDto>> Handle(GetTagsQuery request, CancellationToken cancellationToken)
    {
        return await _context.Tags
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TagDto(t.Id, t.Name, t.Slug))
            .ToListAsync(cancellationToken);
    }
}

public record CreateTagCommand(string Name) : IRequest<Guid>;

public class CreateTagCommandValidator : AbstractValidator<CreateTagCommand>
{
    public CreateTagCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
    }
}

public class CreateTagCommandHandler : IRequestHandler<CreateTagCommand, Guid>
{
    private readonly IApplicationDbContext _context;

    public CreateTagCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> Handle(CreateTagCommand request, CancellationToken cancellationToken)
    {
        var slug = Slug.Slugify(request.Name);

        var existing = await _context.Tags.FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);
        if (existing != null) return existing.Id;

        var tag = Tag.Create(request.Name, slug);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync(cancellationToken);

        return tag.Id;
    }
}
