using BookVerse.Application.Common.Interfaces;
using BookVerse.Domain.Entities.Books;
using BookVerse.Domain.Entities.Reading;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Common.Services;

/// <summary>
/// Single owner of completion coordination: keeps ReadingProgress and the yearly
/// ReadingGoal consistent when a book's completion state changes. UserBook status
/// transitions stay with UserBook.TransitionStatus (called by the handlers).
/// </summary>
public static class ReadingCompletion
{
    /// <summary>
    /// Status-driven sync used by library handlers: makes ReadingProgress match the
    /// target completion state and adjusts the current-year goal exactly once per edge.
    /// </summary>
    public static async Task SyncCompletionStatusAsync(
        IApplicationDbContext context,
        Guid userId,
        Book book,
        bool completed,
        CancellationToken cancellationToken = default)
    {
        var progress = await context.ReadingProgresses
            .FirstOrDefaultAsync(rp => rp.UserId == userId && rp.BookId == book.Id, cancellationToken);

        var wasCompleted = progress?.CompletedAt != null;

        if (progress == null)
        {
            // No progress record: there is nothing to un-complete, and a manual
            // completion is represented by a full-page progress row.
            if (!completed) return;

            progress = ReadingProgress.Create(userId, book.Id, book.PageCount, book.PageCount);
            context.ReadingProgresses.Add(progress);
        }
        else
        {
            progress.ReconcileTotalPages(book.PageCount);
            if (completed)
                progress.UpdateProgress(progress.TotalPages);
            else
                progress.MarkUnfinished();
        }

        await ApplyGoalEdgeAsync(context, userId, wasCompleted, progress.CompletedAt != null, cancellationToken);
    }

    /// <summary>
    /// Progress-driven edge adjustment used by the reading handler after the progress
    /// row itself has been updated: moves the current-year goal only when the
    /// completion state actually flipped.
    /// </summary>
    public static async Task ApplyGoalEdgeAsync(
        IApplicationDbContext context,
        Guid userId,
        bool wasCompleted,
        bool isCompleted,
        CancellationToken cancellationToken = default)
    {
        if (wasCompleted == isCompleted) return;

        var year = DateTime.UtcNow.Year;
        var goal = await context.ReadingGoals
            .FirstOrDefaultAsync(g => g.UserId == userId && g.Year == year, cancellationToken);

        if (goal == null) return;

        if (isCompleted)
            goal.IncrementCompleted();
        else
            goal.DecrementCompleted();
    }
}
