using Stash.Lexing;

namespace Stash.Analysis.Formatting.Rules;

internal static class OperatorRules
{
    internal static bool SpaceAroundBinaryOp(TokenType op) => true;
    internal static bool SpaceAroundAssignment() => true;
    internal static bool SpaceAfterUnaryOp() => false;
    internal static bool SpaceAroundArrow() => true;
}
