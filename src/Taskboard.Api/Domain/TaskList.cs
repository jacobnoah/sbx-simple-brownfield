// The domain model: a list that groups tasks.
//
// SHARED FILE - read-only for feature agents. See AGENTS.md.

namespace Taskboard.Api.Domain;

/// <summary>A named grouping of tasks. Every task belongs to exactly one.</summary>
/// <param name="Id">Stable slug, lower-case kebab-case. Used in URLs.</param>
/// <param name="Name">Display name, 1-40 characters.</param>
/// <param name="CreatedAt">When the list was created, UTC.</param>
public sealed record TaskList(string Id, string Name, DateTimeOffset CreatedAt)
{
    /// <summary>
    /// Id of the list that always exists. It cannot be deleted, and it is where
    /// a task lands when it is created without a list and when the list it was
    /// on is deleted.
    /// </summary>
    public const string InboxId = "inbox";
}
