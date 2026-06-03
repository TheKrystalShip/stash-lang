using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stash.Registry.Contracts;
using Xunit;

namespace Stash.Tests.Registry.Validation;

/// <summary>
/// Roslyn-based meta-test that fails if any controller action in
/// <c>Stash.Registry/Controllers/</c> bypasses ASP.NET Core's declarative model-binding
/// pipeline by either:
/// <list type="bullet">
///   <item><b>Sink (a)</b> — calling <c>JsonSerializer.DeserializeAsync&lt;T&gt;(Request.Body, ...)</c> directly.</item>
///   <item><b>Sink (b)</b> — declaring a public action method that accepts a parameter typed in the
///     <c>Stash.Registry.Contracts</c> namespace without a <c>[FromBody]</c>, <c>[FromQuery]</c>,
///     <c>[FromForm]</c>, or <c>[FromRoute]</c> binding-source attribute.</item>
///   <item><b>Sink (c)</b> — reading <c>Request.Query</c> directly from within a public action method
///     body (indicating a missing <c>[FromQuery]</c> DTO parameter).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// The production compliance test asserts the live violation set equals a pinned
/// <see cref="KnownExemptions"/> set seeded with the twelve currently-bypassing actions.
/// As later phases migrate endpoints to declarative binding, each migration removes an
/// entry from the pin, so the test goes RED until the pin is also updated — forcing the
/// pin to track reality and making silent regressions impossible.
/// </para>
/// <para>
/// The metadata references are built from <c>TRUSTED_PLATFORM_ASSEMBLIES</c> plus
/// <c>Stash.Registry.Contracts</c>, <c>System.ComponentModel.DataAnnotations</c>, and
/// <c>Microsoft.AspNetCore.Mvc.Core</c> — so the Roslyn semantic model can resolve
/// binding-source attribute symbols and contracts types. The reference set is
/// load-order-deterministic; it never uses <c>AppDomain.CurrentDomain.GetAssemblies()</c>.
/// </para>
/// <para>
/// A binding-floor probe asserts a known <c>Stash.Registry.Contracts</c> type
/// (<see cref="AssignRoleRequest"/>) AND <c>FromBodyAttribute</c> both resolve to non-error
/// symbols, so a vacuous scan (0 violations because nothing bound) fails loudly instead of
/// masquerading as "clean".
/// </para>
/// <para>
/// Self-tests prove the scanner has teeth: a positive fixture for sinks (a) and (c)
/// and a negative fixture confirming a clean <c>[FromBody]</c> action is not flagged.
/// </para>
/// </remarks>
public sealed class RequestModelBindingMetaTests
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// The namespace whose types trigger sink (b) checks on action-method parameters.
    /// Any parameter of a type declared here must carry a binding-source attribute.
    /// </summary>
    private const string ContractsNamespace = "Stash.Registry.Contracts";

    /// <summary>
    /// ASP.NET Core MVC binding-source attribute short names that satisfy sink (b).
    /// </summary>
    private static readonly HashSet<string> BindingSourceAttributes = new(StringComparer.Ordinal)
    {
        "FromBody",
        "FromBodyAttribute",
        "FromQuery",
        "FromQueryAttribute",
        "FromForm",
        "FromFormAttribute",
        "FromRoute",
        "FromRouteAttribute",
    };

    /// <summary>
    /// The containing namespace for ASP.NET Core MVC binding-source attributes checked
    /// via the semantic model. Only parameter attributes in this namespace count.
    /// </summary>
    private const string MvcNamespace = "Microsoft.AspNetCore.Mvc";

    /// <summary>
    /// Minimum number of controller <c>.cs</c> files that must be scanned. Guards against
    /// a vacuous pass when repo-root or path discovery regresses. There are currently six
    /// production controllers.
    /// </summary>
    private const int MinScannedFiles = 6;

    /// <summary>
    /// The pinned set of currently-bypassing actions, formatted as
    /// <c>"{ControllerBaseName}.{ActionMethodName}"</c> (no "Controller" suffix).
    /// <para>
    /// This set is seeded with all twelve actions at P1 and shrinks to empty by P5.
    /// Each migration phase removes the migrated actions from this set and also removes
    /// their corresponding <c>DeserializeAsync</c> / <c>Request.Query</c> calls.
    /// </para>
    /// </summary>
    private static readonly IReadOnlySet<string> KnownExemptions = new HashSet<string>(StringComparer.Ordinal)
    {
        // Admin controller — 2 body-deserialization bypasses + 1 query bypass (P5 will migrate these)
        "Admin.CreateUser",
        "Admin.AdminAssignRole",
        "Admin.GetAuditLog",

        // Scopes controller — 1 body-deserialization bypass (P5 will migrate this)
        "Scopes.ClaimScope",
    };

    // ── Repo-root discovery ───────────────────────────────────────────────────

    /// <summary>
    /// Walks up from the test-assembly directory to find the repo root that contains
    /// <c>Stash.Registry/Controllers/</c>, then returns that controllers directory.
    /// </summary>
    private static string FindControllersDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Stash.Registry", "Controllers");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot find Stash.Registry/Controllers/ — test must run from within the repo.");
    }

    // ── Metadata references ───────────────────────────────────────────────────

    /// <summary>
    /// Builds a load-order-deterministic set of metadata references for a Roslyn compilation.
    /// Uses <c>TRUSTED_PLATFORM_ASSEMBLIES</c> (the full framework reference closure) plus
    /// the three additional assemblies required to resolve the types we check:
    /// <list type="bullet">
    ///   <item><c>Stash.Registry.Contracts</c> — for binding-source checks on parameter types.</item>
    ///   <item><c>System.ComponentModel.DataAnnotations</c> — because contracts types carry validation attributes.</item>
    ///   <item><c>Microsoft.AspNetCore.Mvc.Core</c> — for resolving <c>[FromBody]</c> / <c>[FromQuery]</c> etc.</item>
    /// </list>
    /// De-duplicated by path so TPA entries are never double-added.
    /// </summary>
    private static MetadataReference[] BuildMetadataReferences()
    {
        var tpaPaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !string.IsNullOrEmpty(p));

        var extraPaths = new[]
        {
            // Contracts assembly — the primary type-resolution target for sink (b).
            typeof(AssignRoleRequest).Assembly.Location,
            // DataAnnotations — contracts types carry [MinLength] and similar attributes.
            typeof(System.ComponentModel.DataAnnotations.MinLengthAttribute).Assembly.Location,
            // Mvc.Core — needed to resolve [FromBody], [FromQuery], [FromForm], [FromRoute].
            typeof(Microsoft.AspNetCore.Mvc.FromBodyAttribute).Assembly.Location,
        };

        return tpaPaths
            .Concat(extraPaths)
            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            .Distinct(StringComparer.Ordinal)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();
    }

    /// <summary>
    /// Asserts that the metadata references produced by <see cref="BuildMetadataReferences"/>
    /// can resolve both a known <c>Stash.Registry.Contracts</c> type and
    /// <c>Microsoft.AspNetCore.Mvc.FromBodyAttribute</c>. A vacuous scan (refs insufficient →
    /// 0 violations, which is always "passing") is worse than a failing scan, so we fail
    /// loudly here rather than silently.
    /// </summary>
    private static void AssertBindingFloor(MetadataReference[] refs)
    {
        var probe = CSharpCompilation.Create(
            "__BindingFloorProbe__",
            syntaxTrees: [],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var contractsType = probe.GetTypeByMetadataName("Stash.Registry.Contracts.AssignRoleRequest");
        Assert.True(
            contractsType != null && contractsType.TypeKind != TypeKind.Error,
            "Meta-test reference set cannot bind Stash.Registry.Contracts types — " +
            "the scan would be vacuous (0 violations is meaningless). " +
            "Fix BuildMetadataReferences() so it can resolve AssignRoleRequest. " +
            $"Resolved: {contractsType?.ToDisplayString() ?? "<null>"}, TypeKind: {contractsType?.TypeKind.ToString() ?? "N/A"}");

        var fromBodyType = probe.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromBodyAttribute");
        Assert.True(
            fromBodyType != null && fromBodyType.TypeKind != TypeKind.Error,
            "Meta-test reference set cannot bind Microsoft.AspNetCore.Mvc.FromBodyAttribute — " +
            "sink (b) parameter checks would be vacuous. " +
            "Fix BuildMetadataReferences() so it can resolve FromBodyAttribute. " +
            $"Resolved: {fromBodyType?.ToDisplayString() ?? "<null>"}, TypeKind: {fromBodyType?.TypeKind.ToString() ?? "N/A"}");
    }

    // ── Scanner ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans all controller <c>.cs</c> files and returns the set of violating action
    /// names (as <c>"{ControllerBaseName}.{ActionMethodName}"</c>) plus the file count.
    /// </summary>
    private static (HashSet<string> Violations, int ScannedFiles) ScanControllers(string controllersDir)
    {
        var csFiles = Directory.EnumerateFiles(controllersDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                string rel = Path.GetFileName(f);
                // Skip compiler output and non-controller files
                return !rel.StartsWith(".", StringComparison.Ordinal);
            })
            .ToList();

        var violations = new HashSet<string>(StringComparer.Ordinal);
        var refs = BuildMetadataReferences();

        foreach (string filePath in csFiles)
        {
            string source = File.ReadAllText(filePath);
            string label = Path.GetFileName(filePath);
            ScanFile(source, label, refs, violations);
        }

        return (violations, csFiles.Count);
    }

    /// <summary>
    /// Scans a single C# source file. Flags public action methods on <c>ControllerBase</c>
    /// subclasses that satisfy any of the three sink shapes:
    /// <list type="bullet">
    ///   <item><b>(a)</b> Call to <c>JsonSerializer.DeserializeAsync&lt;T&gt;(Request.Body, ...)</c> anywhere in the method body.</item>
    ///   <item><b>(b)</b> A Contracts-typed parameter without a binding-source attribute (<c>[FromBody]</c>, <c>[FromQuery]</c>, <c>[FromForm]</c>, <c>[FromRoute]</c>).</item>
    ///   <item><b>(c)</b> Any access to <c>Request.Query</c> in the method body.</item>
    /// </list>
    /// Violations are added to <paramref name="violations"/> as
    /// <c>"{ControllerBaseName}.{ActionMethodName}"</c>.
    /// </summary>
    private static void ScanFile(
        string source,
        string label,
        MetadataReference[] refs,
        HashSet<string> violations)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "__ScanAssembly__",
            syntaxTrees: [tree],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetCompilationUnitRoot();

        // Enumerate all class declarations in the file that look like controllers.
        // We only care about public non-abstract classes; we rely on the naming
        // convention ("Controller" suffix) rather than a full type-hierarchy walk.
        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            string className = classDecl.Identifier.Text;
            if (!className.EndsWith("Controller", StringComparison.Ordinal))
                continue;

            // Derive the base name used in the exemption/violation key ("Auth", "Admin", …).
            string controllerBase = className[..^"Controller".Length];

            // Walk public instance methods that look like action methods.
            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                // Only public, non-static methods with a body are action methods.
                bool isPublic = method.Modifiers.Any(m =>
                    m.IsKind(SyntaxKind.PublicKeyword));
                bool isStatic = method.Modifiers.Any(m =>
                    m.IsKind(SyntaxKind.StaticKeyword));

                if (!isPublic || isStatic || method.Body == null && method.ExpressionBody == null)
                    continue;

                string methodName = method.Identifier.Text;

                // ── Sink (a): DeserializeAsync<T>(Request.Body, ...) ──────────
                if (HasDeserializeAsyncRequestBody(method))
                {
                    violations.Add($"{controllerBase}.{methodName}");
                    continue; // one violation per action is enough
                }

                // ── Sink (b): Contracts-typed parameter without binding attr ──
                if (HasUnboundContractsParameter(method, model))
                {
                    violations.Add($"{controllerBase}.{methodName}");
                    continue;
                }

                // ── Sink (c): Direct Request.Query access ─────────────────────
                if (HasDirectRequestQueryAccess(method))
                {
                    violations.Add($"{controllerBase}.{methodName}");
                    continue;
                }
            }
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if the method body contains an invocation of
    /// <c>JsonSerializer.DeserializeAsync</c> where the first argument is
    /// <c>Request.Body</c>.
    /// </summary>
    private static bool HasDeserializeAsyncRequestBody(MethodDeclarationSyntax method)
    {
        // Walk all invocations in the method body and return true if we find
        // a call to DeserializeAsync with Request.Body as the first argument.
        var bodyNode = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        if (bodyNode == null) return false;

        foreach (var invocation in bodyNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            // Match: <something>.DeserializeAsync<T>(...)
            if (invocation.Expression is not MemberAccessExpressionSyntax ma)
                continue;
            if (!string.Equals(ma.Name.Identifier.Text, "DeserializeAsync", StringComparison.Ordinal))
                continue;

            // First argument must be Request.Body
            var args = invocation.ArgumentList.Arguments;
            if (args.Count == 0) continue;

            var firstArg = args[0].Expression;
            if (IsRequestBodyAccess(firstArg))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="expression"/> is a member-access
    /// of the form <c>Request.Body</c>.
    /// </summary>
    private static bool IsRequestBodyAccess(ExpressionSyntax expression)
    {
        return expression is MemberAccessExpressionSyntax ma2 &&
               string.Equals(ma2.Name.Identifier.Text, "Body", StringComparison.Ordinal) &&
               ma2.Expression is IdentifierNameSyntax id &&
               string.Equals(id.Identifier.Text, "Request", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns <see langword="true"/> if any parameter of the method has a type declared
    /// in <see cref="ContractsNamespace"/> and does NOT carry a binding-source attribute
    /// (<c>[FromBody]</c>, <c>[FromQuery]</c>, <c>[FromForm]</c>, or <c>[FromRoute]</c>)
    /// confirmed via the Roslyn semantic model.
    /// </summary>
    private static bool HasUnboundContractsParameter(
        MethodDeclarationSyntax method,
        SemanticModel model)
    {
        foreach (var param in method.ParameterList.Parameters)
        {
            // Quick syntactic pre-filter: does the parameter have any attribute?
            // A Contracts-typed param with no attributes at all is always a violation.
            bool hasBindingAttr = false;

            foreach (var attrList in param.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    string attrName = attr.Name switch
                    {
                        IdentifierNameSyntax id => id.Identifier.Text,
                        QualifiedNameSyntax qn => qn.Right.Identifier.Text,
                        _ => string.Empty
                    };

                    // Syntactic short-circuit: if the name matches one of the known binding attrs,
                    // no need to invoke the semantic model.
                    if (BindingSourceAttributes.Contains(attrName))
                    {
                        hasBindingAttr = true;
                        break;
                    }

                    // Semantic fallback: resolve the attribute symbol and check its namespace.
                    var attrSymbol = model.GetSymbolInfo(attr).Symbol
                        ?? model.GetSymbolInfo(attr).CandidateSymbols.FirstOrDefault();

                    if (attrSymbol != null)
                    {
                        var containingType = attrSymbol.ContainingType;
                        string ns = containingType?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                        if (string.Equals(ns, MvcNamespace, StringComparison.Ordinal))
                        {
                            hasBindingAttr = true;
                            break;
                        }
                    }
                }

                if (hasBindingAttr) break;
            }

            if (hasBindingAttr) continue;

            // Check whether the parameter's type is in the Contracts namespace.
            // GetSymbolInfo works for concrete types; GetTypeInfo fallback handles nullable
            // annotated reference types (T?) and cases where the symbol is not directly resolved.
            if (param.Type == null) continue;
            var typeSymbol = model.GetSymbolInfo(param.Type).Symbol as INamedTypeSymbol;
            if (typeSymbol == null)
                typeSymbol = model.GetTypeInfo(param.Type).Type as INamedTypeSymbol;
            if (typeSymbol == null) continue;

            // For nullable reference types (T?), GetTypeInfo returns the underlying T with
            // NullableAnnotation.Annotated; its ContainingNamespace is still the right namespace.
            string paramNs = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

            if (string.Equals(paramNs, ContractsNamespace, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the method body contains any member-access of the
    /// form <c>Request.Query</c>.
    /// </summary>
    private static bool HasDirectRequestQueryAccess(MethodDeclarationSyntax method)
    {
        var bodyNode = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        if (bodyNode == null) return false;

        foreach (var ma in bodyNode.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (!string.Equals(ma.Name.Identifier.Text, "Query", StringComparison.Ordinal))
                continue;

            if (ma.Expression is IdentifierNameSyntax id &&
                string.Equals(id.Identifier.Text, "Request", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// Scans every controller <c>.cs</c> file under <c>Stash.Registry/Controllers/</c>
    /// and asserts that the live violation set equals <see cref="KnownExemptions"/>.
    /// <para>
    /// At P1 the live set equals the pin (12 bypassing actions). As later phases migrate
    /// endpoints to declarative binding, each migration shrinks the live set — the test
    /// goes RED until the pin is also updated, making silent regressions impossible.
    /// </para>
    /// </summary>
    [Fact]
    public void AllControllerActions_UseDeclarativeBinding_OrAreExempted()
    {
        string controllersDir = FindControllersDir();

        var refs = BuildMetadataReferences();
        AssertBindingFloor(refs);

        (HashSet<string> liveViolations, int scannedFiles) = ScanControllers(controllersDir);

        // Floor guard: if discovery regresses and too few files are scanned, the test
        // would vacuously pass (0 live violations == 0 pin entries). Fail loudly.
        Assert.True(
            scannedFiles >= MinScannedFiles,
            $"Only {scannedFiles} controller file(s) scanned under '{controllersDir}' " +
            $"(expected >= {MinScannedFiles}). Path discovery likely regressed.");

        // Compute the unexpected entries in either direction.
        var extra = liveViolations.Except(KnownExemptions).OrderBy(s => s).ToList();
        var missing = KnownExemptions.Except(liveViolations).OrderBy(s => s).ToList();

        Assert.True(
            extra.Count == 0 && missing.Count == 0,
            "Controller model-binding violation set diverged from KnownExemptions.\n\n" +
            $"NEW violations not in KnownExemptions ({extra.Count}):\n" +
            (extra.Count > 0
                ? string.Join("\n", extra.Select(v => $"  + {v}"))
                : "  (none)") +
            "\n\n" +
            $"Missing from live set but still in KnownExemptions ({missing.Count}):\n" +
            (missing.Count > 0
                ? string.Join("\n", missing.Select(v => $"  - {v} (migrated? remove from KnownExemptions)"))
                : "  (none)") +
            "\n\nUpdate KnownExemptions in RequestModelBindingMetaTests to match the live set.");
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    /// <summary>
    /// Positive self-test for sink (a): a manual <c>DeserializeAsync</c> call with
    /// <c>Request.Body</c> is flagged, and a <c>DeserializeAsync</c> call that does NOT
    /// use <c>Request.Body</c> (e.g., uses a local variable) is not flagged.
    /// </summary>
    [Fact]
    public void Scanner_DeserializeAsyncRequestBody_IsFlagged()
    {
        const string source = @"
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Stash.Registry.Controllers;

[ApiController]
public class TestController : ControllerBase
{
    // Sink (a): flagged — first arg is Request.Body
    public async System.Threading.Tasks.Task<string?> BadAction()
    {
        var body = await JsonSerializer.DeserializeAsync<string>(Request.Body, new JsonSerializerOptions());
        return body;
    }

    // NOT flagged — first arg is a local stream, not Request.Body
    public async System.Threading.Tasks.Task<string?> GoodAction()
    {
        using var stream = new System.IO.MemoryStream();
        var body = await JsonSerializer.DeserializeAsync<string>(stream, new JsonSerializerOptions());
        return body;
    }
}";

        var refs = BuildMetadataReferences();
        var violations = new HashSet<string>(StringComparer.Ordinal);
        ScanFile(source, "sink-a-fixture", refs, violations);

        Assert.True(
            violations.Contains("Test.BadAction"),
            $"Expected 'Test.BadAction' in violations but got: {string.Join(", ", violations)}");

        Assert.False(
            violations.Contains("Test.GoodAction"),
            $"'Test.GoodAction' should NOT be flagged (it does not use Request.Body): {string.Join(", ", violations)}");
    }

    /// <summary>
    /// Positive self-test for sink (c): a public action that reads <c>Request.Query</c>
    /// directly is flagged; one that does not is clean.
    /// </summary>
    [Fact]
    public void Scanner_DirectRequestQueryAccess_IsFlagged()
    {
        const string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Stash.Registry.Controllers;

[ApiController]
public class TestController : ControllerBase
{
    // Sink (c): flagged — reads Request.Query directly
    public string BadQueryAction()
    {
        if (Request.Query.TryGetValue(""q"", out var q))
            return q.ToString();
        return string.Empty;
    }

    // NOT flagged — uses a [FromQuery] parameter instead
    public string GoodQueryAction([FromQuery] string q = """")
    {
        return q;
    }
}";

        var refs = BuildMetadataReferences();
        var violations = new HashSet<string>(StringComparer.Ordinal);
        ScanFile(source, "sink-c-fixture", refs, violations);

        Assert.True(
            violations.Contains("Test.BadQueryAction"),
            $"Expected 'Test.BadQueryAction' in violations but got: {string.Join(", ", violations)}");

        Assert.False(
            violations.Contains("Test.GoodQueryAction"),
            $"'Test.GoodQueryAction' should NOT be flagged: {string.Join(", ", violations)}");
    }

    /// <summary>
    /// Positive self-test for sink (b): a public controller action that accepts a
    /// <c>Stash.Registry.Contracts</c>-typed parameter <em>without</em> any binding-source
    /// attribute is flagged. The parameter is deliberately nullable (<c>T?</c>) to exercise
    /// the nullable-reference-type resolution path in <see cref="HasUnboundContractsParameter"/>,
    /// which is the realistic regression shape (e.g. a half-migrated endpoint like
    /// <c>AddMember(string org, AddOrgMemberRequest? body)</c> with no attribute).
    /// </summary>
    [Fact]
    public void Scanner_ContractsTypedParamWithoutBindingAttr_IsFlagged()
    {
        const string source = @"
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Contracts;

namespace Stash.Registry.Controllers;

[ApiController]
public class TestController : ControllerBase
{
    // Sink (b): flagged — Contracts-typed param with NO binding-source attribute
    public string BadUnboundAction(AddOrgMemberRequest? body)
    {
        return body?.Username ?? string.Empty;
    }

    // NOT flagged — same type but carries [FromBody]
    public string GoodBoundAction([FromBody] AddOrgMemberRequest? body)
    {
        return body?.Username ?? string.Empty;
    }
}";

        var refs = BuildMetadataReferences();
        var violations = new HashSet<string>(StringComparer.Ordinal);
        ScanFile(source, "sink-b-fixture", refs, violations);

        Assert.True(
            violations.Contains("Test.BadUnboundAction"),
            $"Expected 'Test.BadUnboundAction' in violations (Contracts-typed param without [FromBody]) " +
            $"but got: {string.Join(", ", violations)}");

        Assert.False(
            violations.Contains("Test.GoodBoundAction"),
            $"'Test.GoodBoundAction' should NOT be flagged (it carries [FromBody]): " +
            $"{string.Join(", ", violations)}");
    }

    /// <summary>
    /// Negative self-test: a public controller action that uses <c>[FromBody]</c> and
    /// does not read <c>Request.Body</c> or <c>Request.Query</c> is not flagged.
    /// </summary>
    [Fact]
    public void Scanner_CleanFromBodyAction_IsNotFlagged()
    {
        const string source = @"
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Contracts;

namespace Stash.Registry.Controllers;

[ApiController]
public class TestController : ControllerBase
{
    // Clean: [FromBody] present, no manual deserialization
    public string CleanAction([FromBody] AssignRoleRequest request)
    {
        return request.PrincipalId ?? string.Empty;
    }
}";

        var refs = BuildMetadataReferences();
        var violations = new HashSet<string>(StringComparer.Ordinal);
        ScanFile(source, "clean-fixture", refs, violations);

        Assert.True(
            violations.Count == 0,
            $"Expected zero violations for the clean [FromBody] fixture, but found: " +
            $"{string.Join(", ", violations)}");
    }
}
