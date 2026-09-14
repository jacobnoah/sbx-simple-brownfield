// The seeded board.
//
// SHARED FILE - read-only for feature agents.
//
// Four lists and twenty tasks, written into the stores at process start and
// again whenever a test resets them.
//
// Dates are relative to <see cref="Today"/>, which is captured once when this
// type is initialised. That keeps "overdue", "due today" and "upcoming"
// meaningful forever instead of going stale, and keeps them deterministic
// within a run. Tests assert against offsets from Today, never against literal
// dates.

using Taskboard.Api.Domain;

namespace Taskboard.Api.Store;

/// <summary>The fixed board every process starts from.</summary>
internal static class SeedData
{
    /// <summary>The anchor every seeded date is relative to (UTC).</summary>
    public static DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Midnight UTC at the start of <see cref="Today"/>.</summary>
    public static DateTimeOffset Midnight { get; } = new(Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    /// <summary>The four seeded lists, ordered by id.</summary>
    public static IReadOnlyList<TaskList> Lists { get; } =
    [
        new("errands", "Errands", At(-60)),
        new("home", "Home", At(-90)),
        new(TaskList.InboxId, "Inbox", At(-120)),
        new("work", "Work", At(-90)),
    ];

    /// <summary>The twenty seeded tasks, ordered by id.</summary>
    public static IReadOnlyList<TaskItem> Tasks { get; } =
    [
        Seed("archive-old-invoices", "Archive old invoices", null, "work", TaskState.Todo, TaskPriority.Low, null, -30),
        Seed("book-dentist", "Book a dentist appointment", "Ask about the evening slots.", "home", TaskState.Todo, TaskPriority.Normal, 3, -12),
        Seed("buy-milk", "Buy milk", null, "errands", TaskState.Todo, TaskPriority.Low, 0, -1),
        Seed("cancel-gym-membership", "Cancel gym membership", null, "errands", TaskState.Cancelled, TaskPriority.Low, -5, -40),
        Seed("clean-the-gutters", "Clean the gutters", "Borrow the long ladder.", "home", TaskState.Blocked, TaskPriority.Normal, 21, -20),
        Seed("draft-q3-report", "Draft the Q3 report", "Numbers land on the 14th.", "work", TaskState.InProgress, TaskPriority.High, 5, -9),
        Seed("file-expenses", "File September expenses", null, "work", TaskState.Todo, TaskPriority.Normal, -2, -6),
        Seed("fix-leaking-tap", "Fix the leaking tap", null, "home", TaskState.Todo, TaskPriority.High, -9, -25),
        Seed("mail-birthday-card", "Mail the birthday card", null, "errands", TaskState.Done, TaskPriority.Normal, -3, -10, completedOffset: -3),
        Seed("order-printer-ink", "Order printer ink", null, "errands", TaskState.Todo, TaskPriority.Low, 14, -2),
        Seed("pay-council-tax", "Pay the council tax", "Direct debit failed last month.", "home", TaskState.Todo, TaskPriority.Urgent, -1, -15),
        Seed("plan-team-offsite", "Plan the team offsite", null, "work", TaskState.InProgress, TaskPriority.Normal, 30, -18),
        Seed("read-onboarding-docs", "Read the onboarding docs", null, TaskList.InboxId, TaskState.Todo, TaskPriority.Low, null, -4),
        Seed("renew-car-insurance", "Renew the car insurance", null, "home", TaskState.Todo, TaskPriority.High, 2, -22),
        Seed("renew-passport", "Renew the passport", "Photos are in the drawer.", "home", TaskState.Todo, TaskPriority.Urgent, 10, -35),
        Seed("reply-to-landlord", "Reply to the landlord", null, TaskList.InboxId, TaskState.Todo, TaskPriority.High, 0, -3),
        Seed("review-pull-requests", "Review the open pull requests", null, "work", TaskState.Todo, TaskPriority.High, 0, -1),
        Seed("submit-timesheet", "Submit the timesheet", null, "work", TaskState.Done, TaskPriority.Normal, -4, -8, completedOffset: -4),
        Seed("unsubscribe-newsletters", "Unsubscribe from the newsletters", null, TaskList.InboxId, TaskState.Todo, TaskPriority.Low, null, -50),
        Seed("water-the-plants", "Water the plants", null, "home", TaskState.Done, TaskPriority.Low, -1, -2, completedOffset: -1),
    ];

    private static DateTimeOffset At(int dayOffset) => Midnight.AddDays(dayOffset);

    private static TaskItem Seed(
        string id,
        string title,
        string? notes,
        string listId,
        TaskState status,
        TaskPriority priority,
        int? dueOffset,
        int createdOffset,
        int? completedOffset = null) =>
        new(
            id,
            title,
            notes,
            listId,
            status,
            priority,
            dueOffset is { } due ? Today.AddDays(due) : null,
            At(createdOffset),
            At(completedOffset ?? createdOffset),
            completedOffset is { } completed ? At(completed) : null);
}
