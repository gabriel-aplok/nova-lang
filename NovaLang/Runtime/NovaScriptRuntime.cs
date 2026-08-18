using System.Diagnostics;
using NovaLang.Compiler;

namespace NovaLang.Runtime;

public class NovaScriptRuntime
{
    private readonly string[] _nativeNames = new string[256];
    private readonly Action<VM>[] _nativeCallbacks = new Action<VM>[256];
    private byte _nativeCount = 0;

    public void RegisterFunction(string name, Action<VM> callback)
    {
        _nativeNames[_nativeCount] = name;
        _nativeCallbacks[_nativeCount] = callback;
        _nativeCount++;
    }

    public ReadOnlySpan<string> GetNativeNames()
    {
        return new(_nativeNames, 0, _nativeCount);
    }

    public int GetNativeCount()
    {
        return _nativeCount;
    }

    public Action<VM> GetNativeCallback(int index)
    {
        return _nativeCallbacks[index];
    }

    public void Execute(string sourceCode, bool quiet = false)
    {
        Chunk chunk = new();
        Local[] localsBuffer = new Local[8192];
        SymbolEntry[] symbolEntries = new SymbolEntry[256];
        StructLayout[] structLayouts = new StructLayout[16];
        StructField[] structFields = new StructField[64];
        HeapAllocator heap = new();
        StringPool stringPool = new();

        if (!quiet)
        {
            Console.WriteLine("====================================================");
            Console.WriteLine("Nova Compiler");
            Console.WriteLine("====================================================");
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long memBefore = GC.GetTotalMemory(true);

        Stopwatch stopwatch = Stopwatch.StartNew();

        ReadOnlySpan<string> nativeNamesSpan = new(_nativeNames, 0, _nativeCount);
        Parser parser = new(
            sourceCode,
            chunk,
            localsBuffer,
            symbolEntries,
            structLayouts,
            structFields,
            nativeNamesSpan,
            stringPool
        );
        parser.Compile();
        if (Environment.GetEnvironmentVariable("NOVA_DUMP") == "1")
        {
            Console.WriteLine("--- bytecode dump ---");
            int ip = 0;
            var names = Enum.GetNames(typeof(OpCode));
            while (ip < chunk.Code.Count)
            {
                OpCode op = (OpCode)chunk.Code[ip];
                string name = names[(byte)op];
                int size = op switch
                {
                    OpCode.PushConst or OpCode.PushConst16 or OpCode.LoadLocal
                    or OpCode.StoreLocal or OpCode.StoreLocalDup or OpCode.BufferFromStack
                    or OpCode.Inc or OpCode.Dec or OpCode.StructSet or OpCode.StructGet
                    or OpCode.BufferSet or OpCode.BufferGet or OpCode.StructNew or OpCode.CallNative
                    or OpCode.Jump or OpCode.JumpIfFalse or OpCode.PushConstInt8 => 2,
                    OpCode.Call or OpCode.PushExceptionHandler => 3,
                    _ => 1,
                };
                string extra = size > 1
                    ? string.Join(" ", Enumerable.Range(1, size - 1).Select(k => chunk.Code[ip + k].ToString()))
                    : "";
                Console.WriteLine($"{ip,4}: {name,-24} {extra}");
                ip += size;
            }
            Console.WriteLine("--- constants ---");
            for (int i = 0; i < chunk.Constants.Count; i++)
                Console.WriteLine($"{i}: {chunk.Constants[i].Type} {chunk.Constants[i].AsInt}/{chunk.Constants[i].AsFloat}/{chunk.Constants[i].ObjectId}");
            Console.WriteLine("--- end dump ---");
        }

        stopwatch.Stop();
        long memAfter = GC.GetTotalMemory(false);

        if (!quiet)
        {
            double msElapsed = stopwatch.Elapsed.TotalMilliseconds;
            double kbAllocated = (memAfter - memBefore) / 1024.0;
            if (kbAllocated < 0)
                kbAllocated = 0;

            Console.WriteLine("----------------------------------------------------");
            Console.WriteLine("Results: Compilation Phase");
            Console.WriteLine("----------------------------------------------------");
            Console.WriteLine($" - Compile Time    : {msElapsed:F4} ms");
            Console.WriteLine($" - Memory Used     : {kbAllocated:F2} KB");
            Console.WriteLine($" - Bytecode Stream : {chunk.Code.Count} Bytes Generated");
            Console.WriteLine($" - Constants Pool  : {chunk.Constants.Count} Constants Cached");
            Console.WriteLine($" - String Pool     : {stringPool.DataSize} Bytes Used");
            Console.WriteLine("----------------------------------------------------\n");
        }

        if (!quiet)
        {
            Console.WriteLine("====================================================");
            Console.WriteLine("Executing Hot Loop Benchmark");
            Console.WriteLine("====================================================");
        }

        Stopwatch vmStopwatch = Stopwatch.StartNew();

        VM vm = new([.. chunk.Code], [.. chunk.Constants]) { Heap = heap, Strings = stringPool };
        for (int i = 0; i < _nativeCount; i++)
            vm.RegisterNative(i, _nativeCallbacks[i]);

        vm.Execute();

        vmStopwatch.Stop();

        if (!quiet)
        {
            Console.WriteLine("----------------------------------------------------");
            Console.WriteLine("Results: Runtime Phase");
            Console.WriteLine("----------------------------------------------------");
            Console.WriteLine(
                $" - VM Hot Loop     : {vmStopwatch.Elapsed.TotalMilliseconds:F2} ms"
            );
            Console.WriteLine("====================================================\n");
        }
    }
}
