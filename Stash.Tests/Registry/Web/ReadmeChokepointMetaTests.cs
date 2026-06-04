using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Stash.Registry.Web.Pages;
using Stash.Registry.Web.Rendering;
using Stash.Tests.Registry.Web.Fixtures;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// README chokepoint meta-test: scans every <c>.cshtml</c> file in the web project for
/// <c>@Html.Raw(...)</c> invocations and asserts that every such invocation is safe — i.e.
/// its argument is either:
/// <list type="bullet">
///   <item>A direct call to <c>IReadmeRenderer.RenderToSafeHtml(...)</c>, OR</item>
///   <item>A model property typed as <see cref="HtmlString"/> that is, by contract, populated
///   only from <see cref="IReadmeRenderer.RenderToSafeHtml"/>.</item>
/// </list>
/// Any other <c>@Html.Raw(...)</c> argument fails the test with a precise file:line location.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design:</b> Regex-based scan over the <c>.cshtml</c> source files. All patterns are
/// tested against the <see cref="ChokepointFailPathFixture"/> synthetic snippets so the scanner
/// is proven to have teeth (not a vacuous pass).
/// </para>
/// <para>
/// <b>Binding floor:</b> asserts at least <see cref="MinScannedCshtmlFiles"/> files were scanned
/// AND that the scan found at least one <c>@Html.Raw</c> call site (the README chokepoint itself)
/// so "0 violations because nothing was scanned / no Html.Raw found" fails loudly.
/// </para>
/// </remarks>
public sealed class ReadmeChokepointMetaTests
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimum number of <c>.cshtml</c> files expected in the project. This prevents
    /// a vacuous pass where the directory search finds nothing.
    /// </summary>
    private const int MinScannedCshtmlFiles = 5;

    // ── Regex patterns ────────────────────────────────────────────────────────

    /// <summary>
    /// Matches any <c>@Html.Raw(...)</c> call in a Razor view.
    /// Captures the full expression for further analysis.
    /// </summary>
    private static readonly Regex HtmlRawPattern = new(
        @"@Html\.Raw\((.+?)\)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Matches the SAFE form 1: the argument is a direct call to
    /// <c>[anything].RenderToSafeHtml(...)</c>.
    /// </summary>
    private static readonly Regex SafeRenderCallPattern = new(
        @"RenderToSafeHtml\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches the SAFE form 2: the argument is a model property access
    /// (e.g. <c>Model.ReadmeHtml</c> or <c>Model.SomeHtmlString</c>).
    /// Accepts: <c>Model.XYZ</c> where <c>XYZ</c> is a <see cref="HtmlString"/> property.
    /// The property type check is performed separately against the actual PageModel type.
    /// </summary>
    private static readonly Regex ModelPropertyPattern = new(
        @"^Model\.(\w+)\s*$",
        RegexOptions.Compiled);

    // ── Core scanner ──────────────────────────────────────────────────────────

    /// <summary>
    /// Scans a single <c>.cshtml</c> source file for <c>@Html.Raw(...)</c> violations.
    /// Returns a list of violation descriptions (empty = clean).
    /// </summary>
    /// <param name="cshtmlPath">Absolute path to the <c>.cshtml</c> file.</param>
    internal static IReadOnlyList<string> ScanFileForViolations(string cshtmlPath)
    {
        var violations = new List<string>();
        var lines = File.ReadAllLines(cshtmlPath);

        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx];
            var matches = HtmlRawPattern.Matches(line);
            foreach (Match match in matches)
            {
                string argument = match.Groups[1].Value.Trim();

                // Check SAFE form 1: direct RenderToSafeHtml call
                if (SafeRenderCallPattern.IsMatch(argument))
                    continue;

                // Check SAFE form 2: Model.PropertyName where property is HtmlString
                var modelMatch = ModelPropertyPattern.Match(argument);
                if (modelMatch.Success)
                {
                    string propertyName = modelMatch.Groups[1].Value;
                    if (IsHtmlStringModelProperty(cshtmlPath, propertyName))
                        continue;
                }

                // Neither safe form matched — this is a violation.
                violations.Add(
                    $"{Path.GetFileName(cshtmlPath)}:{lineIdx + 1}: @Html.Raw({argument}) — " +
                    "argument must be either IReadmeRenderer.RenderToSafeHtml(...) or a " +
                    "Model property typed as HtmlString populated only from the renderer.");
            }
        }

        return violations;
    }

    /// <summary>
    /// Determines whether <paramref name="propertyName"/> on the PageModel declared in
    /// <paramref name="cshtmlPath"/> is typed as <see cref="HtmlString"/>.
    /// Inspects the co-located <c>.cshtml.cs</c> for a <c>@model</c> directive, resolves
    /// the page model type via reflection, and checks the property's declared type.
    /// </summary>
    private static bool IsHtmlStringModelProperty(string cshtmlPath, string propertyName)
    {
        // Read the .cshtml to find the @model directive
        var content = File.ReadAllText(cshtmlPath);
        var modelMatch = Regex.Match(content, @"@model\s+([\w\.]+)");
        if (!modelMatch.Success)
            return false;

        string modelTypeName = modelMatch.Groups[1].Value;

        // Try to resolve the type from the web assembly
        var webAssembly = typeof(HealthModel).Assembly;

        // Try short name (e.g. "PackageModel") and namespaced name
        Type? modelType =
            webAssembly.GetType(modelTypeName) ??
            webAssembly.GetTypes().FirstOrDefault(t =>
                t.Name == modelTypeName ||
                t.FullName == modelTypeName ||
                (t.FullName?.EndsWith("." + modelTypeName, StringComparison.Ordinal) ?? false));

        if (modelType is null)
            return false;

        // Check the property's declared type is HtmlString (or HtmlString?)
        var property = modelType.GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance);

        if (property is null)
            return false;

        var propType = property.PropertyType;

        // Handle nullable value types: Nullable<HtmlString> — HtmlString is a struct
        if (propType.IsGenericType &&
            propType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            propType = propType.GetGenericArguments()[0];
        }

        return propType == typeof(HtmlString);
    }

    // ── Scan-snippet helper (used directly by self-tests) ────────────────────

    /// <summary>
    /// Scans an in-memory <c>.cshtml</c> snippet (as if it were a file) for violations.
    /// Uses a simplified check: considers <c>Model.X</c> safe only if the argument
    /// matches the <see cref="SafeRenderCallPattern"/> or is a known
    /// <c>Model.ReadmeHtml</c>-shaped property reference.
    /// </summary>
    /// <remarks>
    /// This overload is used by the fail-path self-tests in <see cref="ReadmeChokepointMetaTests"/>
    /// that need to verify the scanner trips on known-bad snippet text without a real file on disk.
    /// It performs only the regex-level check (no reflection-based property-type resolution).
    /// </remarks>
    internal static IReadOnlyList<string> ScanSnippetForViolations(
        string snippetText,
        bool treatModelHtmlStringPropertyAsSafe = false)
    {
        var violations = new List<string>();
        var lines = snippetText.Split('\n');

        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx];
            var matches = HtmlRawPattern.Matches(line);
            foreach (Match match in matches)
            {
                string argument = match.Groups[1].Value.Trim();

                // Safe form 1: direct RenderToSafeHtml call
                if (SafeRenderCallPattern.IsMatch(argument))
                    continue;

                // Safe form 2 (for snippet tests only): treat Model.X as safe when the
                // caller opts in (to test the HtmlString-property path specifically).
                if (treatModelHtmlStringPropertyAsSafe &&
                    ModelPropertyPattern.IsMatch(argument))
                    continue;

                // Violation
                violations.Add(
                    $"<snippet>:{lineIdx + 1}: @Html.Raw({argument}) — " +
                    "argument must be either IReadmeRenderer.RenderToSafeHtml(...) or a " +
                    "Model property typed as HtmlString populated only from the renderer.");
            }
        }

        return violations;
    }

    // ── Project directory discovery ───────────────────────────────────────────

    private static string FindWebProjectSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Stash.Registry.Web");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "Stash.Registry.Web.csproj")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot find Stash.Registry.Web/ project source directory — " +
            "test must run from within the repo.");
    }

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// Scans every <c>.cshtml</c> file in <c>Stash.Registry.Web</c> for
    /// <c>@Html.Raw(...)</c> violations. Asserts:
    /// <list type="number">
    ///   <item>At least <see cref="MinScannedCshtmlFiles"/> files were scanned (floor).</item>
    ///   <item>At least one <c>@Html.Raw</c> was found (confirms the chokepoint exists and
    ///   the scanner is not vacuously passing because it found nothing).</item>
    ///   <item>Zero violations — every <c>@Html.Raw</c> uses only approved argument forms.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void AllCshtmlFiles_HtmlRaw_OnlyUsedAtApprovedChokepoint()
    {
        string projectDir = FindWebProjectSourceDir();

        var cshtmlFiles = Directory
            .GetFiles(projectDir, "*.cshtml", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ── Binding floor: enough files found ────────────────────────────────
        Assert.True(
            cshtmlFiles.Count >= MinScannedCshtmlFiles,
            $"Expected to scan at least {MinScannedCshtmlFiles} .cshtml files in '{projectDir}', " +
            $"but found only {cshtmlFiles.Count}. " +
            "The project source directory may not have been resolved correctly.");

        // Scan all files and collect violations
        var allViolations = new List<string>();
        int htmlRawCallsFound = 0;

        foreach (var file in cshtmlFiles)
        {
            var fileContent = File.ReadAllText(file);
            if (HtmlRawPattern.IsMatch(fileContent))
                htmlRawCallsFound++;

            var violations = ScanFileForViolations(file);
            allViolations.AddRange(violations);
        }

        // ── Binding floor: at least one @Html.Raw found ───────────────────────
        // Without this, a future removal of the chokepoint site would pass vacuously.
        Assert.True(
            htmlRawCallsFound >= 1,
            $"Expected to find at least one @Html.Raw(...) call site across {cshtmlFiles.Count} " +
            ".cshtml files, but found zero. " +
            "The README chokepoint (Package.cshtml) may have been removed or the pattern " +
            "is not matching. This is a binding-floor failure — not a security pass.");

        // ── Zero violations ────────────────────────────────────────────────────
        Assert.True(
            allViolations.Count == 0,
            $"Found {allViolations.Count} @Html.Raw violation(s) in Stash.Registry.Web .cshtml files. " +
            "Every @Html.Raw argument must be either IReadmeRenderer.RenderToSafeHtml(...) " +
            "or a Model property typed as HtmlString populated only from the renderer:\n" +
            string.Join("\n", allViolations));
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    /// <summary>
    /// The scan helper MUST trip on the known-bad snippet from
    /// <see cref="ChokepointFailPathFixture.RogueHtmlRawSnippet"/>.
    /// </summary>
    [Fact]
    public void FailPathFixture_RogueHtmlRaw_IsDetected()
    {
        var violations = ScanSnippetForViolations(ChokepointFailPathFixture.RogueHtmlRawSnippet);

        Assert.True(
            violations.Count > 0,
            "The chokepoint scanner did NOT detect the rogue @Html.Raw(someUserControlledString) " +
            "in the known-bad fixture snippet. The guard has lost its teeth — " +
            "a real unsafe @Html.Raw would pass undetected.");
    }

    /// <summary>
    /// The scan helper must NOT flag a snippet using <c>renderer.RenderToSafeHtml(...)</c>
    /// as a violation (<see cref="ChokepointFailPathFixture.SafeRenderToSafeHtmlCallSnippet"/>).
    /// </summary>
    [Fact]
    public void FailPathFixture_SafeRenderToSafeHtmlCall_IsNotFlagged()
    {
        var violations = ScanSnippetForViolations(
            ChokepointFailPathFixture.SafeRenderToSafeHtmlCallSnippet);

        Assert.Empty(violations);
    }

    /// <summary>
    /// The scan helper must NOT flag a snippet using <c>Model.ReadmeHtml</c>
    /// (the HtmlString-property safe form) when the caller opts in to treating it as safe
    /// (<see cref="ChokepointFailPathFixture.SafeHtmlStringPropertySnippet"/>).
    /// </summary>
    [Fact]
    public void FailPathFixture_SafeHtmlStringProperty_IsNotFlagged_WhenTreatedAsSafe()
    {
        var violations = ScanSnippetForViolations(
            ChokepointFailPathFixture.SafeHtmlStringPropertySnippet,
            treatModelHtmlStringPropertyAsSafe: true);

        Assert.Empty(violations);
    }

    /// <summary>
    /// Binding floor: asserts that the known ReadmeHtml property on
    /// <see cref="PackageModel"/> is indeed typed as <see cref="HtmlString"/>
    /// (or <see cref="HtmlString"/>?). This is the reflection check that the
    /// production scanner runs when it finds <c>@Html.Raw(Model.ReadmeHtml)</c>.
    /// </summary>
    [Fact]
    public void PackageModel_ReadmeHtml_IsTypedAsHtmlString()
    {
        var property = typeof(PackageModel).GetProperty(
            "ReadmeHtml",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(property);

        // Accept HtmlString or HtmlString? (Nullable<HtmlString>)
        var propType = property!.PropertyType;
        if (propType.IsGenericType &&
            propType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            propType = propType.GetGenericArguments()[0];
        }

        Assert.Equal(typeof(HtmlString), propType);
    }
}
