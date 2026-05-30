namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Route binding helper that constructs an authoritative <see cref="PackageResource"/>
/// from URL path segments.
/// </summary>
/// <remarks>
/// All package write operations MUST use <see cref="From"/> to build the resource.
/// The manifest body is never permitted to supply the resource identity.
/// </remarks>
internal static class PackageRoute
{
    /// <summary>
    /// Creates a <see cref="PackageResource"/> from a route's <paramref name="scope"/>
    /// and <paramref name="name"/> segments after normalising them.
    /// </summary>
    /// <param name="scope">The scope segment from the URL path (without leading <c>@</c>).</param>
    /// <param name="name">The package name segment from the URL path.</param>
    public static PackageResource From(string scope, string name)
        => new(Sanitize(scope), Sanitize(name));

    /// <summary>Trims whitespace and lowercases a route segment.</summary>
    private static string Sanitize(string segment) => segment.Trim().ToLowerInvariant();
}
