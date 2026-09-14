// Deriving the kebab-case ids that tasks and lists are addressed by.
//
// SHARED FILE - read-only for feature agents.
//
// Both existing features need this, which is why it lives here rather than in
// one of them. Uniqueness is not this type's problem: a caller that needs a free
// id checks its own store. See AGENTS.md.

using System.Text;

namespace Taskboard.Api.Domain;

/// <summary>Turns a human title into a url-safe id.</summary>
public static class Slug
{
    /// <summary>Longest slug this will produce.</summary>
    public const int MaxLength = 60;

    /// <summary>
    /// Lower-cases, drops everything that is not an ASCII letter or digit,
    /// collapses the gaps to single dashes and trims the ends. Returns an empty
    /// string when nothing survives.
    /// </summary>
    public static string From(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);
        var pendingDash = false;

        foreach (var character in text)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }

            if (builder.Length >= MaxLength)
            {
                break;
            }
        }

        return builder.ToString().Trim('-');
    }
}
