namespace Stash.Runtime;

/// <summary>
/// Abstraction for evaluating Stash expressions within template rendering.
/// Implemented by the Interpreter to decouple the template engine from the interpreter.
/// </summary>
public interface ITemplateEvaluator
{
    /// <summary>
    /// Evaluates a Stash expression string in the given environment scope.
    /// </summary>
    (object? Value, string? Error) EvaluateExpression(string expression, object environment);

    /// <summary>
    /// Returns the global environment (top of the scope chain).
    /// </summary>
    object GlobalEnvironment { get; }

    /// <summary>
    /// Creates a new child environment enclosed by the given parent.
    /// </summary>
    object CreateChildEnvironment(object parent);

    /// <summary>
    /// Defines a variable in the given environment.
    /// </summary>
    void DefineVariable(object environment, string name, object? value);
}
