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

    /// <summary>
    /// The exhaustive allow-list of <c>Model.X</c> property names that are approved for use
    /// inside <c>@Html.Raw(Model.X)</c>. Every entry must be a <see cref="HtmlString"/>
    /// (or <see cref="HtmlString"/>?) property whose value is populated <b>only</b> from
    /// <see cref="IReadmeRenderer.RenderToSafeHtml"/>. See <see cref="IsApprovedModelProperty"/>.
    /// <para>
    /// <b>Upgrade path (Roslyn provenance):</b> if the chokepoint surface grows beyond one or two
    /// properties, replace this allow-list with a Roslyn CSharpSyntaxTree walk over the
    /// corresponding <c>.cshtml.cs</c> file — assert the property's getter returns only the
    /// result of a <c>RenderToSafeHtml(…)</c> call, not just that the declared type is
    /// <see cref="HtmlString"/>. The allow-list is the right cost/complexity trade-off for Phase 2
    /// with a single chokepoint site.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ApprovedHtmlRawProperties =
        new(StringComparer.Ordinal) { "ReadmeHtml" };

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
    /// <remarks>
    /// Reads the file as a single string so that multi-line <c>@Html.Raw(\n  expr\n)</c>
    /// invocations (valid Razor) are matched by <see cref="RegexOptions.Singleline"/> on
    /// <see cref="HtmlRawPattern"/>. The line number of each match is computed from the
    /// number of newlines that precede <see cref="Match.Index"/>.
    /// </remarks>
    /// <param name="cshtmlPath">Absolute path to the <c>.cshtml</c> file.</param>
    internal static IReadOnlyList<string> ScanFileForViolations(string cshtmlPath)
    {
        var violations = new List<string>();
        var content = File.ReadAllText(cshtmlPath);

        foreach (Match match in HtmlRawPattern.Matches(content))
        {
            string argument = match.Groups[1].Value.Trim();

            // Check SAFE form 1: direct RenderToSafeHtml call
            if (SafeRenderCallPattern.IsMatch(argument))
                continue;

            // Check SAFE form 2: Model.PropertyName where property is on the allow-list
            // AND is typed as HtmlString (two necessary conditions — type alone is not
            // sufficient to prove the value was produced by the renderer).
            var modelMatch = ModelPropertyPattern.Match(argument);
            if (modelMatch.Success)
            {
                string propertyName = modelMatch.Groups[1].Value;
                if (IsApprovedModelProperty(cshtmlPath, propertyName))
                    continue;
            }

            // Neither safe form matched — this is a violation.
            int lineNumber = content[..match.Index].Count(c => c == '\n') + 1;
            violations.Add(
                $"{Path.GetFileName(cshtmlPath)}:{lineNumber}: @Html.Raw({argument}) — " +
                "argument must be either IReadmeRenderer.RenderToSafeHtml(...) or a " +
                "Model property typed as HtmlString populated only from the renderer.");
        }

        return violations;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="propertyName"/> is approved for use
    /// inside <c>@Html.Raw(Model.X)</c>: it must appear in <see cref="ApprovedHtmlRawProperties"/>
    /// AND be declared as <see cref="HtmlString"/> (or <see cref="HtmlString"/>?) on the
    /// page model type identified by the <c>@model</c> directive in <paramref name="cshtmlPath"/>.
    /// Both conditions must hold — the type check alone is insufficient because a future property
    /// could be typed as <see cref="HtmlString"/> while being populated from raw user input.
    /// </summary>
    private static bool IsApprovedModelProperty(string cshtmlPath, string propertyName)
    {
        // Only proceed if the property name is on the approved allow-list.
        if (!ApprovedHtmlRawProperties.Contains(propertyName))
            return false;

        return IsHtmlStringModelProperty(cshtmlPath, propertyName);
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

    // ── Self-tests (scanner has teeth — all using the production ScanFileForViolations) ──

    /// <summary>
    /// The PRODUCTION scanner (<see cref="ScanFileForViolations"/>) MUST trip on a real
    /// <c>.cshtml</c> file containing a rogue <c>@Html.Raw(someUserControlledString)</c>.
    /// This exercises the actual guard that runs in production compliance, not a parallel helper.
    /// </summary>
    [Fact]
    public void FailPathFixture_RogueHtmlRaw_IsDetected_ByProductionScanner()
    {
        // Write the known-bad snippet to a real temp .cshtml file so the production scanner runs.
        string tempFile = Path.Combine(Path.GetTempPath(), $"chokepoint-rogue-{Guid.NewGuid():N}.cshtml");
        try
        {
            File.WriteAllText(tempFile, ChokepointFailPathFixture.RogueHtmlRawSnippet);

            var violations = ScanFileForViolations(tempFile);

            Assert.True(
                violations.Count > 0,
                "The PRODUCTION chokepoint scanner (ScanFileForViolations) did NOT detect " +
                "the rogue @Html.Raw(someUserControlledString) in the known-bad fixture file. " +
                "The guard has lost its teeth — a real unsafe @Html.Raw would pass undetected.");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// The PRODUCTION scanner must NOT flag a <c>.cshtml</c> file whose <c>@Html.Raw</c>
    /// argument is a direct <c>renderer.RenderToSafeHtml(...)</c> call — safe form 1.
    /// </summary>
    [Fact]
    public void FailPathFixture_SafeRenderToSafeHtmlCall_IsNotFlagged_ByProductionScanner()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"chokepoint-safe-call-{Guid.NewGuid():N}.cshtml");
        try
        {
            File.WriteAllText(tempFile, ChokepointFailPathFixture.SafeRenderToSafeHtmlCallSnippet);

            var violations = ScanFileForViolations(tempFile);

            Assert.Empty(violations);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// The PRODUCTION scanner must flag a <c>.cshtml</c> file whose <c>@Html.Raw</c> argument
    /// is <c>Model.InstallCommand</c> — a <c>string</c> property on <see cref="PackageModel"/>,
    /// NOT an <see cref="HtmlString"/>. This exercises the full reflection path (safe form 2
    /// reject branch) and proves a non-HtmlString property triggers a violation.
    /// </summary>
    [Fact]
    public void FailPathFixture_ModelStringProperty_IsFlagged_ByProductionScanner()
    {
        // A .cshtml that declares @model PackageModel and calls @Html.Raw(Model.InstallCommand),
        // where InstallCommand is string (not HtmlString) — must be flagged.
        const string snippet = """
            @page "/test"
            @model Stash.Registry.Web.Pages.PackageModel
            <div>
                @Html.Raw(Model.InstallCommand)
            </div>
            """;

        string tempFile = Path.Combine(Path.GetTempPath(), $"chokepoint-str-prop-{Guid.NewGuid():N}.cshtml");
        try
        {
            File.WriteAllText(tempFile, snippet);

            var violations = ScanFileForViolations(tempFile);

            Assert.True(
                violations.Count > 0,
                "The PRODUCTION chokepoint scanner did NOT flag @Html.Raw(Model.InstallCommand), " +
                "where InstallCommand is typed as string — not HtmlString. " +
                "The reflection-based reject branch has lost its teeth.");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// The PRODUCTION scanner must NOT flag a <c>.cshtml</c> file whose <c>@Html.Raw</c>
    /// argument is <c>Model.ReadmeHtml</c> — the real <see cref="HtmlString"/>? property on
    /// <see cref="PackageModel"/>. This exercises the full reflection path (safe form 2 accept
    /// branch) and proves the real chokepoint is recognized as safe.
    /// </summary>
    [Fact]
    public void ProductionPackageCshtml_ModelReadmeHtml_IsNotFlagged_ByProductionScanner()
    {
        // Mirror the actual Package.cshtml @model declaration so the production scanner resolves
        // the PackageModel type and finds ReadmeHtml : HtmlString?.
        const string snippet = """
            @page "/packages/@{scope}/{name}"
            @model Stash.Registry.Web.Pages.PackageModel
            <div>
                @Html.Raw(Model.ReadmeHtml)
            </div>
            """;

        string tempFile = Path.Combine(Path.GetTempPath(), $"chokepoint-safe-prop-{Guid.NewGuid():N}.cshtml");
        try
        {
            File.WriteAllText(tempFile, snippet);

            var violations = ScanFileForViolations(tempFile);

            Assert.Empty(violations);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// The PRODUCTION scanner MUST flag a multi-line <c>@Html.Raw(\n  expr\n)</c> invocation —
    /// valid Razor that spans three lines. This exercises gap-1 coverage: the whole-file
    /// <c>ReadAllText</c> + <c>RegexOptions.Singleline</c> path must match across newlines.
    /// A per-line scan would see three innocent lines and miss this entirely.
    /// </summary>
    [Fact]
    public void FailPathFixture_MultilineHtmlRaw_IsDetected_ByProductionScanner()
    {
        const string snippet = """
            @page "/multi"
            @model BadModel
            <div>
                @Html.Raw(
                    someUnsafeExpression
                )
            </div>
            """;

        string tempFile = Path.Combine(Path.GetTempPath(), $"chokepoint-multiline-{Guid.NewGuid():N}.cshtml");
        try
        {
            File.WriteAllText(tempFile, snippet);

            var violations = ScanFileForViolations(tempFile);

            Assert.True(
                violations.Count > 0,
                "The PRODUCTION chokepoint scanner did NOT detect a multi-line " +
                "@Html.Raw(\\n  someUnsafeExpression\\n) — a per-line scan would miss this. " +
                "The whole-file ReadAllText + RegexOptions.Singleline path has lost its teeth.");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// The PRODUCTION scanner MUST flag <c>@Html.Raw(Model.FutureEvil)</c> even when the
    /// property is typed as <see cref="HtmlString"/> — because <c>FutureEvil</c> is not on the
    /// <see cref="ApprovedHtmlRawProperties"/> allow-list. This exercises gap-2 coverage:
    /// provenance-via-allow-list must reject a non-approved HtmlString property.
    /// </summary>
    [Fact]
    public void FailPathFixture_NonApprovedHtmlStringProperty_IsFlagged_ByProductionScanner()
    {
        // PackageModel has a HtmlString? property called ReadmeHtml.  We manufacture a .cshtml
        // that references a DIFFERENT name that is NOT on the allow-list.  Even if someone added
        // a HtmlString property with that name, the allow-list gate must still reject it.
        const string snippet = """
            @page "/test"
            @model Stash.Registry.Web.Pages.PackageModel
            <div>
                @Html.Raw(Model.FutureEvil)
            </div>
            """;

        string tempFile = Path.Combine(Path.GetTempPath(), $"chokepoint-non-approved-{Guid.NewGuid():N}.cshtml");
        try
        {
            File.WriteAllText(tempFile, snippet);

            var violations = ScanFileForViolations(tempFile);

            Assert.True(
                violations.Count > 0,
                "The PRODUCTION chokepoint scanner did NOT flag @Html.Raw(Model.FutureEvil), " +
                "which is not on the ApprovedHtmlRawProperties allow-list. " +
                "The allow-list guard (gap-2 fix) has lost its teeth.");
        }
        finally
        {
            File.Delete(tempFile);
        }
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
