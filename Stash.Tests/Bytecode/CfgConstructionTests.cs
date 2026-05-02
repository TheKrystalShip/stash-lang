using System;
using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Bytecode.Optimization;
using Stash.Runtime;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests for CFG construction and the linear lowering round-trip.
/// </summary>
public class CfgConstructionTests : BytecodeTestBase
{
    // ===========================================================================
    // Basic structural tests
    // ===========================================================================

    [Fact]
    public void Linear_Code_OneBlock()
    {
        // A program with no branches produces exactly one basic block.
        Chunk chunk = CompileSource("let x = 1; let y = 2; x + y;");
        ControlFlowGraph cfg = CfgBuilder.Build(chunk.Code, chunk.Constants);

        Assert.NotEmpty(cfg.Blocks);
        Assert.Single(cfg.Blocks);
        Assert.Equal(0, cfg.Blocks[0].Id);
        Assert.Equal(0, cfg.Blocks[0].OriginalStart);
    }

    [Fact]
    public void IfElse_ProducesThreeBlocks()
    {
        // if/else produces: [condition+branch], [then-body+jmp], [else-body], ...merge
        // At minimum 3 distinct basic blocks.
        Chunk chunk = CompileSource("let x = 1; if (x > 0) { x = 2; } else { x = 3; }");
        ControlFlowGraph cfg = CfgBuilder.Build(chunk.Code, chunk.Constants);

        Assert.True(cfg.Blocks.Count >= 3,
            $"if/else should produce ≥3 blocks but got {cfg.Blocks.Count}");

        // Entry block has at least one successor (the conditional branch).
        Assert.NotEmpty(cfg.Entry.Successors);
    }

    [Fact]
    public void WhileLoop_ProducesBackEdge()
    {
        Chunk chunk = CompileSource("let i = 0; while (i < 10) { i = i + 1; }");
        ControlFlowGraph cfg = CfgBuilder.Build(chunk.Code, chunk.Constants);

        Assert.True(cfg.Blocks.Count >= 2,
            $"while loop should produce ≥2 blocks but got {cfg.Blocks.Count}");

        // There must be at least one Loop or Branch edge pointing backward (lower block ID).
        bool hasBackEdge = false;
        foreach (BasicBlock block in cfg.Blocks)
        {
            foreach (BlockEdge edge in block.Successors)
            {
                if (edge.TargetBlockId < block.Id)
                {
                    hasBackEdge = true;
                    break;
                }
            }
            if (hasBackEdge) break;
        }
        Assert.True(hasBackEdge, "While loop must produce a backward control-flow edge");
    }

    [Fact]
    public void TryCatch_SplitsAtTryBeginAndHandler()
    {
        Chunk chunk = CompileSource("try { let x = 1; } catch (e) { let y = 2; }");;
        ControlFlowGraph cfg = CfgBuilder.Build(chunk.Code, chunk.Constants);

        Assert.True(cfg.Blocks.Count >= 2,
            $"try/catch should produce ≥2 blocks but got {cfg.Blocks.Count}");

        // There must be at least one ExceptionHandler edge.
        bool hasHandlerEdge = false;
        foreach (BasicBlock block in cfg.Blocks)
        {
            foreach (BlockEdge edge in block.Successors)
            {
                if (edge.Kind == EdgeKind.ExceptionHandler)
                {
                    hasHandlerEdge = true;
                    break;
                }
            }
            if (hasHandlerEdge) break;
        }
        Assert.True(hasHandlerEdge, "try/catch must produce an ExceptionHandler edge");
    }

    [Fact]
    public void ForLoop_Numeric_ProducesMultipleBlocks()
    {
        Chunk chunk = CompileSource("for (let i in 0..9) { let x = i; }");
        ControlFlowGraph cfg = CfgBuilder.Build(chunk.Code, chunk.Constants);

        Assert.True(cfg.Blocks.Count >= 2,
            $"numeric for loop should produce ≥2 blocks but got {cfg.Blocks.Count}");
    }

    [Fact]
    public void ForLoop_Collection_ProducesMultipleBlocks()
    {
        Chunk chunk = CompileSource("let arr = [1, 2, 3]; for (let x in arr) { let y = x; }");
        ControlFlowGraph cfg = CfgBuilder.Build(chunk.Code, chunk.Constants);

        Assert.True(cfg.Blocks.Count >= 2,
            $"collection for loop should produce ≥2 blocks but got {cfg.Blocks.Count}");
    }

    // ===========================================================================
    // Companion word / round-trip test
    // ===========================================================================

    [Fact]
    public void CompanionWords_PreservedThroughRoundTrip()
    {
        // Craft a synthetic code array:
        //   [0] LoadK r0, k0        (ABx, no companion)
        //   [1] LoadK r1, k1        (ABx, no companion)
        //   [2] Jmp   r0, sBx=1    → jumps to position 4
        //   [3] LoadNull r2        (unreachable, companion-free)
        //   [4] GetFieldIC r3, r0, c0  (ABC, followed by companion at [5])
        //   [5] 0x0000ABCD          (raw companion word — IC slot index)
        //   [6] Return r0, 1, 0
        uint jmpInst = Instruction.EncodeAsBx(OpCode.Jmp, 0, 1);  // target = 2+1+1 = 4
        uint getFieldIC = Instruction.EncodeABC(OpCode.GetFieldIC, 3, 0, 0);
        uint companion = 0x0000ABCD;
        var code = new List<uint>
        {
            Instruction.EncodeABx(OpCode.LoadK, 0, 0),   // [0]
            Instruction.EncodeABx(OpCode.LoadK, 1, 1),   // [1]
            jmpInst,                                       // [2] Jmp → 4
            Instruction.EncodeABC(OpCode.LoadNull, 2, 0, 0), // [3] unreachable
            getFieldIC,                                    // [4] GetFieldIC + companion
            companion,                                     // [5] IC slot companion word
            Instruction.EncodeABC(OpCode.Return, 0, 1, 0), // [6]
        };

        var constants = new List<StashValue>(); // no constants needed for this test
        ControlFlowGraph cfg = CfgBuilder.Build(code, constants);

        // Verify companion was identified (block[1] starts at [4] and has 2 words:
        // GetFieldIC + companion, then Return is in the same or next block).
        bool companionFound = false;
        foreach (BasicBlock block in cfg.Blocks)
        {
            foreach (CfgWord word in block.Words)
            {
                if (word.IsCompanion && word.Raw == companion)
                {
                    companionFound = true;
                    break;
                }
            }
        }
        Assert.True(companionFound, "Companion word must be identified in the CFG");

        // Round-trip: lower back to linear code — must be byte-equal to input.
        var codeOut = new List<uint>();
        var srcOut = new List<SourceMapEntry>();
        CfgLowering.Lower(cfg, codeOut, srcOut, Array.Empty<SourceMapEntry>());

        Assert.Equal(code.Count, codeOut.Count);
        for (int i = 0; i < code.Count; i++)
        {
            // Jump instructions have their sBx re-patched; verify the patched Jmp still
            // points to the same logical target (position 4 maps to the same output position).
            OpCode opOriginal = Instruction.GetOp(code[i]);
            OpCode opNew      = Instruction.GetOp(codeOut[i]);
            Assert.Equal(opOriginal, opNew);

            if (!CfgOpcodeInfo.IsJumpWithSBx(opOriginal))
            {
                // Non-jump words must be byte-equal.
                Assert.Equal(code[i], codeOut[i]);
            }
        }

        // Verify the re-patched Jmp still has the same target.
        int origJmpTarget = 2 + 1 + Instruction.GetSBx(code[2]);   // = 4
        int newJmpTarget  = 2 + 1 + Instruction.GetSBx(codeOut[2]); // must still = 4
        Assert.Equal(origJmpTarget, newJmpTarget);
    }

    // ===========================================================================
    // CFG word count / structure invariants
    // ===========================================================================

    [Fact]
    public void Cfg_TotalWordCount_MatchesOriginalCode()
    {
        Chunk chunk = CompileSource("let x = 1 + 2; let y = x * 3; y;");
        ControlFlowGraph cfg = CfgBuilder.Build(chunk.Code, chunk.Constants);

        // Every word in the original code must appear in exactly one block.
        int totalWords = 0;
        foreach (BasicBlock block in cfg.Blocks)
            totalWords += block.Words.Count;

        Assert.Equal(chunk.Code.Length, totalWords);
        Assert.Equal(chunk.Code.Length, cfg.OriginalWordCount);
    }

    [Fact]
    public void Cfg_PredecessorEdges_AreSymmetric()
    {
        Chunk chunk = CompileSource("if (true) { let a = 1; } else { let b = 2; }");
        ControlFlowGraph cfg = CfgBuilder.Build(chunk.Code, chunk.Constants);

        // For every successor edge A→B there must be a corresponding predecessor edge B←A.
        foreach (BasicBlock block in cfg.Blocks)
        {
            foreach (BlockEdge succ in block.Successors)
            {
                BasicBlock target = cfg.GetBlock(succ.TargetBlockId);
                bool found = false;
                foreach (BlockEdge pred in target.Predecessors)
                {
                    if (pred.TargetBlockId == block.Id)
                    {
                        found = true;
                        break;
                    }
                }
                Assert.True(found,
                    $"Missing predecessor edge from block {succ.TargetBlockId} back to block {block.Id}");
            }
        }
    }
}
