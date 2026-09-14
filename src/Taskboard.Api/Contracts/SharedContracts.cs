// Contracts shared by two or more features.
//
// SHARED FILE - read-only for feature agents.
//
// Only add a type here if TWO OR MORE features need it. A contract used by a
// single feature belongs in that feature's own folder. See AGENTS.md.

namespace Taskboard.Api.Contracts;

/// <summary>Payload returned by the built-in health endpoint.</summary>
public sealed record HealthPayload(string Status, long UptimeSeconds, DateTimeOffset Timestamp);

/// <summary>Envelope for endpoints that return a page of items.</summary>
/// <param name="Items">The items on this page.</param>
/// <param name="Total">Total matching items, before paging is applied.</param>
/// <param name="Limit">Page size that was applied.</param>
/// <param name="Offset">Number of items skipped.</param>
/// <typeparam name="T">Item type, owned by the calling feature.</typeparam>
public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int Limit, int Offset);
