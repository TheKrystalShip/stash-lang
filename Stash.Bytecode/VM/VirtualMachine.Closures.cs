using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Closure creation and upvalue lifetime management.
/// </summary>
public sealed partial class VirtualMachine
{
    private void ExecuteClosure(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        ushort chunkIdx = Instruction.GetBx(inst);
        Chunk fnChunk = (Chunk)frame.Chunk.Constants[chunkIdx].AsObj!;

        var upvalues = new Upvalue[fnChunk.Upvalues.Length];
        for (int i = 0; i < fnChunk.Upvalues.Length; i++)
        {
            uint desc = frame.Chunk.Code[frame.IP++];
            byte isLocal = (byte)(desc & 0xFF);
            byte index = (byte)((desc >> 8) & 0xFF);
            upvalues[i] = isLocal == 1
                ? CaptureUpvalue(frame.BaseSlot + index)
                : frame.Upvalues![index];
        }

        _stack[frame.BaseSlot + a] = StashValue.FromObj(
            new VMFunction(fnChunk, upvalues) { ModuleGlobals = frame.ModuleGlobals ?? _globals });
    }

    private Upvalue CaptureUpvalue(int stackIndex)
    {
        for (int i = 0; i < _openUpvalues.Count; i++)
        {
            Upvalue existing = _openUpvalues[i];
            if (existing.StackIndex == stackIndex)
            {
                return existing;
            }
        }

        var upvalue = new Upvalue(_stack, stackIndex);
        int insertIdx = 0;
        while (insertIdx < _openUpvalues.Count && _openUpvalues[insertIdx].StackIndex > stackIndex)
        {
            insertIdx++;
        }

        _openUpvalues.Insert(insertIdx, upvalue);
        return upvalue;
    }

    private void CloseUpvalues(int fromSlot)
    {
        if (_openUpvalues.Count == 0) return;

        for (int i = _openUpvalues.Count - 1; i >= 0; i--)
        {
            if (_openUpvalues[i].StackIndex >= fromSlot)
            {
                _openUpvalues[i].Close();
                _openUpvalues.RemoveAt(i);
            }
        }
    }
}