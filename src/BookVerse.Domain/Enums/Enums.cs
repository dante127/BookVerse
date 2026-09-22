namespace BookVerse.Domain.Enums;

public enum UserStatus
{
    Active = 1,
    Inactive = 2,
    Suspended = 3
}

public enum BookStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2
}

public enum BookEditionFormat
{
    Hardcover = 1,
    Paperback = 2,
    Ebook = 3,
    Audiobook = 4
}

public enum AuthorRole
{
    Author = 1,
    CoAuthor = 2,
    Translator = 3,
    Editor = 4
}

public enum UserBookStatus
{
    WantToRead = 1,
    Reading = 2,
    Completed = 3,
    Paused = 4,
    Dropped = 5
}

public enum ReviewStatus
{
    Pending = 1,
    Published = 2,
    Rejected = 3,
    Hidden = 4
}

public enum NotificationType
{
    NewRecommendation = 1,
    ReviewApproved = 2,
    ReviewRejected = 3,
    ReadingGoalAchieved = 4,
    BookCompleted = 5,
    AuthorNewBook = 6,
    ReadingStreakMilestone = 7
}

public enum ReadingHistoryAction
{
    StartedBook = 1,
    ProgressUpdated = 2,
    CompletedBook = 3,
    PausedBook = 4,
    DroppedBook = 5
}
