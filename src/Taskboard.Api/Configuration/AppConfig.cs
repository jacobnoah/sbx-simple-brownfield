// Strongly-typed application configuration, with defaults and validation.
//
// SHARED FILE - read-only for feature agents.
//
// Features read configuration by injecting IOptions<AppConfig> into an endpoint
// handler. Never inject IConfiguration, and never call
// Environment.GetEnvironmentVariable. See AGENTS.md.

using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Options;

namespace Taskboard.Api.Configuration;

/// <summary>Application settings, bound from the "App" configuration section.</summary>
public sealed class AppConfig
{
    /// <summary>Configuration section these settings bind from.</summary>
    public const string SectionName = "App";

    /// <summary>Route prefix every feature endpoint is mounted under.</summary>
    [Required]
    [RegularExpression("^/[A-Za-z0-9._~/-]*$", ErrorMessage = "FeatureRoutePrefix must start with '/'.")]
    public string FeatureRoutePrefix { get; init; } = "/api";

    /// <summary>Page size used when a request does not specify one.</summary>
    [Range(1, 1000)]
    public int DefaultPageSize { get; init; } = 25;

    /// <summary>Largest page size a request may ask for.</summary>
    [Range(1, 1000)]
    public int MaxPageSize { get; init; } = 100;

    /// <summary>Largest request body Kestrel will accept, in bytes.</summary>
    [Range(1024, 104_857_600)]
    public long MaxRequestBodyBytes { get; init; } = 1_048_576;
}

/// <summary>Cross-field validation that data annotations cannot express on their own.</summary>
internal sealed class AppConfigValidator : IValidateOptions<AppConfig>
{
    public ValidateOptionsResult Validate(string? name, AppConfig options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.MaxPageSize < options.DefaultPageSize)
        {
            failures.Add(
                $"{nameof(AppConfig.MaxPageSize)} ({options.MaxPageSize}) must be greater than or equal to " +
                $"{nameof(AppConfig.DefaultPageSize)} ({options.DefaultPageSize}).");
        }

        if (options.FeatureRoutePrefix.Length > 1 && options.FeatureRoutePrefix.EndsWith('/'))
        {
            failures.Add($"{nameof(AppConfig.FeatureRoutePrefix)} must not end with '/'.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
