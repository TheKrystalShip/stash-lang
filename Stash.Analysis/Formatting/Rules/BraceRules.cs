using Stash.Parsing.AST;

namespace Stash.Analysis.Formatting.Rules;

internal static class BraceRules
{
    internal static bool AllowSingleLine(BlockStmt block, ScopeKind parentScope, FormatConfig config)
    {
        if (!config.SingleLineBlocks) return false;
        if (block.Statements.Count != 1) return false;
        return true;
    }

    internal static void BeforeOpenBrace(FormatContext ctx)
    {
        ctx.Space();
    }
}
