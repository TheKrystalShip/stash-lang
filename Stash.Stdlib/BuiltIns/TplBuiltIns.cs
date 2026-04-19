namespace Stash.Stdlib.BuiltIns;

using System;
using System.IO;
using Stash.Runtime;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>tpl</c> built-in namespace, which exposes Stash's templating engine
/// to user code via <c>tpl.render</c>, <c>tpl.renderFile</c>, and <c>tpl.compile</c>.
/// </summary>
/// <remarks>
/// <para>
/// The three functions mirror the three stages of the templating pipeline:
/// <list type="bullet">
///   <item><description>
///     <c>tpl.compile(template)</c> runs the <see cref="TemplateLexer"/> and
///     <see cref="TemplateParser"/> and returns the resulting AST as a
///     <c>List&lt;TemplateNode&gt;</c> for repeated use.
///   </description></item>
///   <item><description>
///     <c>tpl.render(template, data)</c> accepts either a raw template string (lex +
///     parse + render) or a pre-compiled AST (render only) together with a data dictionary.
///   </description></item>
///   <item><description>
///     <c>tpl.renderFile(path, data)</c> reads a template from disk and renders it, with
///     the file's directory automatically set as the base path for <c>{% include %}</c>
///     directives.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
public static class TplBuiltIns
{
    /// <summary>
    /// Registers the <c>tpl</c> namespace and its three functions into <paramref name="globals"/>.
    /// </summary>
    /// <param name="globals">The interpreter's global <see cref="Stash.Interpreting.Environment"/>.</param>
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("tpl");

        // tpl.render(template, data) — render a template string with data dictionary
        // tpl.render(compiled, data) — render a pre-compiled template with data dictionary
        ns.Function("render", [Param("template"), Param("data", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var data = SvArgs.Dict(args, 1, "tpl.render");

            // If first arg is a string, render it as a template
            if (args[0].ToObject() is string template)
            {
                return ctx.CompileAndRenderTemplate(template, data) is { } rendered
                    ? StashValue.FromObj(rendered)
                    : StashValue.Null;
            }

            // If first arg is a compiled template (List<TemplateNode>), render pre-parsed AST
            return ctx.RenderCompiledTemplate(args[0].ToObject(), data) is { } compiled
                ? StashValue.FromObj(compiled)
                : StashValue.Null;
        },
            returnType: "string",
            documentation: "Renders a template string or pre-compiled template with the given data dictionary.\n@param template The template string or pre-compiled template\n@param data The data dictionary to use for rendering\n@return The rendered output string");

        // tpl.renderFile(path, data) — render a template file with data dictionary
        ns.Function("renderFile", [Param("path", "string"), Param("data", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var path = SvArgs.String(args, 0, "tpl.renderFile");
            var data = SvArgs.Dict(args, 1, "tpl.renderFile");
            string expandedPath = ExpandTilde(path);

            if (!File.Exists(expandedPath))
            {
                throw new RuntimeError($"Template file not found: '{path}'.");
            }

            string template;
            try
            {
                template = File.ReadAllText(expandedPath);
            }
            catch (IOException ex)
            {
                throw new RuntimeError($"Failed to read template file '{path}': {ex.Message}");
            }
            string? basePath = Path.GetDirectoryName(Path.GetFullPath(expandedPath));
            return StashValue.FromObject(ctx.CompileAndRenderTemplate(template, data, basePath));
        },
            returnType: "string",
            documentation: "Reads a template from a file and renders it with the given data dictionary.\n@param path The path to the template file\n@param data The data dictionary to use for rendering\n@return The rendered output string");

        // tpl.compile(template) — pre-compile a template string for repeated rendering
        ns.Function("compile", [Param("template", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var template = SvArgs.String(args, 0, "tpl.compile");
            return StashValue.FromObject(ctx.CompileTemplate(template));
        },
            returnType: "function",
            documentation: "Compiles a template string and returns a pre-compiled template for repeated rendering.\n@param template The template string to compile\n@return A pre-compiled template object");

        return ns.Build();
    }

    /// <summary>
    /// Expands a leading <c>~</c> in <paramref name="path"/> to the current user's
    /// home directory, mirroring shell tilde expansion.
    /// </summary>
    /// <param name="path">The file path, potentially starting with <c>~</c>.</param>
    /// <returns>The path with <c>~</c> replaced by the user profile directory.</returns>
    private static string ExpandTilde(string path)
    {
        if (path.StartsWith('~'))
        {
            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[1..].TrimStart(Path.DirectorySeparatorChar));
        }
        return path;
    }
}
