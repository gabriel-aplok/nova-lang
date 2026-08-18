using System.Buffers.Text;
using System.Runtime.CompilerServices;

namespace NovaLang.Runtime;

public enum OpCode : byte
{
    Return,
    PushConst,
    Add,
    Sub,
    Mul,
    Div,
    JumpIfFalse,
    Jump,
    LoadLocal,
    StoreLocal,
    LoadLocal_0,
    LoadLocal_1,
    LoadLocal_2,
    LoadLocal_3,
    StoreLocal_0,
    StoreLocal_1,
    StoreLocal_2,
    StoreLocal_3,
    CallNative,
    Call,
    RetUserFunc,
    Equal,
    NotEqual,
    Less,
    Greater,
    LessEqual,
    GreaterEqual,
    StructNew,
    StructGet,
    StructSet,
    Pop,
    BufferNew,
    BufferGet,
    BufferSet,
    PushConst16,
    Dup,
    Dup2,
    BufferSlice,
    BufferSliceAssign,
    PushConstInt_0,
    PushConstInt_1,
    PushConstInt8,
    StoreLocalDup,
    StoreLocalDup_0,
    StoreLocalDup_1,
    StoreLocalDup_2,
    StoreLocalDup_3,
    BufferFromStack,
    Inc,
    Dec,
    PushNone,
    PushExceptionHandler,
    PopExceptionHandler,
    Throw,
    PushExceptionValue,
    EndFinally,
}

/// <summary>
/// Threading context optimized to live in CPU registers and L1 lines during execution.
/// </summary>
public unsafe struct VMContext
{
    public byte* IP;
    public byte* BaseAddress;
    public NovaValue* Stack;
    public int StackTop;
    public NovaValue* Constants;

    // Unmanaged pointers to call-stack frames
    public byte** ReturnAddressStack;
    public int* FrameBaseStack;
    public int CallStackTop;
    public int FrameBase;

    // Raw heap base pointer for struct allocation
    public byte* HeapBase;

    // String pool base pointer for zero-allocation string interning
    public byte* StringPoolBase;

    public bool IsRunning;
}

/// <summary>
/// Creates a VM. Stack capacities are configurable so deep recursion has room
/// to grow beyond the historical fixed 4096-value / 256-frame limits.
/// </summary>
public unsafe class VM(
    byte[] bytecode,
    NovaValue[] constants,
    int valueStackCapacity = 65536,
    int callStackCapacity = 4096
)
{
    private readonly NovaValue[] _stack = new NovaValue[valueStackCapacity];
    private readonly byte[] _instructions = bytecode;
    private readonly NovaValue[] _constants = constants;
    internal readonly Action<VM>[] _nativeFunctions = new Action<VM>[256];

    private readonly byte*[] _returnAddressStack = new byte*[callStackCapacity];
    private readonly int[] _frameBaseStack = new int[callStackCapacity];
    private readonly int _callStackCapacity = callStackCapacity;
    private readonly int _valueStackCapacity = valueStackCapacity;
    private int _callStackTop = 0;
    private int _frameBase = 0;

    // Exception handler stack — records the bytecode offset of each active
    // catch block along with the frame/stack state to unwind to on throw.
    private readonly int[] _handlerTargetStack = new int[callStackCapacity];
    private readonly int[] _handlerStackTop = new int[callStackCapacity];
    private readonly int[] _handlerFrameBase = new int[callStackCapacity];
    private readonly int[] _handlerCallTop = new int[callStackCapacity];
    private int _handlerTop = 0;
    private NovaValue _exceptionValue;
    internal HeapAllocator Heap = new();
    internal StringPool? Strings;

    public int StackTop { get; set; } = 0;

    public void RegisterNative(int index, Action<VM> func)
    {
        _nativeFunctions[index] = func;
    }

    public NovaValue Pop()
    {
        return _stack[--StackTop];
    }

    public void Push(NovaValue value)
    {
        _stack[StackTop++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Execute()
    {
        // lock array reference memory tracking fields manually
        fixed (byte* pInstructions = _instructions)
        fixed (NovaValue* pStack = _stack)
        fixed (NovaValue* pConstants = _constants)
        fixed (byte** pRetAddr = _returnAddressStack)
        fixed (int* pFrameBase = _frameBaseStack)
        {
            byte* pHeap = Heap.GetBuffer();
            byte* pStringPool = Strings != null ? Strings.GetBuffer() : null;
            // direct jump tablem aomputed GOTO via stack-allocated function pointers
            delegate* <VM, VMContext*, void>* dispatch =
                stackalloc delegate* managed<VM, VMContext*, void>[256];
            for (int i = 0; i < 256; i++)
                dispatch[i] = &HandleUnknown;
            dispatch[(byte)OpCode.Return] = &HandleReturn;
            dispatch[(byte)OpCode.PushConst] = &HandlePushConst;
            dispatch[(byte)OpCode.Add] = &HandleAdd;
            dispatch[(byte)OpCode.Sub] = &HandleSub;
            dispatch[(byte)OpCode.Mul] = &HandleMul;
            dispatch[(byte)OpCode.Div] = &HandleDiv;
            dispatch[(byte)OpCode.JumpIfFalse] = &HandleJumpIfFalse;
            dispatch[(byte)OpCode.Jump] = &HandleJump;
            dispatch[(byte)OpCode.LoadLocal] = &HandleLoadLocal;
            dispatch[(byte)OpCode.LoadLocal_0] = &HandleLoadLocal_0;
            dispatch[(byte)OpCode.LoadLocal_1] = &HandleLoadLocal_1;
            dispatch[(byte)OpCode.LoadLocal_2] = &HandleLoadLocal_2;
            dispatch[(byte)OpCode.LoadLocal_3] = &HandleLoadLocal_3;
            dispatch[(byte)OpCode.StoreLocal] = &HandleStoreLocal;
            dispatch[(byte)OpCode.StoreLocal_0] = &HandleStoreLocal_0;
            dispatch[(byte)OpCode.StoreLocal_1] = &HandleStoreLocal_1;
            dispatch[(byte)OpCode.StoreLocal_2] = &HandleStoreLocal_2;
            dispatch[(byte)OpCode.StoreLocal_3] = &HandleStoreLocal_3;
            dispatch[(byte)OpCode.CallNative] = &HandleCallNative;
            dispatch[(byte)OpCode.Call] = &HandleCall;
            dispatch[(byte)OpCode.RetUserFunc] = &HandleRetUserFunc;
            dispatch[(byte)OpCode.Equal] = &HandleEqual;
            dispatch[(byte)OpCode.NotEqual] = &HandleNotEqual;
            dispatch[(byte)OpCode.Less] = &HandleLess;
            dispatch[(byte)OpCode.Greater] = &HandleGreater;
            dispatch[(byte)OpCode.LessEqual] = &HandleLessEqual;
            dispatch[(byte)OpCode.GreaterEqual] = &HandleGreaterEqual;
            dispatch[(byte)OpCode.StructNew] = &HandleStructNew;
            dispatch[(byte)OpCode.StructGet] = &HandleStructGet;
            dispatch[(byte)OpCode.StructSet] = &HandleStructSet;
            dispatch[(byte)OpCode.Pop] = &HandlePop;
            dispatch[(byte)OpCode.BufferNew] = &HandleBufferNew;
            dispatch[(byte)OpCode.BufferGet] = &HandleBufferGet;
            dispatch[(byte)OpCode.BufferSet] = &HandleBufferSet;
            dispatch[(byte)OpCode.PushConst16] = &HandlePushConst16;
            dispatch[(byte)OpCode.Dup] = &HandleDup;
            dispatch[(byte)OpCode.Dup2] = &HandleDup2;
            dispatch[(byte)OpCode.BufferSlice] = &HandleBufferSlice;
            dispatch[(byte)OpCode.BufferSliceAssign] = &HandleBufferSliceAssign;
            dispatch[(byte)OpCode.PushConstInt_0] = &HandlePushConstInt_0;
            dispatch[(byte)OpCode.PushConstInt_1] = &HandlePushConstInt_1;
            dispatch[(byte)OpCode.PushConstInt8] = &HandlePushConstInt8;
            dispatch[(byte)OpCode.StoreLocalDup] = &HandleStoreLocalDup;
            dispatch[(byte)OpCode.StoreLocalDup_0] = &HandleStoreLocalDup_0;
            dispatch[(byte)OpCode.StoreLocalDup_1] = &HandleStoreLocalDup_1;
            dispatch[(byte)OpCode.StoreLocalDup_2] = &HandleStoreLocalDup_2;
            dispatch[(byte)OpCode.StoreLocalDup_3] = &HandleStoreLocalDup_3;
            dispatch[(byte)OpCode.BufferFromStack] = &HandleBufferFromStack;
            dispatch[(byte)OpCode.Inc] = &HandleInc;
            dispatch[(byte)OpCode.Dec] = &HandleDec;
            dispatch[(byte)OpCode.PushNone] = &HandlePushNone;
            dispatch[(byte)OpCode.PushExceptionHandler] = &HandlePushExceptionHandler;
            dispatch[(byte)OpCode.PopExceptionHandler] = &HandlePopExceptionHandler;
            dispatch[(byte)OpCode.Throw] = &HandleThrow;
            dispatch[(byte)OpCode.PushExceptionValue] = &HandlePushExceptionValue;
            dispatch[(byte)OpCode.EndFinally] = &HandleEndFinally;

            VMContext ctx = new()
            {
                IP = pInstructions,
                BaseAddress = pInstructions,
                Stack = pStack,
                StackTop = StackTop,
                Constants = pConstants,
                ReturnAddressStack = pRetAddr,
                FrameBaseStack = pFrameBase,
                CallStackTop = _callStackTop,
                FrameBase = _frameBase,
                HeapBase = pHeap,
                StringPoolBase = pStringPool,
                IsRunning = true,
            };

            VMContext* pCtx = &ctx;

            // core indirect threading dispatcher loop
            while (pCtx->IsRunning)
            {
                byte opcode = *pCtx->IP++;
                dispatch[opcode](this, pCtx);
            }

            // push synced final states back onto the heap tracking bounds
            StackTop = pCtx->StackTop;
            _callStackTop = pCtx->CallStackTop;
            _frameBase = pCtx->FrameBase;
        }
    }

    #region Opcode Pointer Handlers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleUnknown(VM vm, VMContext* ctx)
    {
        ctx->IsRunning = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleReturn(VM vm, VMContext* ctx)
    {
        ctx->IsRunning = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandlePushConst(VM vm, VMContext* ctx)
    {
        byte index = *ctx->IP++;
        ctx->Stack[ctx->StackTop++] = ctx->Constants[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandlePushConst16(VM vm, VMContext* ctx)
    {
        ushort index = Unsafe.ReadUnaligned<ushort>(ctx->IP);
        ctx->IP += 2;
        ctx->Stack[ctx->StackTop++] = ctx->Constants[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ToFloat(NovaValue val)
    {
        return val.Type == ValueType.Float ? val.AsFloat : val.AsInt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandlePushConstInt_0(VM vm, VMContext* ctx)
    {
        ctx->Stack[ctx->StackTop++] = NovaValue.Int(0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandlePushConstInt_1(VM vm, VMContext* ctx)
    {
        ctx->Stack[ctx->StackTop++] = NovaValue.Int(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandlePushConstInt8(VM vm, VMContext* ctx)
    {
        sbyte val = (sbyte)*ctx->IP++;
        ctx->Stack[ctx->StackTop++] = NovaValue.Int(val);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandlePushNone(VM vm, VMContext* ctx)
    {
        ctx->Stack[ctx->StackTop++] = new NovaValue { Type = ValueType.Null };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleAdd(VM vm, VMContext* ctx)
    {
        NovaValue b = ctx->Stack[--ctx->StackTop];
        NovaValue a = ctx->Stack[--ctx->StackTop];

        if (a.Type == ValueType.String || b.Type == ValueType.String)
        {
            byte* poolBase = ctx->StringPoolBase;
            byte* bufA = stackalloc byte[64];
            byte* bufB = stackalloc byte[64];
            int lenA = ValueToUtf8(a, poolBase, bufA, 64);
            int lenB = ValueToUtf8(b, poolBase, bufB, 64);
            int total = lenA + lenB;
            byte* combined = stackalloc byte[total];
            Unsafe.CopyBlock(combined, bufA, (uint)lenA);
            Unsafe.CopyBlock(combined + lenA, bufB, (uint)lenB);
            int newOff = vm.Strings!.InternFromBytes(combined, total);
            ctx->StringPoolBase = vm.Strings.GetBuffer();
            ctx->Stack[ctx->StackTop++] = NovaValue.String(newOff);
            return;
        }

        if (a.Type == ValueType.Int && b.Type == ValueType.Int)
            ctx->Stack[ctx->StackTop++] = NovaValue.Int(a.AsInt + b.AsInt);
        else
            ctx->Stack[ctx->StackTop++] = NovaValue.Float(ToFloat(a) + ToFloat(b));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ValueToUtf8(NovaValue val, byte* poolBase, byte* buf, int bufSize)
    {
        switch (val.Type)
        {
            case ValueType.String:
            {
                int len = StringPool.ReadLength(poolBase, val.ObjectId);
                byte* src = StringPool.GetData(poolBase, val.ObjectId);
                Unsafe.CopyBlock(buf, src, (uint)len);
                return len;
            }
            case ValueType.Int:
            {
                Utf8Formatter.TryFormat(val.AsInt, new Span<byte>(buf, bufSize), out int written);
                return written;
            }
            case ValueType.Float:
            {
                Utf8Formatter.TryFormat(val.AsFloat, new Span<byte>(buf, bufSize), out int written);
                return written;
            }
            case ValueType.Bool:
            {
                if (val.AsBool)
                {
                    buf[0] = (byte)'T';
                    buf[1] = (byte)'r';
                    buf[2] = (byte)'u';
                    buf[3] = (byte)'e';
                    return 4;
                }
                else
                {
                    buf[0] = (byte)'F';
                    buf[1] = (byte)'a';
                    buf[2] = (byte)'l';
                    buf[3] = (byte)'s';
                    buf[4] = (byte)'e';
                    return 5;
                }
            }
            case ValueType.Null:
                buf[0] = (byte)'n';
                buf[1] = (byte)'u';
                buf[2] = (byte)'l';
                buf[3] = (byte)'l';
                return 4;
            case ValueType.ObjectRef:
            {
                int id = val.ObjectId;
                int pos = 0;
                buf[pos++] = (byte)'[';
                buf[pos++] = (byte)'O';
                buf[pos++] = (byte)'b';
                buf[pos++] = (byte)'j';
                buf[pos++] = (byte)'R';
                buf[pos++] = (byte)'e';
                buf[pos++] = (byte)'f';
                buf[pos++] = (byte)':';
                pos += FormatInt(id, buf + pos);
                buf[pos++] = (byte)']';
                return pos;
            }
            default:
                return 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FormatInt(int value, byte* buf)
    {
        if (value == 0)
        {
            *buf = (byte)'0';
            return 1;
        }
        bool neg = value < 0;
        uint u = neg ? (uint)-value : (uint)value;
        byte* start = buf;
        while (u != 0)
        {
            *buf++ = (byte)('0' + u % 10);
            u /= 10;
        }
        if (neg)
            *buf++ = (byte)'-';
        int len = (int)(buf - start);
        // rverse in place
        byte* left = start;
        byte* right = buf - 1;
        while (left < right)
        {
            byte tmp = *left;
            *left = *right;
            *right = tmp;
            left++;
            right--;
        }
        return len;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleSub(VM vm, VMContext* ctx)
    {
        NovaValue b = ctx->Stack[--ctx->StackTop];
        NovaValue a = ctx->Stack[--ctx->StackTop];

        if (a.Type == ValueType.Int && b.Type == ValueType.Int)
            ctx->Stack[ctx->StackTop++] = NovaValue.Int(a.AsInt - b.AsInt);
        else
            ctx->Stack[ctx->StackTop++] = NovaValue.Float(ToFloat(a) - ToFloat(b));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleMul(VM vm, VMContext* ctx)
    {
        NovaValue b = ctx->Stack[--ctx->StackTop];
        NovaValue a = ctx->Stack[--ctx->StackTop];

        if (a.Type == ValueType.Int && b.Type == ValueType.Int)
            ctx->Stack[ctx->StackTop++] = NovaValue.Int(a.AsInt * b.AsInt);
        else
            ctx->Stack[ctx->StackTop++] = NovaValue.Float(ToFloat(a) * ToFloat(b));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleDiv(VM vm, VMContext* ctx)
    {
        NovaValue b = ctx->Stack[--ctx->StackTop];
        NovaValue a = ctx->Stack[--ctx->StackTop];

        if (a.Type == ValueType.Int && b.Type == ValueType.Int)
            ctx->Stack[ctx->StackTop++] =
                b.AsInt == 0 ? NovaValue.Int(0) : NovaValue.Int(a.AsInt / b.AsInt);
        else
            ctx->Stack[ctx->StackTop++] = NovaValue.Float(ToFloat(a) / ToFloat(b));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CompareEqual(NovaValue a, NovaValue b)
    {
        if (a.Type == ValueType.Int && b.Type == ValueType.Int)
            return a.AsInt == b.AsInt;
        return ToFloat(a) == ToFloat(b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleEqual(VM vm, VMContext* ctx)
    {
        NovaValue b = ctx->Stack[--ctx->StackTop];
        NovaValue a = ctx->Stack[--ctx->StackTop];
        ctx->Stack[ctx->StackTop++] = NovaValue.Bool(CompareEqual(a, b));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleNotEqual(VM vm, VMContext* ctx)
    {
        NovaValue b = ctx->Stack[--ctx->StackTop];
        NovaValue a = ctx->Stack[--ctx->StackTop];
        ctx->Stack[ctx->StackTop++] = NovaValue.Bool(!CompareEqual(a, b));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleLess(VM vm, VMContext* ctx)
    {
        NovaValue b = ctx->Stack[--ctx->StackTop];
        NovaValue a = ctx->Stack[--ctx->StackTop];

        bool result;
        if (a.Type == ValueType.Int && b.Type == ValueType.Int)
            result = a.AsInt < b.AsInt;
        else
            result = ToFloat(a) < ToFloat(b);
        ctx->Stack[ctx->StackTop++] = NovaValue.Bool(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleGreater(VM vm, VMContext* ctx)
    {
        NovaValue b = ctx->Stack[--ctx->StackTop];
        NovaValue a = ctx->Stack[--ctx->StackTop];

        bool result;
        if (a.Type == ValueType.Int && b.Type == ValueType.Int)
            result = a.AsInt > b.AsInt;
        else
            result = ToFloat(a) > ToFloat(b);
        ctx->Stack[ctx->StackTop++] = NovaValue.Bool(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleLessEqual(VM vm, VMContext* ctx)
    {
        NovaValue b = ctx->Stack[--ctx->StackTop];
        NovaValue a = ctx->Stack[--ctx->StackTop];

        bool result;
        if (a.Type == ValueType.Int && b.Type == ValueType.Int)
            result = a.AsInt <= b.AsInt;
        else
            result = ToFloat(a) <= ToFloat(b);
        ctx->Stack[ctx->StackTop++] = NovaValue.Bool(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleGreaterEqual(VM vm, VMContext* ctx)
    {
        NovaValue b = ctx->Stack[--ctx->StackTop];
        NovaValue a = ctx->Stack[--ctx->StackTop];

        bool result;
        if (a.Type == ValueType.Int && b.Type == ValueType.Int)
            result = a.AsInt >= b.AsInt;
        else
            result = ToFloat(a) >= ToFloat(b);
        ctx->Stack[ctx->StackTop++] = NovaValue.Bool(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleJumpIfFalse(VM vm, VMContext* ctx)
    {
        byte jumpOffset = *ctx->IP++;
        NovaValue condition = ctx->Stack[--ctx->StackTop];
        if (!condition.AsBool)
        {
            ctx->IP += jumpOffset;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleJump(VM vm, VMContext* ctx)
    {
        sbyte jOffset = (sbyte)*ctx->IP++;
        ctx->IP += jOffset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleLoadLocal(VM vm, VMContext* ctx)
    {
        byte index = *ctx->IP++;
        NovaValue val = ctx->Stack[ctx->FrameBase + index];
        if (val.Type == ValueType.Null)
        {
            if (ThrowGuard(vm, ctx, "Cannot read uninitialized variable."))
                return;
        }
        ctx->Stack[ctx->StackTop++] = val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStoreLocal(VM vm, VMContext* ctx)
    {
        byte index = *ctx->IP++;
        ctx->Stack[ctx->FrameBase + index] = ctx->Stack[--ctx->StackTop];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleLoadLocal_0(VM vm, VMContext* ctx)
    {
        NovaValue val = ctx->Stack[ctx->FrameBase + 0];
        if (val.Type == ValueType.Null)
        {
            if (ThrowGuard(vm, ctx, "Cannot read uninitialized variable."))
                return;
        }
        ctx->Stack[ctx->StackTop++] = val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleLoadLocal_1(VM vm, VMContext* ctx)
    {
        NovaValue val = ctx->Stack[ctx->FrameBase + 1];
        if (val.Type == ValueType.Null)
        {
            if (ThrowGuard(vm, ctx, "Cannot read uninitialized variable."))
                return;
        }
        ctx->Stack[ctx->StackTop++] = val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleLoadLocal_2(VM vm, VMContext* ctx)
    {
        NovaValue val = ctx->Stack[ctx->FrameBase + 2];
        if (val.Type == ValueType.Null)
        {
            if (ThrowGuard(vm, ctx, "Cannot read uninitialized variable."))
                return;
        }
        ctx->Stack[ctx->StackTop++] = val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleLoadLocal_3(VM vm, VMContext* ctx)
    {
        NovaValue val = ctx->Stack[ctx->FrameBase + 3];
        if (val.Type == ValueType.Null)
        {
            if (ThrowGuard(vm, ctx, "Cannot read uninitialized variable."))
                return;
        }
        ctx->Stack[ctx->StackTop++] = val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStoreLocal_0(VM vm, VMContext* ctx)
    {
        ctx->Stack[ctx->FrameBase + 0] = ctx->Stack[--ctx->StackTop];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStoreLocal_1(VM vm, VMContext* ctx)
    {
        ctx->Stack[ctx->FrameBase + 1] = ctx->Stack[--ctx->StackTop];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStoreLocal_2(VM vm, VMContext* ctx)
    {
        ctx->Stack[ctx->FrameBase + 2] = ctx->Stack[--ctx->StackTop];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStoreLocal_3(VM vm, VMContext* ctx)
    {
        ctx->Stack[ctx->FrameBase + 3] = ctx->Stack[--ctx->StackTop];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStoreLocalDup(VM vm, VMContext* ctx)
    {
        byte index = *ctx->IP++;
        ctx->Stack[ctx->FrameBase + index] = ctx->Stack[ctx->StackTop - 1];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStoreLocalDup_0(VM vm, VMContext* ctx)
    {
        ctx->Stack[ctx->FrameBase + 0] = ctx->Stack[ctx->StackTop - 1];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStoreLocalDup_1(VM vm, VMContext* ctx)
    {
        ctx->Stack[ctx->FrameBase + 1] = ctx->Stack[ctx->StackTop - 1];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStoreLocalDup_2(VM vm, VMContext* ctx)
    {
        ctx->Stack[ctx->FrameBase + 2] = ctx->Stack[ctx->StackTop - 1];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStoreLocalDup_3(VM vm, VMContext* ctx)
    {
        ctx->Stack[ctx->FrameBase + 3] = ctx->Stack[ctx->StackTop - 1];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleCall(VM vm, VMContext* ctx)
    {
        if (ctx->CallStackTop >= vm._callStackCapacity)
        {
            if (ThrowGuard(vm, ctx, $"Call stack depth exceeded (max {vm._callStackCapacity})."))
                return;
        }

        byte targetAddr = *ctx->IP++;
        byte argCount = *ctx->IP++;

        if (ctx->StackTop + 8 > vm._valueStackCapacity)
        {
            if (ThrowGuard(vm, ctx, $"Value stack overflow (max {vm._valueStackCapacity} slots)."))
                return;
        }

        ctx->ReturnAddressStack[ctx->CallStackTop] = ctx->IP;
        ctx->FrameBaseStack[ctx->CallStackTop] = ctx->FrameBase;
        ctx->CallStackTop++;

        ctx->FrameBase = ctx->StackTop - argCount;
        ctx->IP = ctx->BaseAddress + targetAddr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRetUserFunc(VM vm, VMContext* ctx)
    {
        NovaValue retVal = ctx->Stack[--ctx->StackTop];
        ctx->CallStackTop--;

        ctx->IP = ctx->ReturnAddressStack[ctx->CallStackTop];
        int oldFrameBase = ctx->FrameBaseStack[ctx->CallStackTop];

        ctx->StackTop = ctx->FrameBase;
        ctx->Stack[ctx->StackTop++] = retVal;
        ctx->FrameBase = oldFrameBase;
    }

    // PushExceptionHandler: creates a new handler for a try block.
    // Operand is a 16-bit bytecode offset to the catch block (relative to base).
    // Records the current frame/stack/depth so a throw can unwind to it.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandlePushExceptionHandler(VM vm, VMContext* ctx)
    {
        ushort catchOffset = Unsafe.ReadUnaligned<ushort>(ctx->IP);
        ctx->IP += 2;

        if (vm._handlerTop >= vm._callStackCapacity)
        {
            // As a last resort treat exceeding handler depth as a rethrow upward.
            ctx->IsRunning = false;
            throw new Exception("Runtime Error: Exception handler stack exhausted.");
        }

        int top = vm._handlerTop;
        vm._handlerTargetStack[top] = catchOffset;
        vm._handlerStackTop[top] = ctx->StackTop;
        vm._handlerFrameBase[top] = ctx->FrameBase;
        vm._handlerCallTop[top] = ctx->CallStackTop;
        vm._handlerTop = top + 1;
    }

    // PopExceptionHandler: discards the most recent handler (normal completion of a try).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandlePopExceptionHandler(VM vm, VMContext* ctx)
    {
        if (vm._handlerTop > 0)
            vm._handlerTop--;
    }

    // Throw: if any handler is active, unwind the stack/frames back to that
    // handler's saved state and jump to its catch block. Otherwise, the throw
    // propagates up to the host as a runtime error.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleThrow(VM vm, VMContext* ctx)
    {
        NovaValue thrown = ctx->Stack[--ctx->StackTop];
        vm._exceptionValue = thrown;

        if (vm._handlerTop == 0)
        {
            ctx->IsRunning = false;
            throw new Exception(VmValueException(vm, ctx, thrown));
        }

        vm._handlerTop--;
        int top = vm._handlerTop;

        ctx->IP = ctx->BaseAddress + vm._handlerTargetStack[top];
        ctx->StackTop = vm._handlerStackTop[top];
        ctx->FrameBase = vm._handlerFrameBase[top];
        ctx->CallStackTop = vm._handlerCallTop[top];
    }

    // PushExceptionValue: pushes the value of the most recent thrown expression
    // onto the stack so a catch block can bind it to a variable.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandlePushExceptionValue(VM vm, VMContext* ctx)
    {
        ctx->Stack[ctx->StackTop++] = vm._exceptionValue;
    }

    // EndFinally: ends a finally block. The finally body is entered with an
    // Int marker on the stack: 0 = normal completion (just exit), 1 = the body
    // was entered from an exception unwind, so the pending exception must keep
    // propagating to the enclosing handler (or host if none remains).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleEndFinally(VM vm, VMContext* ctx)
    {
        NovaValue marker = ctx->Stack[--ctx->StackTop];
        if (marker.AsInt != 1)
            return;

        if (vm._handlerTop == 0)
        {
            ctx->IsRunning = false;
            throw new Exception(VmValueException(vm, ctx, vm._exceptionValue));
        }

        vm._handlerTop--;
        int top = vm._handlerTop;

        ctx->IP = ctx->BaseAddress + vm._handlerTargetStack[top];
        ctx->StackTop = vm._handlerStackTop[top];
        ctx->FrameBase = vm._handlerFrameBase[top];
        ctx->CallStackTop = vm._handlerCallTop[top];
    }

    // Raises a guard/runtime error. If a Nova try/catch handler is active, the
    // error is converted to a thrown string (so it can be caught), unwinding to
    // the nearest handler and jumping to its catch block, and returns true. With
    // no active handler it propagates up to the host as a standard exception.
    // Callers MUST return immediately when this returns true, because the IP and
    // stack have already been rewound to the catch block state.
    private static bool ThrowGuard(VM vm, VMContext* ctx, string message)
    {
        if (vm._handlerTop == 0 || vm.Strings == null)
        {
            ctx->IsRunning = false;
            throw new Exception("Runtime Error: " + message);
        }

        int off = vm.Strings.Intern(message.AsSpan());
        ctx->StringPoolBase = vm.Strings.GetBuffer();
        vm._exceptionValue = NovaValue.String(off);

        vm._handlerTop--;
        int top = vm._handlerTop;

        ctx->IP = ctx->BaseAddress + vm._handlerTargetStack[top];
        ctx->StackTop = vm._handlerStackTop[top];
        ctx->FrameBase = vm._handlerFrameBase[top];
        ctx->CallStackTop = vm._handlerCallTop[top];
        return true;
    }

    private static string VmValueException(VM vm, VMContext* ctx, NovaValue value)
    {
        return value.Type switch
        {
            ValueType.Int => $"Runtime Error: Unhandled exception: {value.AsInt}",
            ValueType.Float => $"Runtime Error: Unhandled exception: {value.AsFloat}",
            ValueType.Bool =>
                $"Runtime Error: Unhandled exception: {(value.AsBool ? "true" : "false")}",
            ValueType.String =>
                $"Runtime Error: Unhandled exception: {vm.Strings!.GetString(value.ObjectId)}",
            _ => "Runtime Error: Unhandled exception.",
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleCallNative(VM vm, VMContext* ctx)
    {
        byte nativeIndex = *ctx->IP++;

        // sync localized stack configs back into the VM object context
        // so any host callbacks calling vm.Pop() or vm.Push() map accurately
        vm.StackTop = ctx->StackTop;

        vm._nativeFunctions[nativeIndex](vm);

        // reacquire post-execution mutation traces back from host environment limits
        ctx->StackTop = vm.StackTop;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStructNew(VM vm, VMContext* ctx)
    {
        byte fieldCount = *ctx->IP++;
        int size = fieldCount * 8;
        int handle = vm.Heap.Alloc(size);
        ctx->HeapBase = vm.Heap.GetBuffer();
        ctx->Stack[ctx->StackTop++] = new NovaValue
        {
            Type = ValueType.ObjectRef,
            ObjectId = handle,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStructGet(VM vm, VMContext* ctx)
    {
        byte fieldIndex = *ctx->IP++;
        NovaValue handle = ctx->Stack[--ctx->StackTop];
        int offset = handle.ObjectId + fieldIndex * 8;
        ctx->Stack[ctx->StackTop++] = *(NovaValue*)(ctx->HeapBase + offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStructSet(VM vm, VMContext* ctx)
    {
        byte fieldIndex = *ctx->IP++;
        NovaValue value = ctx->Stack[--ctx->StackTop];
        NovaValue handle = ctx->Stack[--ctx->StackTop];
        int offset = handle.ObjectId + fieldIndex * 8;
        *(NovaValue*)(ctx->HeapBase + offset) = value;
        ctx->Stack[ctx->StackTop++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandlePop(VM vm, VMContext* ctx)
    {
        ctx->StackTop--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleDup(VM vm, VMContext* ctx)
    {
        NovaValue top = ctx->Stack[ctx->StackTop - 1];
        ctx->Stack[ctx->StackTop++] = top;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleDup2(VM vm, VMContext* ctx)
    {
        NovaValue a = ctx->Stack[ctx->StackTop - 2];
        NovaValue b = ctx->Stack[ctx->StackTop - 1];
        ctx->Stack[ctx->StackTop] = a;
        ctx->Stack[ctx->StackTop + 1] = b;
        ctx->StackTop += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleBufferNew(VM vm, VMContext* ctx)
    {
        NovaValue countVal = ctx->Stack[--ctx->StackTop];
        int count = countVal.Type == ValueType.Float ? (int)countVal.AsFloat : countVal.AsInt;
        int userHandle = vm.Heap.AllocBuffer(count);
        ctx->HeapBase = vm.Heap.GetBuffer();
        ctx->Stack[ctx->StackTop++] = new NovaValue
        {
            Type = ValueType.ObjectRef,
            ObjectId = userHandle,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AsIndex(NovaValue val)
    {
        return val.Type == ValueType.Float ? (int)val.AsFloat : val.AsInt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BufCount(byte* heapBase, int userHandle)
    {
        return (*(NovaValue*)(heapBase + userHandle - 8)).AsInt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleBufferGet(VM vm, VMContext* ctx)
    {
        int index = AsIndex(ctx->Stack[--ctx->StackTop]);
        NovaValue handle = ctx->Stack[--ctx->StackTop];
        int count = BufCount(ctx->HeapBase, handle.ObjectId);
        if (index < 0 || index >= count)
        {
            if (ThrowGuard(vm, ctx, $"Buffer index {index} out of bounds (length {count})."))
                return;
        }
        int offset = handle.ObjectId + index * 8;
        ctx->Stack[ctx->StackTop++] = *(NovaValue*)(ctx->HeapBase + offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleBufferSet(VM vm, VMContext* ctx)
    {
        NovaValue value = ctx->Stack[--ctx->StackTop];
        int index = AsIndex(ctx->Stack[--ctx->StackTop]);
        NovaValue handle = ctx->Stack[--ctx->StackTop];
        int count = BufCount(ctx->HeapBase, handle.ObjectId);
        if (index < 0 || index >= count)
        {
            if (ThrowGuard(vm, ctx, $"Buffer index {index} out of bounds (length {count})."))
                return;
        }
        int offset = handle.ObjectId + index * 8;
        *(NovaValue*)(ctx->HeapBase + offset) = value;
        ctx->Stack[ctx->StackTop++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleBufferFromStack(VM vm, VMContext* ctx)
    {
        byte count = *ctx->IP++;
        int handle = vm.Heap.AllocBuffer(count);
        ctx->HeapBase = vm.Heap.GetBuffer();
        NovaValue* dest = (NovaValue*)(ctx->HeapBase + handle);
        for (int i = count - 1; i >= 0; i--)
            dest[i] = ctx->Stack[--ctx->StackTop];
        ctx->Stack[ctx->StackTop++] = new NovaValue
        {
            Type = ValueType.ObjectRef,
            ObjectId = handle,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleInc(VM vm, VMContext* ctx)
    {
        byte slot = *ctx->IP++;
        NovaValue val = ctx->Stack[ctx->FrameBase + slot];
        ctx->Stack[ctx->StackTop++] = val;
        if (val.Type == ValueType.Float)
            ctx->Stack[ctx->FrameBase + slot] = NovaValue.Float(val.AsFloat + 1.0f);
        else
            ctx->Stack[ctx->FrameBase + slot] = NovaValue.Int(val.AsInt + 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleDec(VM vm, VMContext* ctx)
    {
        byte slot = *ctx->IP++;
        NovaValue val = ctx->Stack[ctx->FrameBase + slot];
        ctx->Stack[ctx->StackTop++] = val;
        if (val.Type == ValueType.Float)
            ctx->Stack[ctx->FrameBase + slot] = NovaValue.Float(val.AsFloat - 1.0f);
        else
            ctx->Stack[ctx->FrameBase + slot] = NovaValue.Int(val.AsInt - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleBufferSliceAssign(VM vm, VMContext* ctx)
    {
        NovaValue srcHandle = ctx->Stack[--ctx->StackTop];
        NovaValue endVal = ctx->Stack[--ctx->StackTop];
        NovaValue startVal = ctx->Stack[--ctx->StackTop];
        NovaValue dstHandle = ctx->Stack[--ctx->StackTop];

        int start = AsIndex(startVal);
        int end = AsIndex(endVal);
        int dstCount = BufCount(ctx->HeapBase, dstHandle.ObjectId);
        if (start < 0 || start > end || end > dstCount)
        {
            if (
                ThrowGuard(
                    vm,
                    ctx,
                    $"Buffer slice assign [{start}..{end}] out of bounds (length {dstCount})."
                )
            )
                return;
        }

        Unsafe.CopyBlock(
            ctx->HeapBase + dstHandle.ObjectId + start * 8,
            ctx->HeapBase + srcHandle.ObjectId,
            (uint)((end - start) * 8)
        );

        ctx->Stack[ctx->StackTop++] = srcHandle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleBufferSlice(VM vm, VMContext* ctx)
    {
        NovaValue endVal = ctx->Stack[--ctx->StackTop];
        NovaValue startVal = ctx->Stack[--ctx->StackTop];
        NovaValue handle = ctx->Stack[--ctx->StackTop];

        int start = AsIndex(startVal);
        int end = AsIndex(endVal);
        int srcCount = BufCount(ctx->HeapBase, handle.ObjectId);
        if (start < 0 || start > end || end > srcCount)
        {
            if (
                ThrowGuard(
                    vm,
                    ctx,
                    $"Buffer slice [{start}..{end}] out of bounds (length {srcCount})."
                )
            )
                return;
        }
        int count = end - start;

        int newHandle = vm.Heap.AllocBuffer(count);
        ctx->HeapBase = vm.Heap.GetBuffer();

        Unsafe.CopyBlock(
            ctx->HeapBase + newHandle,
            ctx->HeapBase + handle.ObjectId + start * 8,
            (uint)(count * 8)
        );

        ctx->Stack[ctx->StackTop++] = new NovaValue
        {
            Type = ValueType.ObjectRef,
            ObjectId = newHandle,
        };
    }

    #endregion
}
