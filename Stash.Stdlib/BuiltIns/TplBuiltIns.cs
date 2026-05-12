namespace Stash.Stdlib.BuiltIns;

using System;
using System.IO;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

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
[StashNamespace]
public static partial class TplBuiltIns
{
    /// <summary>Renders a template string or pre-compiled template with the given data dictionary.</summary>
    /// <param name="template">The template string or pre-compiled template</param>
    /// <param name="data">The data dictionary to use for rendering</param>
    /// <exception cref="StashErrorTypes.ParseError">if the template string contains invalid syntax</exception>
    /// <exception cref="StashErrorTypes.TypeError">if the first argument is not a string or compiled template</exception>
    /// <returns>The rendered output string</returns>
    [StashFn(ReturnType = "string")]
    public static StashValue Render(IInterpreterContext ctx, StashValue template, StashDictionary data)
    {
        // If first arg is a string, render it as a template
        if (template.ToObject() is string str)
        {
            return ctx.CompileAndRenderTemplate(str, data) is { } rendered
                ? StashValue.FromObj(rendered)
                : StashValue.Null;
        }

        // If first arg is a compiled template (List<TemplateNode>), render pre-parsed AST
        return ctx.RenderCompiledTemplate(template.ToObject(), data) is { } compiled
            ? StashValue.FromObj(compiled)
            : StashValue.Null;
    }

    /// <summary>Reads a template from a file and renders it with the given data dictionary.</summary>
    /// <param name="path">The path to the template file</param>
    /// <param name="data">The data dictionary to use for rendering</param>
    /// <exception cref="StashErrorTypes.IOError">if the template file does not exist or cannot be read</exception>
    /// <exception cref="StashErrorTypes.ParseError">if the template contains invalid syntax</exception>
    /// <returns>The rendered output string</returns>
    [StashFn(ReturnType = "string")]
    public static StashValue RenderFile(IInterpreterContext ctx, string path, StashDictionary data)
    {
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
    }

    /// <summary>Compiles a template string and returns a pre-compiled template for repeated rendering.</summary>
    /// <param name="template">The template string to compile</param>
    /// <exception cref="StashErrorTypes.ParseError">if the template string contains invalid syntax</exception>
    /// <returns>A pre-compiled template object</returns>
    [StashFn(ReturnType = "function")]
    public static StashValue Compile(IInterpreterContext ctx, string template)
    {
        return StashValue.FromObject(ctx.CompileTemplate(template));
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
