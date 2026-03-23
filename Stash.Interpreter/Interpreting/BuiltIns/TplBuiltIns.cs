namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.IO;
using Stash.Interpreting.Templating;
using Stash.Interpreting.Types;

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
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var tpl = new StashNamespace("tpl");

        // tpl.render(template, data) — render a template string with data dictionary
        // tpl.render(compiled, data) — render a pre-compiled template with data dictionary
        tpl.Define("render", new BuiltInFunction("tpl.render", 2, (interp, args) =>
        {
            if (args[1] is not StashDictionary data)
            {
                throw new RuntimeError("'tpl.render' expects a dictionary as the second argument.");
            }

            // If first arg is a string, render it as a template
            if (args[0] is string template)
            {
                var renderer = new TemplateRenderer(interp);
                return renderer.Render(template, data);
            }

            // If first arg is a compiled template (List<TemplateNode>), render pre-parsed AST
            if (args[0] is List<TemplateNode> nodes)
            {
                var renderer = new TemplateRenderer(interp);
                return renderer.Render(nodes, data);
            }

            throw new RuntimeError("'tpl.render' expects a string or compiled template as the first argument.");
        }));

        // tpl.renderFile(path, data) — render a template file with data dictionary
        tpl.Define("renderFile", new BuiltInFunction("tpl.renderFile", 2, (interp, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("'tpl.renderFile' expects a file path string as the first argument.");
            }
            if (args[1] is not StashDictionary data)
            {
                throw new RuntimeError("'tpl.renderFile' expects a dictionary as the second argument.");
            }

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
            var renderer = new TemplateRenderer(interp, basePath);
            return renderer.Render(template, data);
        }));

        // tpl.compile(template) — pre-compile a template string for repeated rendering
        tpl.Define("compile", new BuiltInFunction("tpl.compile", 1, (_, args) =>
        {
            if (args[0] is not string template)
            {
                throw new RuntimeError("'tpl.compile' expects a template string.");
            }

            var lexer = new TemplateLexer(template);
            var tokens = lexer.Scan();
            var parser = new TemplateParser(tokens);
            var nodes = parser.Parse();

            // Return as a List<TemplateNode> — the renderer can accept this directly
            return nodes;
        }));

        globals.Define("tpl", tpl);
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
