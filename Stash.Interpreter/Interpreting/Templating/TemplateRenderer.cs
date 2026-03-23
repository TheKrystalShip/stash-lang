namespace Stash.Interpreting.Templating;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Stash.Interpreting.Types;
using Environment = Stash.Interpreting.Environment;

/// <summary>
/// Tree-walk renderer that evaluates a <see cref="TemplateNode"/> AST and produces
/// the final output string.
/// </summary>
/// <remarks>
/// <para>
/// The renderer is the final stage of the templating pipeline:<br/>
/// Template&nbsp;String → <see cref="TemplateLexer"/> → <see cref="TemplateParser"/> →
/// <c>List&lt;TemplateNode&gt;</c> → <see cref="TemplateRenderer"/> → <c>string</c>.
/// </para>
/// <para>
/// <strong>Environment setup:</strong> before rendering begins, <c>CreateEnvironment</c>
/// builds a <see cref="Stash.Interpreting.Environment"/> whose parent is the interpreter's
/// global scope, so every template variable in the data dictionary shadows globals but
/// built-in functions and namespaces remain accessible.
/// </para>
/// <para>
/// <strong>Expression evaluation:</strong> output expressions and conditions are forwarded
/// to <see cref="Interpreter.EvaluateString"/>, which parses and executes them as full
/// Stash expressions.  This means the complete Stash language (arithmetic, string
/// operations, method calls, etc.) is available inside templates.
/// </para>
/// <para>
/// <strong>Loop metadata:</strong> inside every <c>{% for %}</c> body a child
/// <see cref="Stash.Interpreting.Environment"/> is created and a <c>loop</c>
/// <see cref="Stash.Interpreting.Types.StashInstance"/> is injected with the fields
/// <c>loop.index</c> (1-based), <c>loop.index0</c> (0-based), <c>loop.first</c>,
/// <c>loop.last</c>, and <c>loop.length</c>.
/// </para>
/// <para>
/// <strong>Includes:</strong> <c>{% include "path" %}</c> is resolved relative to the
/// <c>basePath</c> supplied at construction time.  Path traversal outside the base
/// directory is blocked.  Includes are disabled (and throw) when no base path is set.
/// </para>
/// <para>
/// <strong>Filters:</strong> after an expression is evaluated, each
/// <see cref="TemplateFilter"/> in the pipeline is applied in order via
/// <see cref="TemplateFilters.Apply"/>.
/// </para>
/// </remarks>
public class TemplateRenderer
{
    private readonly Interpreter _interpreter;
    private readonly string? _basePath;

    /// <summary>
    /// Creates a renderer.
    /// </summary>
    /// <param name="interpreter">The Stash interpreter to evaluate expressions with.</param>
    /// <param name="basePath">Base directory for resolving include paths. Null disables includes.</param>
    public TemplateRenderer(Interpreter interpreter, string? basePath = null)
    {
        _interpreter = interpreter;
        _basePath = basePath;
    }

    /// <summary>
    /// Renders a template string with the given data dictionary by running the full
    /// lex → parse → render pipeline.
    /// </summary>
    /// <param name="template">The raw template source string.</param>
    /// <param name="data">Key-value pairs exposed as variables inside the template.</param>
    /// <returns>The rendered output string.</returns>
    /// <exception cref="TemplateException">
    /// Propagated from the lexer, parser, or renderer on any template error.
    /// </exception>
    public string Render(string template, StashDictionary data)
    {
        var lexer = new TemplateLexer(template);
        var tokens = lexer.Scan();
        var parser = new TemplateParser(tokens);
        var nodes = parser.Parse();
        return RenderNodes(nodes, data);
    }

    /// <summary>
    /// Renders a pre-parsed template AST with the given data dictionary,
    /// skipping the lex and parse phases.
    /// </summary>
    /// <param name="nodes">An AST produced by a prior call to <see cref="TemplateParser.Parse"/>.</param>
    /// <param name="data">Key-value pairs exposed as variables inside the template.</param>
    /// <returns>The rendered output string.</returns>
    public string Render(List<TemplateNode> nodes, StashDictionary data)
    {
        return RenderNodes(nodes, data);
    }

    /// <summary>
    /// Iterates over <paramref name="nodes"/>, creating a shared
    /// <see cref="Stash.Interpreting.Environment"/> populated from <paramref name="data"/>,
    /// and appends each rendered node to a <see cref="StringBuilder"/>.
    /// </summary>
    private string RenderNodes(List<TemplateNode> nodes, StashDictionary data)
    {
        var sb = new StringBuilder();
        var env = CreateEnvironment(data);

        foreach (var node in nodes)
        {
            RenderNode(node, env, data, sb);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Dispatches a single <see cref="TemplateNode"/> to its specific render method
    /// and appends the result to <paramref name="sb"/>.
    /// </summary>
    /// <param name="node">The AST node to render.</param>
    /// <param name="env">The current variable environment.</param>
    /// <param name="data">The original data dictionary (passed through to child renderers).</param>
    /// <param name="sb">The output buffer to append to.</param>
    private void RenderNode(TemplateNode node, Environment env, StashDictionary data, StringBuilder sb)
    {
        switch (node)
        {
            case TextNode text:
                sb.Append(text.Text);
                break;

            case OutputNode output:
                RenderOutput(output, env, sb);
                break;

            case IfNode ifNode:
                RenderIf(ifNode, env, data, sb);
                break;

            case ForNode forNode:
                RenderFor(forNode, env, data, sb);
                break;

            case IncludeNode include:
                RenderInclude(include, data, sb);
                break;

            case RawNode raw:
                sb.Append(raw.Text);
                break;
        }
    }

    /// <summary>
    /// Evaluates the expression in <paramref name="output"/>, applies its filter pipeline,
    /// and appends the stringified result to <paramref name="sb"/>.
    /// </summary>
    /// <remarks>
    /// If the expression evaluation produces an error and a <c>default</c> filter is
    /// present in the pipeline, the error is suppressed and <see langword="null"/> is
    /// passed to the <c>default</c> filter.  Otherwise the error is re-thrown as a
    /// <see cref="TemplateException"/>.
    /// </remarks>
    private void RenderOutput(OutputNode output, Environment env, StringBuilder sb)
    {
        var (value, error) = _interpreter.EvaluateString(output.Expression, env);
        if (error is not null)
        {
            if (output.Filters.Any(f => f.Name == "default"))
            {
                value = null;
            }
            else
            {
                throw new TemplateException($"Error evaluating expression '{output.Expression}': {error}");
            }
        }

        // Apply filters in order
        foreach (var filter in output.Filters)
        {
            value = TemplateFilters.Apply(filter.Name, value, filter.Arguments, _interpreter);
        }

        // Stringify the final value (but don't render "null")
        if (value is not null)
        {
            sb.Append(RuntimeValues.Stringify(value));
        }
    }

    /// <summary>
    /// Evaluates each branch condition of <paramref name="ifNode"/> in order and renders
    /// the first truthy branch.  Falls back to the else body if all conditions are falsy.
    /// </summary>
    private void RenderIf(IfNode ifNode, Environment env, StashDictionary data, StringBuilder sb)
    {
        foreach (var branch in ifNode.Branches)
        {
            var (value, error) = _interpreter.EvaluateString(branch.Condition, env);
            if (error is not null)
            {
                throw new TemplateException($"Error evaluating condition '{branch.Condition}': {error}");
            }

            if (RuntimeValues.IsTruthy(value))
            {
                foreach (var child in branch.Body)
                {
                    RenderNode(child, env, data, sb);
                }
                return;
            }
        }

        // No branch matched — render else body if present
        if (ifNode.ElseBody is not null)
        {
            foreach (var child in ifNode.ElseBody)
            {
                RenderNode(child, env, data, sb);
            }
        }
    }

    /// <summary>
    /// Iterates the collection described by <paramref name="forNode"/>, rendering the loop
    /// body once per element with the loop variable and <c>loop</c> metadata bound in a
    /// child environment.
    /// </summary>
    private void RenderFor(ForNode forNode, Environment env, StashDictionary data, StringBuilder sb)
    {
        var (iterableValue, error) = _interpreter.EvaluateString(forNode.Iterable, env);
        if (error is not null)
        {
            throw new TemplateException($"Error evaluating iterable '{forNode.Iterable}': {error}");
        }

        var items = CollectItems(iterableValue, forNode.Iterable);
        int totalCount = items.Count;

        for (int i = 0; i < totalCount; i++)
        {
            var childEnv = new Environment(env);
            childEnv.Define(forNode.Variable, items[i]);

            // Define loop metadata as a StashInstance
            var loopFields = new Dictionary<string, object?>
            {
                { "index", (long)(i + 1) },
                { "index0", (long)i },
                { "first", i == 0 },
                { "last", i == totalCount - 1 },
                { "length", (long)totalCount }
            };
            childEnv.Define("loop", new StashInstance("LoopInfo", loopFields));

            foreach (var child in forNode.Body)
            {
                RenderNode(child, childEnv, data, sb);
            }
        }
    }

    /// <summary>
    /// Converts a Stash runtime value into a flat <see cref="List{T}"/> of items
    /// suitable for iteration by <c>RenderFor</c>.
    /// </summary>
    /// <param name="iterableValue">The evaluated iterable: list, range, string, or dictionary.</param>
    /// <param name="iterableExpr">The original expression string, used in error messages.</param>
    /// <returns>A list of items to iterate over.</returns>
    /// <exception cref="TemplateException">
    /// Thrown when <paramref name="iterableValue"/> is <see langword="null"/> or an
    /// unsupported type.
    /// </exception>
    private List<object?> CollectItems(object? iterableValue, string iterableExpr)
    {
        switch (iterableValue)
        {
            case List<object?> list:
                return list;

            case StashRange range:
                var items = new List<object?>();
                foreach (var val in range.Iterate())
                {
                    items.Add(val);
                }
                return items;

            case string s:
                var chars = new List<object?>();
                foreach (var c in s)
                {
                    chars.Add(c.ToString());
                }
                return chars;

            case StashDictionary dict:
                var keys = new List<object?>();
                foreach (var entry in dict.RawEntries())
                {
                    keys.Add(entry.Key);
                }
                return keys;

            case null:
                throw new TemplateException($"Cannot iterate over null (expression: '{iterableExpr}').");

            default:
                throw new TemplateException($"Cannot iterate over value of type '{iterableValue.GetType().Name}' (expression: '{iterableExpr}').");
        }
    }

    /// <summary>
    /// Resolves, reads, and renders an included template file, appending its output to
    /// <paramref name="sb"/>.
    /// </summary>
    /// <remarks>
    /// The resolved absolute path must remain within <c>_basePath</c>; otherwise a
    /// <see cref="TemplateException"/> is thrown to prevent directory traversal.
    /// The included file is rendered with the same <paramref name="data"/> dictionary as
    /// the parent template.
    /// </remarks>
    private void RenderInclude(IncludeNode include, StashDictionary data, StringBuilder sb)
    {
        if (_basePath is null)
        {
            throw new TemplateException($"Cannot resolve include '{include.Path}' — no base path configured.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_basePath, include.Path));

        // Security: ensure the resolved path is within the base path
        var normalizedBase = Path.GetFullPath(_basePath);
        if (!fullPath.StartsWith(normalizedBase, StringComparison.Ordinal))
        {
            throw new TemplateException($"Include path '{include.Path}' resolves outside the base directory.");
        }

        if (!File.Exists(fullPath))
        {
            throw new TemplateException($"Included template not found: '{include.Path}'.");
        }

        var templateContent = File.ReadAllText(fullPath);
        var result = Render(templateContent, data);
        sb.Append(result);
    }

    /// <summary>
    /// Creates an environment populated with the data dictionary's entries,
    /// with the interpreter's globals as the enclosing scope so built-in
    /// functions and namespaces are accessible.
    /// </summary>
    private Environment CreateEnvironment(StashDictionary data)
    {
        var env = new Environment(_interpreter.Globals);

        foreach (var entry in data.RawEntries())
        {
            if (entry.Key is string key)
            {
                env.Define(key, entry.Value);
            }
        }

        return env;
    }
}
