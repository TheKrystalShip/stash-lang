namespace Stash.Runtime;

using Stash.Runtime.Types;

/// <summary>
/// Template rendering: compile and render TPL templates.
/// Used by TplBuiltIns. Default implementations return null — overridden by Interpreter.
/// </summary>
public interface ITemplateContext
{
    object? CompileAndRenderTemplate(string template, StashDictionary data, string? basePath = null) { return null; }
    object? CompileTemplate(string template) { return null; }
    object? RenderCompiledTemplate(object? compiled, StashDictionary data) { return null; }
}
