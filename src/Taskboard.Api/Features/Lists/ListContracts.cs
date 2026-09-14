// Contracts owned by the Lists feature.

namespace Taskboard.Api.Features.Lists;

/// <summary>Body of <c>POST /lists</c>.</summary>
/// <param name="Name">Required, trimmed, 1-40 characters.</param>
public sealed record CreateListRequest(string? Name);

/// <summary>A list as the API reports it, with its task counts folded in.</summary>
/// <param name="Id">Stable slug, used in URLs.</param>
/// <param name="Name">Display name.</param>
/// <param name="CreatedAt">When the list was created, UTC.</param>
/// <param name="TaskCount">Tasks filed under this list, whatever their status.</param>
/// <param name="OpenTaskCount">Tasks that are neither done nor cancelled.</param>
public sealed record TaskListSummary(
    string Id,
    string Name,
    DateTimeOffset CreatedAt,
    int TaskCount,
    int OpenTaskCount);
