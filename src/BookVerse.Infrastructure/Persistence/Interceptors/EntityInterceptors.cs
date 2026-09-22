using BookVerse.Application.Common.Interfaces;
using BookVerse.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookVerse.Infrastructure.Persistence.Interceptors;

public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AuditableEntityInterceptor(ICurrentUserService currentUserService, IDateTimeProvider dateTimeProvider)
    {
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateEntities(DbContext? context)
    {
        if (context == null) return;

        var now = _dateTimeProvider.UtcNow;
        var user = _currentUserService.Email ?? "System";

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditableEntity<Guid> auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedAt = now;
                    auditable.CreatedBy = user;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = user;
                }
            }
        }
    }
}

public class DispatchDomainEventsInterceptor : SaveChangesInterceptor
{
    private readonly IMediator _mediator;

    public DispatchDomainEventsInterceptor(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await DispatchEvents(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private async Task DispatchEvents(DbContext? context, CancellationToken cancellationToken)
    {
        if (context == null) return;

        var aggregates = context.ChangeTracker
            .Entries<IAggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = aggregates
            .SelectMany(a => a.DomainEvents)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        foreach (var domainEvent in domainEvents)
        {
            if (domainEvent is INotification notification)
            {
                await _mediator.Publish(notification, cancellationToken);
            }
        }
    }
}
