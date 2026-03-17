namespace Stash.Interpreting.Templating;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Stash.Interpreting.Types;
using Environment = Stash.Interpreting.Environment;

/// <summary>
/// Renders a parsed template AST into a string by evaluating expressions
/// via the Stash interpreter and applying filters.
/// </summary>
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
    /// Renders a template string with the given data dictionary.
    /// </summary>
    public string Render(string template, StashDictionary data)
    {
        var lexer = new TemplateLexer(template);
        var tokens = lexer.Scan();
        var parser = new TemplateParser(tokens);
        var nodes = parser.Parse();
        return RenderNodes(nodes, data);
    }

    /// <summary>
    /// Renders a pre-parsed template with the given data dictionary.
    /// </summary>
    public string Render(List<TemplateNode> nodes, StashDictionary data)
    {
        return RenderNodes(nodes, data);
    }

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

    private void RenderOutput(OutputNode output, Environment env, StringBuilder sb)
    {
        var (value, error) = _interpreter.EvaluateString(output.Expression, env);
        if (error is not null)
        {
            if (output.Filters.Any(f => f.Name == "default"))
                value = null;
            else
                throw new TemplateException($"Error evaluating expression '{output.Expression}': {error}");
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
