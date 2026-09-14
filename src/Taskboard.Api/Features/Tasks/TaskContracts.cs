// Request contracts owned by the Tasks feature.

using System.Globalization;
using System.Text.Json;

using Taskboard.Api.Domain;
using Taskboard.Api.Errors;

namespace Taskboard.Api.Features.Tasks;

/// <summary>Body of <c>POST /tasks</c>.</summary>
/// <param name="Title">Required, trimmed, 1-120 characters.</param>
/// <param name="Notes">Optional free text, trimmed, up to 2000 characters.</param>
/// <param name="ListId">Optional list to file the task under. Defaults to the inbox.</param>
/// <param name="Priority">Optional priority name. Defaults to <c>normal</c>.</param>
/// <param name="DueOn">Optional due date.</param>
public sealed record CreateTaskRequest(
    string? Title,
    string? Notes,
    string? ListId,
    TaskPriority? Priority,
    DateOnly? DueOn);

/// <summary>
/// Body of <c>PATCH /tasks/{taskId}</c>, parsed field by field so that "absent"
/// and "null" stay different things: omitting <c>notes</c> leaves it alone,
/// sending <c>"notes": null</c> clears it.
/// </summary>
internal sealed class TaskPatch
{
    private static readonly string[] KnownFields =
        ["title", "notes", "listId", "priority", "dueOn", "status"];

    public bool HasTitle { get; private init; }

    public string? Title { get; private init; }

    public bool HasNotes { get; private init; }

    public string? Notes { get; private init; }

    public bool HasListId { get; private init; }

    public string? ListId { get; private init; }

    public bool HasPriority { get; private init; }

    public TaskPriority Priority { get; private init; }

    public bool HasDueOn { get; private init; }

    public DateOnly? DueOn { get; private init; }

    public bool HasStatus { get; private init; }

    public TaskState Status { get; private init; }

    /// <summary>Reads a patch document, rejecting anything it does not understand.</summary>
    public static TaskPatch Parse(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw AppException.BadRequest("The request body must be a JSON object.");
        }

        var unknown = body.EnumerateObject()
            .Select(property => property.Name)
            .Where(name => !KnownFields.Contains(name, StringComparer.Ordinal))
            .ToList();

        if (unknown.Count > 0)
        {
            throw AppException.BadRequest("The request body contains unknown fields.", new { fields = unknown });
        }

        var hasTitle = body.TryGetProperty("title", out var title);
        var hasNotes = body.TryGetProperty("notes", out var notes);
        var hasListId = body.TryGetProperty("listId", out var listId);
        var hasPriority = body.TryGetProperty("priority", out var priority);
        var hasDueOn = body.TryGetProperty("dueOn", out var dueOn);
        var hasStatus = body.TryGetProperty("status", out var status);

        return new TaskPatch
        {
            HasTitle = hasTitle,
            Title = ReadString(title, "title"),
            HasNotes = hasNotes,
            Notes = ReadString(notes, "notes"),
            HasListId = hasListId,
            ListId = ReadString(listId, "listId"),
            HasPriority = hasPriority,
            Priority = ReadEnum<TaskPriority>(priority, "priority"),
            HasDueOn = hasDueOn,
            DueOn = ReadDate(dueOn, "dueOn"),
            HasStatus = hasStatus,
            Status = ReadEnum<TaskState>(status, "status"),
        };
    }

    /// <summary>True when the document carried no recognised field at all.</summary>
    public bool IsEmpty =>
        !HasTitle && !HasNotes && !HasListId && !HasPriority && !HasDueOn && !HasStatus;

    private static string? ReadString(JsonElement value, string field) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        _ => throw AppException.BadRequest($"{field} must be a string or null."),
    };

    private static DateOnly? ReadDate(JsonElement value, string field) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        JsonValueKind.String when DateOnly.TryParse(
            value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed) => parsed,
        _ => throw AppException.BadRequest($"{field} must be an ISO-8601 date (yyyy-MM-dd) or null."),
    };

    private static TEnum ReadEnum<TEnum>(JsonElement value, string field)
        where TEnum : struct, Enum
    {
        if (value.ValueKind is JsonValueKind.Undefined)
        {
            return default;
        }

        if (value.ValueKind is JsonValueKind.String &&
            Enum.TryParse<TEnum>(value.GetString(), ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw AppException.BadRequest(
            $"{field} must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
    }
}
