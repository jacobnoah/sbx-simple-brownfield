// Limit/offset paging, validated against AppConfig.
//
// SHARED FILE - read-only for feature agents.
//
// Every endpoint that returns a Page<T> parses its paging parameters through
// PageRequest.From, so "limit" and "offset" mean the same thing and fail the
// same way everywhere. See AGENTS.md.

using Taskboard.Api.Configuration;
using Taskboard.Api.Contracts;
using Taskboard.Api.Errors;

namespace Taskboard.Api.Routing;

/// <summary>A validated limit/offset pair.</summary>
/// <param name="Limit">Page size, between 1 and <see cref="AppConfig.MaxPageSize"/>.</param>
/// <param name="Offset">Items to skip, zero or more.</param>
public readonly record struct PageRequest(int Limit, int Offset)
{
    /// <summary>
    /// Validates raw query values. A null <paramref name="limit"/> falls back to
    /// <see cref="AppConfig.DefaultPageSize"/> and a null <paramref name="offset"/>
    /// to zero; anything out of range throws <see cref="AppException.BadRequest"/>.
    /// </summary>
    public static PageRequest From(int? limit, int? offset, AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var resolvedLimit = limit ?? config.DefaultPageSize;
        var resolvedOffset = offset ?? 0;

        if (resolvedLimit < 1 || resolvedLimit > config.MaxPageSize)
        {
            throw AppException.BadRequest(
                $"limit must be between 1 and {config.MaxPageSize}.",
                new { limit = resolvedLimit, maxPageSize = config.MaxPageSize });
        }

        if (resolvedOffset < 0)
        {
            throw AppException.BadRequest("offset must be zero or greater.", new { offset = resolvedOffset });
        }

        return new PageRequest(resolvedLimit, resolvedOffset);
    }

    /// <summary>
    /// Slices <paramref name="items"/> onto this page. <c>Total</c> is the count
    /// before paging, never the size of the page.
    /// </summary>
    public Page<T> Apply<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new Page<T>([.. items.Skip(Offset).Take(Limit)], items.Count, Limit, Offset);
    }
}
