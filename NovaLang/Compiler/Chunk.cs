using NovaLang.Runtime;

namespace NovaLang.Compiler;

public class Chunk
{
    public List<byte> Code { get; } = new(256);
    public List<NovaValue> Constants { get; } = new(32);
    public List<int> Lines { get; } = new(256);

    private int[] _constantRefCounts = [];

    public int Write(byte val, int line)
    {
        Code.Add(val);
        Lines.Add(line);
        return Code.Count - 1;
    }

    public int WriteU16(ushort val, int line)
    {
        Code.Add((byte)val);
        Lines.Add(line);
        Code.Add((byte)(val >> 8));
        Lines.Add(line);
        return Code.Count - 2;
    }

    public int WriteOp(OpCode op, int line)
    {
        return Write((byte)op, line);
    }

    public int AddConstant(NovaValue value)
    {
        for (int i = 0; i < Constants.Count; i++)
        {
            if (Constants[i].Type != value.Type)
                continue;
            if (value.Type == Runtime.ValueType.Float)
            {
                if (Constants[i].AsFloat == value.AsFloat)
                    return i;
            }
            else if (Constants[i].AsInt == value.AsInt)
                return i;
        }
        Constants.Add(value);
        return Constants.Count - 1;
    }

    public void PatchJump(int offset, int jumpTarget)
    {
        Code[offset] = (byte)jumpTarget;

        int pos = offset + 1 + jumpTarget;
        int follow = 10;
        while (follow-- > 0 && pos < Code.Count - 1)
        {
            if ((OpCode)Code[pos] != OpCode.Jump)
                break;
            sbyte inner = (sbyte)Code[pos + 1];
            int next = pos + 2 + inner;
            if (next == pos || next < 0 || next >= Code.Count)
                break;
            int newDist = next - offset - 1;
            if (newDist < 0 || newDist > 127)
                break;
            Code[offset] = (byte)newDist;
            pos = next;
        }
    }

    public void IncrementConstantRefCount(int index)
    {
        if (index >= _constantRefCounts.Length)
            Array.Resize(ref _constantRefCounts, Constants.Count);
        _constantRefCounts[index]++;
    }

    public void ReorderConstantsByFrequency()
    {
        if (Constants.Count <= 256)
            return;

        var indices = Enumerable.Range(0, Constants.Count).ToArray();
        Array.Sort(indices, (a, b) => _constantRefCounts[b].CompareTo(_constantRefCounts[a]));

        int[] perm = new int[Constants.Count];
        for (int newIdx = 0; newIdx < indices.Length; newIdx++)
            perm[indices[newIdx]] = newIdx;

        int cumDelta = 0;
        int[] cumDeltas = new int[Code.Count + 1];
        int ip = 0;
        while (ip < Code.Count)
        {
            cumDeltas[ip] = cumDelta;
            OpCode op = (OpCode)Code[ip];
            if (op == OpCode.PushConst && perm[Code[ip + 1]] > 255)
                cumDelta++;
            else if (op == OpCode.PushConst16)
            {
                ushort oldIdx = (ushort)(Code[ip + 1] | (Code[ip + 2] << 8));
                if (perm[oldIdx] <= 255)
                    cumDelta--;
            }
            ip += OpCodeSize(op);
        }
        cumDeltas[Code.Count] = cumDelta;

        List<byte> newCode = new(Code.Count + cumDelta);
        List<int> newLines = new(Lines.Count + cumDelta);

        ip = 0;
        while (ip < Code.Count)
        {
            int line = Lines[ip];
            OpCode op = (OpCode)Code[ip];

            switch (op)
            {
                case OpCode.PushConst:
                {
                    byte oldIdx = Code[ip + 1];
                    int newIdx = perm[oldIdx];
                    int line2 = Lines[ip + 1];
                    if (newIdx <= 255)
                    {
                        newCode.Add((byte)OpCode.PushConst);
                        newLines.Add(line);
                        newCode.Add((byte)newIdx);
                        newLines.Add(line2);
                    }
                    else
                    {
                        newCode.Add((byte)OpCode.PushConst16);
                        newLines.Add(line);
                        newCode.Add((byte)newIdx);
                        newLines.Add(line2);
                        newCode.Add((byte)(newIdx >> 8));
                        newLines.Add(line2);
                    }
                    ip += 2;
                    break;
                }
                case OpCode.PushConst16:
                {
                    ushort oldIdx = (ushort)(Code[ip + 1] | (Code[ip + 2] << 8));
                    int newIdx = perm[oldIdx];
                    int line2 = Lines[ip + 1];
                    int line3 = Lines[ip + 2];
                    if (newIdx <= 255)
                    {
                        newCode.Add((byte)OpCode.PushConst);
                        newLines.Add(line);
                        newCode.Add((byte)newIdx);
                        newLines.Add(line2);
                    }
                    else
                    {
                        newCode.Add((byte)OpCode.PushConst16);
                        newLines.Add(line);
                        newCode.Add((byte)newIdx);
                        newLines.Add(line2);
                        newCode.Add((byte)(newIdx >> 8));
                        newLines.Add(line3);
                    }
                    ip += 3;
                    break;
                }
                case OpCode.Jump:
                {
                    sbyte oldOff = (sbyte)Code[ip + 1];
                    int oldTarget = ip + 2 + oldOff;
                    int newTarget = oldTarget + cumDeltas[oldTarget];
                    int newOff = newTarget - (newCode.Count + 2);
                    newCode.Add((byte)OpCode.Jump);
                    newLines.Add(line);
                    newCode.Add((byte)newOff);
                    newLines.Add(Lines[ip + 1]);
                    ip += 2;
                    break;
                }
                case OpCode.JumpIfFalse:
                {
                    byte oldOff = Code[ip + 1];
                    int oldTarget = ip + 2 + oldOff;
                    int newTarget = oldTarget + cumDeltas[oldTarget];
                    int newOff = newTarget - (newCode.Count + 2);
                    newCode.Add((byte)OpCode.JumpIfFalse);
                    newLines.Add(line);
                    newCode.Add((byte)newOff);
                    newLines.Add(Lines[ip + 1]);
                    ip += 2;
                    break;
                }
                default:
                {
                    int size = OpCodeSize(op);
                    for (int j = 0; j < size; j++)
                    {
                        newCode.Add(Code[ip + j]);
                        newLines.Add(Lines[ip + j]);
                    }
                    ip += size;
                    break;
                }
            }
        }

        NovaValue[] reordered = new NovaValue[Constants.Count];
        for (int newIdx = 0; newIdx < indices.Length; newIdx++)
            reordered[newIdx] = Constants[indices[newIdx]];

        Constants.Clear();
        Constants.AddRange(reordered);
        Code.Clear();
        Code.AddRange(newCode);
        Lines.Clear();
        Lines.AddRange(newLines);
    }

    private static int OpCodeSize(OpCode op)
    {
        return op switch
        {
            OpCode.Return => 1,
            OpCode.PushConst => 2,
            OpCode.PushConst16 => 3,
            OpCode.Add => 1,
            OpCode.Sub => 1,
            OpCode.Mul => 1,
            OpCode.Div => 1,
            OpCode.JumpIfFalse => 2,
            OpCode.Jump => 2,
            OpCode.LoadLocal => 2,
            OpCode.StoreLocal => 2,
            OpCode.LoadLocal_0 => 1,
            OpCode.LoadLocal_1 => 1,
            OpCode.LoadLocal_2 => 1,
            OpCode.LoadLocal_3 => 1,
            OpCode.StoreLocal_0 => 1,
            OpCode.StoreLocal_1 => 1,
            OpCode.StoreLocal_2 => 1,
            OpCode.StoreLocal_3 => 1,
            OpCode.CallNative => 2,
            OpCode.Call => 3,
            OpCode.RetUserFunc => 1,
            OpCode.Equal => 1,
            OpCode.NotEqual => 1,
            OpCode.Less => 1,
            OpCode.Greater => 1,
            OpCode.LessEqual => 1,
            OpCode.GreaterEqual => 1,
            OpCode.StructNew => 2,
            OpCode.StructGet => 2,
            OpCode.StructSet => 2,
            OpCode.Pop => 1,
            OpCode.BufferNew => 1,
            OpCode.BufferGet => 1,
            OpCode.BufferSet => 1,
            OpCode.Dup => 1,
            OpCode.Dup2 => 1,
            OpCode.BufferSlice => 1,
            OpCode.BufferSliceAssign => 1,
            OpCode.PushConstInt_0 => 1,
            OpCode.PushConstInt_1 => 1,
            OpCode.PushConstInt8 => 2,
            OpCode.StoreLocalDup => 2,
            OpCode.StoreLocalDup_0 => 1,
            OpCode.StoreLocalDup_1 => 1,
            OpCode.StoreLocalDup_2 => 1,
            OpCode.StoreLocalDup_3 => 1,
            OpCode.BufferFromStack => 2,
            OpCode.Inc => 2,
            OpCode.Dec => 2,
            OpCode.PushNone => 1,
            OpCode.PushExceptionHandler => 3,
            OpCode.PopExceptionHandler => 1,
            OpCode.EndFinally => 1,
            OpCode.Throw => 1,
            OpCode.PushExceptionValue => 1,
            _ => 1,
        };
    }
}
