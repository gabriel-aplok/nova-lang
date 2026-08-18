# nova language

a fast, **zero-alloc**, single-pass compiler and stack-frame virtual machine tailored for real-time game scripting, vertex data processing, and low-latency embedded runtimes - built in C# with **native AOT** support.

nova bypasses the .NET GC entirely during compilation and execution by using stack-allocated `ref struct` components, `ReadOnlySpan<char>` streaming, and raw native pointer memory backed by `NativeMemory.Alloc`.

---

## Features

### Game-Dev Ready

- **struct types** - user-defined value-like types (`Vector3D { var x; var y; var z; }`) with field get/set, backed by a custom unmanaged heap allocator.
- **buffer/array** - `Buffer(count)` creates a raw heap buffer; `buf[index]` get/set for vertex data, particle arrays, transform pools.
- **`Len(buf)`** - bounds-check your buffers before iterating.
- **`buf[start..end]` slice** - copy a sub-range into a new buffer for batch processing.
- **compound assignment** - `+=`, `-=`, `*=`, `/=` for concise vector math and accumulator updates.
- **built-in math** - `Math_Sin`, `Math_Cos`, `Math_Sqrt`, `Math_Abs`, `Math_Pow` as native calls with zero GC pressure.
- **C-style for/while** - `for (var i = 0; i < count; i = i + 1)` loop syntax with all four patterns (no-init, no-inc, etc.).
- **for-each** - `for (var v in buffer)` iteration with hidden locals.
- **int/float distinction** - integer literals (`42`) use exact int arithmetic; float literals (`3.14`) promote to float. Mixed-type operations handled automatically.
- **short-circuit logic** - `&&` and `||` with proper branch elimination.
- **ternary operator** - `condition ? true_val : false_val`.
- **`++` / `--`** - postfix increment and decrement operators.
- **struct literals** - `Vector3D { x = 10, y = 20, z = 30 }` inline field initialization.
- **buffer literals** - `[10, 20, 30]` heap-allocated buffer creation (including nested `[[1,2],[3,4]]`).
- **bounds checking** - runtime range validation on all `buf[index]` get/set/slice ops.
- **string concat** - `"hello " + "world"` with string pool interning.
- **comments** - `// line` and `/* block */` comments.
- **escape sequences** - `\n`, `\t`, `\\`, `\"`, `\'` in strings.
- **call stack guard** - configurable max nested calls (default 4096) with runtime overflow error.
- **uninit var guard** - `var x;` sets Null; reading before first assignment throws.

### Performance

- compiles ~2600 lines in ~170ms with **zero managed heap allocations** during lexing/parsing.
- indirect-threaded dispatch VM with function-pointer jump table (no switch‑case pipeline flushes).
- dedicated 0‑3 local load/store opcodes eliminate operand bytes for the hottest variables.
- `PushConst` (1‑byte index) + `PushConst16` (2‑byte index) hybrid encoding keeps hot constants in L1 cache.
- interned constant pool with type-aware deduplication (bit‑exact float comparison).
- unmanaged heap (`NativeMemory.AllocZeroed`) with bump allocation - no GC collections, no pointer invalidation.

### Safety & Correctness

- **heap never moves** - `NativeMemory.Alloc` backing replaces `byte[]`+`fixed` pattern, eliminating undefined behavior from GC relocation. Heap base re-synced after every `Alloc`.
- **string pool is stable** - same `NativeMemory` pattern keeps interned string pointers valid across concatenation and resize.
- **constant pool overflow fixed** - `(byte)constantIndex` truncation replaced with `PushConst16` fallback; all 5014+ constants load the correct value.
- **fraction‑safe dedup** - `AddConstant` uses `AsFloat` comparison for float constants, correctly handling `-0.0f`/`0.0f`.
- **culture‑invariant parsing** - `int.Parse` / `float.Parse` use `InvariantCulture`; no locale‑dependent decimal separator bugs.

---

## Code Example (Game Scripting)

```c
// define a vertex struct
struct Vector3D {
    var x;
    var y;
    var z;
}

// high-load math function
func CoreHeavyAlgorithm(multiplier, modifier) {
    var trigRatio = Math_Sin(multiplier) * Math_Cos(modifier);
    var absolute = Math_Abs(trigRatio);
    var v = Vector3D();
    v.x = absolute;
    v.y = Math_Sqrt(v.x) + Math_Pow(multiplier, 2.0);
    v.z = v.y;
    return v.z;
}

// buffer operations with slice and compound assignment
var vertices = Buffer(100);
for (var i = 0; i < Len(vertices); i = i + 1) {
    vertices[i] = i * 10;
}

var batch = vertices[0..50];  // slice copy
batch[0] += 5;                // compound assignment

// 100k iteration benchmark loop
var acc = 0.0;
var counter = 0;
while (counter < 100000) {
    acc = acc + CoreHeavyAlgorithm(counter * 0.0001, 1.25);
    counter = counter + 1;
}
```

---

## Performance (2,600+ Line Benchmark)

```text
Compile Time    : ~140 ms
Memory Used     : ~873 KB
Bytecode Stream : ~30 KB
Constants Pool  : ~5025 cached

VM Hot Loop (100k iterations with Sin/Cos/Sqrt/Pow): ~65 ms
```

All 2600+ lines compile with **zero GC allocations** on the managed heap. The runtime loop executes 100,000 iterations of complex math (Sin, Cos, Abs, Sqrt, Pow) with user function calls, struct construction, and buffer operations - all under 70ms.

---

## Quick Start

### Prerequisites

- .NET 10.0 SDK

### Build Native AOT

```bash
dotnet publish -c Release
```

### Run

```bash
NovaLang.exe                   # run built-in benchmark
NovaLang.exe script.nova       # compile & run .nova script
NovaLang.exe --compile src.nova out.novab  # compile to binary
NovaLang.exe --run out.novab              # run compiled binary
```

---

## Development Roadmap

### Game-Dev Features

- [x] Struct types with field get/set (`Vector3D { var x; var y; var z; }`)
- [x] Buffer heap arrays with `buf[index]` get/set for vertex/particle data
- [x] Struct constructor syntax (`Vector3D()`)
- [x] Compound assignment (`+=`, `-=`, `*=`, `/=`) for concise vector math
- [x] `Len(buf)` native function for bounds-safe iteration
- [x] `buf[start..end]` slice copy for batch sub-range processing
- [x] Native math library: `Sin`, `Cos`, `Sqrt`, `Abs`, `Pow`
- [x] Int/float literal distinction (exact integer math where possible)
- [x] **Slice assignment:** `buf[start..end] = other` bulk copy via `BufferSliceAssign` opcode
- [x] **More math:** `Min`, `Max`, `Floor`, `Ceil`, `Clamp`, `Lerp`
- [x] **Buffer utilities:** `Buffer_Fill(val)`, `Buffer_Copy(src)`, `Buffer_Reverse()` native functions
- [x] **For-each iteration:** `for (var v in buffer)`
- [x] **Buffer literals:** `[1, 2, 3]` syntax
- [x] **Nested buffer literals:** `[[1, 2], [3, 4]]` buffers of buffers
- [x] **Ternary operator:** `condition ? val1 : val2`
- [x] **Comments:** `// line` and `/* block */` support
- [x] **Increment/Decrement:** `i++` and `i--` operators
- [x] **Struct literals:** `Vector3D { x = 10, y = 20, z = 30 }`
- [x] **String concatenation:** `"hello " + "world"`

### Compiler & Parser

- [x] Pratt parser with prefix/infix dispatch (no AST, single-pass)
- [x] Zero heap allocation during lexing/parsing (`ref struct`, `Span`)
- [x] Native comparison operators (`<`, `>`, `<=`, `>=`, `!=`)
- [x] Short-circuit logical operators (`&&`, `||`)
- [x] Dedicated 0-3 local load/store opcodes for hot variables
- [x] Global symbol table (compile-time hashmap)
- [x] Compound assignment tokenization + three-site emission
- [x] Range operator (`..`) with number-literal disambiguation
- [x] **If/elseif/else chains** with recursive `else if` support
- [x] **String escape sequences** (`\n`, `\t`, `\\`, `\"`, `\'`, `\r`, `\0`)
- [x] **Bounds checking** — debug-mode `buf[index]` range validation
- [x] **Call stack depth safety** — configurable guard against runaway recursion (default max 4096)
- [x] **Uninitialized variable access guard** — `var x;` sets Null; reading before assignment throws
- [x] **CLI runner** — `NovaLang.exe <file>` compiles and runs .nova scripts
- [x] **Binary compiler** — `--compile <src> <out>` serializes to .novab; `--run <file>` loads and executes
- [x] **try/catch/throw** — exception handling with catch variable binding and cross-frame unwinding
- [ ] **Multi-file includes** / module system

### Virtual Machine

- [x] Indirect-threaded dispatch (function pointer jump table)
- [x] Unmanaged bump allocator heap (`NativeMemory.AllocZeroed`)
- [x] Zero-allocation string interning with stable pointer
- [x] String concatenation via stackalloc + pool intern
- [x] `Dup` / `Dup2` opcodes for compound assignment
- [x] Hybrid `PushConst` (1-byte) / `PushConst16` (2-byte) encoding
- [x] User function call/return with relative frame save/restore
- [x] Native function dispatch via direct delegate table
- [ ] **Thread-safe VM instance** — isolated `VMContext` per thread

### Memory & Performance

- [x] Heap resize via `NativeMemory.Alloc`+`CopyBlock`+`Free`
- [x] `ctx->HeapBase` re-sync after every `Alloc`
- [x] String pool same stable-pointer pattern as heap
- [x] Constant dedup with type-aware comparison (float via `AsFloat`)
- [x] `Chunk.ReorderConstantsByFrequency()` available for L1 locality
- [x] **Small-value immediate encoding** — `PushConstInt_0/1` (zero-operand) + `PushConstInt8` (sbyte operand)
- [ ] **Constant pool frequency profiling** — auto reorder hot constants
- [x] **Jump threading** — `PatchJump` follows unconditional `Jump` chains, short-circuits to final target

### Safety & Correctness

- [x] Heap dangling-pointer elimination (`NativeMemory` replaces `byte[]`+`fixed`)
- [x] Constant pool index overflow fix (`PushConst16` for indices > 255)
- [x] Culture-invariant number parsing (`InvariantCulture`)
- [x] Mixed-type VM ops (all handlers check both operands)
- [x] Float-safe constant dedup (`AsFloat` instead of bit-level `AsInt`)
- [x] **Division-by-zero guard** — integer `/ 0` returns `Int(0)` gracefully

### Nice to Have

- [ ] VS Code syntax highlighting extension
- [ ] Hot-reload REPL for live script iteration
