namespace Stash.Interpreting.Templating;

/// <summary>
/// Base class for all template AST nodes.
/// </summary>
public abstract record TemplateNode;

/// <summary>
/// Literal text segment — output as-is.
/// </summary>
public record TextNode(string Text) : TemplateNode;

/// <summary>
/// Output expression: {{ expr | filter1 | filter2 }}
/// The Expression string is a Stash expression evaluated by the interpreter.
/// Filters are applied in order to the result.
/// </summary>
public record OutputNode(string Expression, TemplateFilter[] Filters, bool TrimBefore, bool TrimAfter) : TemplateNode;

/// <summary>
/// Conditional block: {% if %}...{% elif %}...{% else %}...{% endif %}
/// </summary>
public record IfNode(TemplateBranch[] Branches, TemplateNode[]? ElseBody, bool TrimBefore, bool TrimAfter) : TemplateNode;

/// <summary>
/// Loop block: {% for var in iterable %}...{% endfor %}
/// </summary>
public record ForNode(string Variable, string Iterable, TemplateNode[] Body, bool TrimBefore, bool TrimAfter) : TemplateNode;

/// <summary>
/// Include directive: {% include "path" %}
/// </summary>
public record IncludeNode(string Path, bool TrimBefore, bool TrimAfter) : TemplateNode;

/// <summary>
/// Raw block: {% raw %}...{% endraw %} — output literal text without parsing.
/// </summary>
public record RawNode(string Text) : TemplateNode;

/// <summary>
/// A single branch of an if/elif chain.
/// </summary>
public record TemplateBranch(string Condition, TemplateNode[] Body);

/// <summary>
/// A filter applied to an output expression: name(arg1, arg2, ...)
/// </summary>
public record TemplateFilter(string Name, string[] Arguments);
