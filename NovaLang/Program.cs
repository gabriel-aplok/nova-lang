using System.Runtime.CompilerServices;
using System.Text;
using NovaLang.Compiler;
using NovaLang.Runtime;

namespace NovaLang;

using VType = NovaLang.Runtime.ValueType;

internal class Program
{
    private static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            RunBenchmark();
            return;
        }

        switch (args[0])
        {
            case "--compile" when args.Length >= 3:
            {
                string srcPath = args[1];
                string outPath = args[2];
                if (!File.Exists(srcPath))
                {
                    Console.Error.WriteLine($"Error: source file '{srcPath}' not found.");
                    return;
                }
                CompileToBinary(srcPath, outPath);
                break;
            }
            case "--run" when args.Length >= 2:
                ExecuteBinary(args[1]);
                break;
            default:
            {
                string path = args[0];
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"Error: file '{path}' not found.");
                    Console.Error.WriteLine("Usage:");
                    Console.Error.WriteLine("  NovaLang.exe                    - run benchmark");
                    Console.Error.WriteLine(
                        "  NovaLang.exe <file>            - compile & run .nova script"
                    );
                    Console.Error.WriteLine(
                        "  NovaLang.exe --compile <src> <out> - compile .nova to .novab"
                    );
                    Console.Error.WriteLine("  NovaLang.exe --run <file>      - run .novab binary");
                    return;
                }
                RunFile(path);
                break;
            }
        }
    }

    private static unsafe NovaScriptRuntime CreateRuntime()
    {
        NovaScriptRuntime rt = new();

        static double GetDouble(NovaValue val) => val.Type == VType.Float ? val.AsFloat : val.AsInt;

        rt.RegisterFunction(
            "Print",
            (vm) =>
            {
                NovaValue val = vm.Pop();
                if (val.Type == VType.Int)
                    Console.Write(val.AsInt);
                else if (val.Type == VType.Float)
                    Console.Write(val.AsFloat);
                else if (val.Type == VType.Bool)
                    Console.Write(val.AsBool);
                else if (val.Type == VType.String)
                    Console.Write(vm.Strings?.GetString(val.ObjectId) ?? "?");
                else if (val.Type == VType.ObjectRef)
                    Console.Write($"[ObjRef:{val.ObjectId}]");
                else
                    Console.Write(0);
            }
        );

        rt.RegisterFunction(
            "PrintLn",
            (vm) =>
            {
                NovaValue val = vm.Pop();
                if (val.Type == VType.Int)
                    Console.WriteLine(val.AsInt);
                else if (val.Type == VType.Float)
                    Console.WriteLine(val.AsFloat);
                else if (val.Type == VType.Bool)
                    Console.WriteLine(val.AsBool);
                else if (val.Type == VType.String)
                    Console.WriteLine(vm.Strings?.GetString(val.ObjectId) ?? "?");
                else if (val.Type == VType.ObjectRef)
                    Console.WriteLine($"[ObjRef:{val.ObjectId}]");
                else
                    Console.WriteLine(0);
            }
        );

        rt.RegisterFunction(
            "Math_Sqrt",
            (vm) => vm.Push(NovaValue.Float((float)Math.Sqrt(GetDouble(vm.Pop()))))
        );
        rt.RegisterFunction(
            "Math_Abs",
            (vm) => vm.Push(NovaValue.Float((float)Math.Abs(GetDouble(vm.Pop()))))
        );
        rt.RegisterFunction(
            "Math_Sin",
            (vm) => vm.Push(NovaValue.Float((float)Math.Sin(GetDouble(vm.Pop()))))
        );
        rt.RegisterFunction(
            "Math_Cos",
            (vm) => vm.Push(NovaValue.Float((float)Math.Cos(GetDouble(vm.Pop()))))
        );
        rt.RegisterFunction(
            "Math_Pow",
            (vm) =>
            {
                NovaValue exp = vm.Pop();
                NovaValue b = vm.Pop();
                vm.Push(NovaValue.Float((float)Math.Pow(GetDouble(b), GetDouble(exp))));
            }
        );
        rt.RegisterFunction(
            "Math_Min",
            (vm) =>
            {
                NovaValue b = vm.Pop();
                NovaValue a = vm.Pop();
                vm.Push(NovaValue.Float((float)Math.Min(GetDouble(a), GetDouble(b))));
            }
        );
        rt.RegisterFunction(
            "Math_Max",
            (vm) =>
            {
                NovaValue b = vm.Pop();
                NovaValue a = vm.Pop();
                vm.Push(NovaValue.Float((float)Math.Max(GetDouble(a), GetDouble(b))));
            }
        );
        rt.RegisterFunction(
            "Math_Floor",
            (vm) => vm.Push(NovaValue.Float((float)Math.Floor(GetDouble(vm.Pop()))))
        );
        rt.RegisterFunction(
            "Math_Ceil",
            (vm) => vm.Push(NovaValue.Float((float)Math.Ceiling(GetDouble(vm.Pop()))))
        );
        rt.RegisterFunction(
            "Math_Clamp",
            (vm) =>
            {
                NovaValue max = vm.Pop();
                NovaValue min = vm.Pop();
                NovaValue v = vm.Pop();
                vm.Push(
                    NovaValue.Float((float)Math.Clamp(GetDouble(v), GetDouble(min), GetDouble(max)))
                );
            }
        );
        rt.RegisterFunction(
            "Math_Lerp",
            (vm) =>
            {
                NovaValue t = vm.Pop();
                NovaValue b = vm.Pop();
                NovaValue a = vm.Pop();
                double da = GetDouble(a),
                    db = GetDouble(b),
                    dt = GetDouble(t);
                vm.Push(NovaValue.Float((float)(da + (db - da) * dt)));
            }
        );

        rt.RegisterFunction(
            "Len",
            (vm) =>
            {
                NovaValue buf = vm.Pop();
                vm.Push(NovaValue.Int(vm.Heap.GetBufferLength(buf.ObjectId)));
            }
        );

        rt.RegisterFunction(
            "Buffer_Fill",
            (vm) =>
            {
                NovaValue v = vm.Pop();
                NovaValue buf = vm.Pop();
                int len = vm.Heap.GetBufferLength(buf.ObjectId);
                byte* ptr = vm.Heap.GetBuffer() + buf.ObjectId;
                for (int i = 0; i < len; i++)
                    ((NovaValue*)ptr)[i] = v;
                vm.Push(buf);
            }
        );

        rt.RegisterFunction(
            "Buffer_Copy",
            (vm) =>
            {
                NovaValue src = vm.Pop();
                NovaValue dst = vm.Pop();
                int srcLen = vm.Heap.GetBufferLength(src.ObjectId);
                int dstLen = vm.Heap.GetBufferLength(dst.ObjectId);
                int copyLen = Math.Min(srcLen, dstLen);
                byte* srcPtr = vm.Heap.GetBuffer() + src.ObjectId;
                byte* dstPtr = vm.Heap.GetBuffer() + dst.ObjectId;
                Unsafe.CopyBlock(dstPtr, srcPtr, (uint)(copyLen * 8));
                vm.Push(dst);
            }
        );

        rt.RegisterFunction(
            "Buffer_Reverse",
            (vm) =>
            {
                NovaValue buf = vm.Pop();
                int len = vm.Heap.GetBufferLength(buf.ObjectId);
                byte* ptr = vm.Heap.GetBuffer() + buf.ObjectId;
                NovaValue* elements = (NovaValue*)ptr;
                for (int i = 0; i < len / 2; i++)
                {
                    NovaValue tmp = elements[i];
                    elements[i] = elements[len - 1 - i];
                    elements[len - 1 - i] = tmp;
                }
                vm.Push(buf);
            }
        );

        return rt;
    }

    private static void RunFile(string path)
    {
        string source = File.ReadAllText(path);
        NovaScriptRuntime rt = CreateRuntime();
        rt.Execute(source, quiet: false);
    }

    private static void CompileToBinary(string srcPath, string outPath)
    {
        string source = File.ReadAllText(srcPath);
        NovaScriptRuntime rt = CreateRuntime();

        Chunk chunk = new();
        Local[] localsBuffer = new Local[8192];
        SymbolEntry[] symbolEntries = new SymbolEntry[256];
        StructLayout[] structLayouts = new StructLayout[16];
        StructField[] structFields = new StructField[64];
        StringPool stringPool = new();

        Parser parser = new(
            source,
            chunk,
            localsBuffer,
            symbolEntries,
            structLayouts,
            structFields,
            new ReadOnlySpan<string>(rt.GetNativeNames().ToArray()),
            stringPool
        );
        parser.Compile();

        byte[] bytecode = [.. chunk.Code];
        NovaValue[] constants = [.. chunk.Constants];

        using FileStream fs = new(outPath, FileMode.Create);
        using BinaryWriter bw = new(fs);

        bw.Write("NOVA"u8);
        bw.Write(1);

        List<(int byteLen, byte[] utf8)> stringList = [];
        Dictionary<int, int> stringIndexMap = [];
        for (int i = 0; i < constants.Length; i++)
        {
            if (constants[i].Type == VType.String)
            {
                int po = constants[i].ObjectId;
                if (!stringIndexMap.ContainsKey(po))
                {
                    byte[] utf8 = Encoding.UTF8.GetBytes(stringPool.GetString(po));
                    stringIndexMap[po] = stringList.Count;
                    stringList.Add((utf8.Length, utf8));
                }
            }
        }

        bw.Write(stringList.Count);
        foreach ((int len, byte[]? utf8) in stringList)
        {
            bw.Write(len);
            bw.Write(utf8);
        }

        bw.Write(constants.Length);
        foreach (NovaValue cv in constants)
        {
            bw.Write((byte)cv.Type);
            switch (cv.Type)
            {
                case VType.Null:
                    break;
                case VType.Int:
                    bw.Write(cv.AsInt);
                    break;
                case VType.Float:
                    bw.Write(cv.AsFloat);
                    break;
                case VType.Bool:
                    bw.Write(cv.AsBool);
                    break;
                case VType.String:
                    bw.Write(stringIndexMap[cv.ObjectId]);
                    break;
            }
        }

        bw.Write(bytecode.Length);
        bw.Write(bytecode);

        Console.WriteLine(
            $"Compiled '{srcPath}' -> '{outPath}' ({bytecode.Length} bytes bytecode, {constants.Length} constants)"
        );
    }

    private static unsafe void ExecuteBinary(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Error: binary file '{path}' not found.");
            return;
        }

        using FileStream fs = new(path, FileMode.Open);
        using BinaryReader br = new(fs);

        byte[] magic = br.ReadBytes(4);
        if (magic[0] != 'N' || magic[1] != 'O' || magic[2] != 'V' || magic[3] != 'A')
        {
            Console.Error.WriteLine("Error: invalid Nova binary (bad magic).");
            return;
        }
        int version = br.ReadInt32();
        if (version != 1)
        {
            Console.Error.WriteLine($"Error: unsupported binary version {version}.");
            return;
        }

        StringPool stringPool = new();
        int stringCount = br.ReadInt32();
        string[] loadedStrings = new string[stringCount];
        for (int i = 0; i < stringCount; i++)
        {
            int byteLen = br.ReadInt32();
            byte[] utf8 = br.ReadBytes(byteLen);
            loadedStrings[i] = Encoding.UTF8.GetString(utf8);
            fixed (byte* p = utf8)
                stringPool.InternFromBytes(p, byteLen);
        }

        int constCount = br.ReadInt32();
        NovaValue[] constants = new NovaValue[constCount];
        for (int i = 0; i < constCount; i++)
        {
            VType type = (VType)br.ReadByte();
            switch (type)
            {
                case VType.Null:
                    constants[i] = new NovaValue { Type = VType.Null };
                    break;
                case VType.Int:
                    constants[i] = NovaValue.Int(br.ReadInt32());
                    break;
                case VType.Float:
                    constants[i] = NovaValue.Float(br.ReadSingle());
                    break;
                case VType.Bool:
                    constants[i] = NovaValue.Bool(br.ReadBoolean());
                    break;
                case VType.String:
                {
                    int strIdx = br.ReadInt32();
                    string s = loadedStrings[strIdx];
                    int newOff = stringPool.Intern(s.AsSpan());
                    constants[i] = NovaValue.String(newOff);
                    break;
                }
            }
        }

        int bcLen = br.ReadInt32();
        byte[] bytecode = br.ReadBytes(bcLen);

        NovaScriptRuntime rt = CreateRuntime();
        HeapAllocator heap = new();
        VM vm = new(bytecode, constants) { Heap = heap, Strings = stringPool };
        for (int i = 0; i < rt.GetNativeCount(); i++)
            vm.RegisterNative(i, rt.GetNativeCallback(i)!);

        vm.Execute();
    }

    private static void RunBenchmark()
    {
        NovaScriptRuntime scriptRuntime = CreateRuntime();

        Console.WriteLine("Generating Test Script File content in memory...");
        StringBuilder script = new();

        script.AppendLine(
            @"
                struct Vector3D { var x; var y; var z; }
                func CoreHeavyAlgorithm(multiplier, modifier) {
                    var trigRatio = Math_Sin(multiplier) * Math_Cos(modifier);
                    var absolute = Math_Abs(trigRatio);
                    var v = Vector3D(); v.x = absolute;
                    v.y = Math_Sqrt(v.x) + Math_Pow(multiplier, 2.0);
                    v.z = v.y; return v.z;
                }
                func level1() { return 1; }
                func level2() { return level1() + 1; }
                func level3() { return level2() + 1; }
                PrintLn('--- Struct Literal ---');
                var sl_test = Vector3D { x = 10, y = 20, z = 30 };
                PrintLn(sl_test.x); PrintLn(sl_test.y); PrintLn(sl_test.z);
                var sl_empty = Vector3D {};
                PrintLn(sl_empty.x); PrintLn(sl_empty.y); PrintLn(sl_empty.z);
                var sl_partial = Vector3D { z = 99 };
                PrintLn(sl_partial.x); PrintLn(sl_partial.y); PrintLn(sl_partial.z);
            "
        );

        script.AppendLine(
            @"
                PrintLn('--- For Break ---');
                for (var fb = 0; fb < 5; fb = fb + 1) { if (fb >= 3) { break; } PrintLn(fb); }
                PrintLn('--- For Continue ---');
                for (var fc = 0; fc < 5; fc = fc + 1) { if (fc == 2) { continue; } PrintLn(fc); }
                PrintLn('--- For Loop Test ---');
                for (var i = 0; i < 10; i = i + 1) { PrintLn(i); }
                PrintLn('--- For (;;) ---');
                var fe_i = 0; for (;;) { if (fe_i >= 3) { break; } PrintLn(fe_i); fe_i = fe_i + 1; }
                PrintLn('--- No Init ---');
                var j = 0; for (; j < 5; j = j + 1) { PrintLn(j); }
                PrintLn('--- No Increment ---');
                for (var k = 0; k < 3;) { PrintLn(k); k = k + 1; }
                PrintLn('--- No Init No Inc ---');
                var z = 0; for (; z < 3;) { PrintLn(z); z = z + 1; }
                PrintLn('--- For Loop Complete ---');
            "
        );

        script.AppendLine(
            @"
                PrintLn('--- Buffer Test ---');
                var buf = Buffer(5); buf[0] = 10; buf[1] = 20; buf[2] = 30;
                PrintLn(buf[0]); PrintLn(buf[1]); PrintLn(buf[2]);
                PrintLn('--- Vertex Data Processing ---');
                var vertices = Buffer(12); var idx = 0;
                while (idx < 12) { vertices[idx] = idx * 10; idx = idx + 1; }
                idx = 0; while (idx < 12) { PrintLn(vertices[idx]); idx = idx + 1; }
                PrintLn('--- Buffer In Expression ---');
                PrintLn(buf[0] + buf[1] + buf[2]);
                PrintLn('--- Compound Assignment Test ---');
                var ca = 10; ca = ca + 5; PrintLn(ca); ca += 5; PrintLn(ca);
                ca -= 3; PrintLn(ca); ca *= 2; PrintLn(ca); ca /= 2; PrintLn(ca);
                var caBuf = Buffer(3); caBuf[0] = 10; caBuf[1] = 20; caBuf[2] = 30;
                caBuf[0] += 5; PrintLn(caBuf[0]); caBuf[1] -= 5; PrintLn(caBuf[1]);
                caBuf[2] *= 2; PrintLn(caBuf[2]); caBuf[2] /= 3; PrintLn(caBuf[2]);
                PrintLn('--- Len Test ---');
                PrintLn(Len(buf)); PrintLn(Len(caBuf)); PrintLn(Len(vertices));
                PrintLn('--- Buffer Literal ---');
                var bl = [10, 20, 30];
                PrintLn(Len(bl)); PrintLn(bl[0]); PrintLn(bl[1]); PrintLn(bl[2]);
                PrintLn('--- Nested Buffers ---');
                var nb = [[1, 2], [3, 4]];
                PrintLn(Len(nb)); PrintLn(nb[0][0]); PrintLn(nb[0][1]); PrintLn(nb[1][0]); PrintLn(nb[1][1]);
                PrintLn('--- String Concat ---');
                PrintLn('Hello ' + 'World'); var greeting = 'Hi' + ' ' + 'There'; PrintLn(greeting);
                PrintLn('--- Escape Sequences ---');
                PrintLn('line1\nline2'); PrintLn('tab\there'); PrintLn('back\\slash');
                PrintLn('--- Nested Break ---');
                var nb2 = 0;
                while (nb2 < 3) { var n_inner = 0;
                    while (n_inner < 5) { if (n_inner >= 2) { break; } PrintLn(n_inner); n_inner = n_inner + 1; }
                    nb2 = nb2 + 1; }
                PrintLn('--- Ternary ---');
                var ta = 5; PrintLn(ta >= 3 ? 100 : 200); PrintLn(ta >= 8 ? 300 : 400);
                PrintLn(ta >= 3 ? 'yes' : 'no'); PrintLn(ta >= 8 ? 'big' : 'small');
                PrintLn('--- Increment/Decrement ---');
                var ic = 0; ic++; PrintLn(ic); ic++; PrintLn(ic); ic--; PrintLn(ic);
                var id = 10; var id2 = id++; PrintLn(id); PrintLn(id2); var ifval = 3.5; ifval++; PrintLn(ifval);
                PrintLn('--- Buffer Slice Test ---');
                var src = Buffer(5); src[0] = 10; src[1] = 20; src[2] = 30; src[3] = 40; src[4] = 50;
                var slice = src[1..4];
                PrintLn(Len(slice)); PrintLn(slice[0]); PrintLn(slice[1]); PrintLn(slice[2]);
                PrintLn('--- Buffer Utilities Test ---');
                var bufA = Buffer(5); bufA[0] = 1; bufA[1] = 2; bufA[2] = 3; bufA[3] = 4; bufA[4] = 5;
                Buffer_Fill(bufA, 99); PrintLn(bufA[0]); PrintLn(bufA[2]);
                PrintLn('--- Slice Assignment Test ---');
                var dstA = Buffer(5); dstA[0..2] = src; PrintLn(dstA[0]); PrintLn(dstA[1]);
                PrintLn('--- If/Else Test ---');
                var score = 75;
                if (score >= 90) { PrintLn('A'); }
                else if (score >= 80) { PrintLn('B'); }
                else if (score >= 70) { PrintLn('C'); }
                else { PrintLn('D'); }
                if (score < 60) { PrintLn('Fail'); }
                PrintLn('Done');
                PrintLn('--- Math Extension Test ---');
                PrintLn(Math_Min(3.0, 7.0)); PrintLn(Math_Max(3.0, 7.0));
                PrintLn(Math_Floor(3.9)); PrintLn(Math_Ceil(3.1));
                PrintLn(Math_Clamp(15.0, 0.0, 10.0)); PrintLn(Math_Lerp(0.0, 10.0, 0.5));
                PrintLn('--- Uninitialized Var Guard ---');
                var uninit_var; uninit_var = 42; PrintLn(uninit_var);
                PrintLn('--- Call Stack Test ---');
                PrintLn(level3());
            "
        );

        script.AppendLine("// --- STRESS MATRIX (2,500 GENERATED EXPRESSIONS) ---");
        for (int i = 0; i < 2500; i++)
            script.AppendLine(
                $"var compileStressVal_{i} = CoreHeavyAlgorithm({i}.0 * 0.001, {i + 1}.5);"
            );
        script.AppendLine("// --- END STRESS MATRIX ---");

        script.AppendLine(
            @"
                PrintLn(88888888);
                var loopCounter = 0; var totalBenchmarkAccumulator = 0.0;
                while (loopCounter < 100000) {
                    totalBenchmarkAccumulator = totalBenchmarkAccumulator + CoreHeavyAlgorithm(loopCounter * 0.0001, 1.25);
                    loopCounter = loopCounter + 1;
                }
                Print('aggregated numerical metric result: ');
                PrintLn(totalBenchmarkAccumulator);
            "
        );

        string finished = script.ToString();
        Console.WriteLine(
            $"Generation complete! created {finished.Split('\n').Length} lines of code.\n"
        );
        scriptRuntime.Execute(finished, quiet: false);
    }
}
