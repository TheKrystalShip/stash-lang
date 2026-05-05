using System;

namespace Stash.Registry.Configuration;

/// <summary>
/// Validates and normalises the configured <c>Server.BasePath</c> value.
/// </summary>
public static class BasePathValidator
{
    /// <summary>
    /// Normalises a base-path configuration value. Returns an empty string when the
    /// value is unset (null, empty, or a single "/"). Otherwise returns the validated
    /// base path (must start with "/" and must not end with "/").
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the supplied value is non-empty but malformed.
    /// </exception>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "/")
        {
            return "";
        }

        if (!value.StartsWith('/'))
        {
            throw new InvalidOperationException(
                $"Invalid Registry:Server:BasePath value '{value}': must start with '/' (e.g. '/registry').");
        }

        if (value.EndsWith('/'))
        {
            throw new InvalidOperationException(
                $"Invalid Registry:Server:BasePath value '{value}': must not end with '/' (e.g. use '/registry' instead of '/registry/').");
        }

        return value;
    }
}
