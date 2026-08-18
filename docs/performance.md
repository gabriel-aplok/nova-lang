# Performance

Nova is designed for zero-allocation, low-latency execution. All compilation and VM execution bypass the .NET garbage collector entirely.

## Benchmarks

From the canonical 100,000-iteration game scripting benchmark:

| Metric | Value |
|--------|-------|
| Compile time | ~140 ms |
| Memory used | ~873 KB |
| Bytecode stream | ~30 KB |
| Constants pool | ~5,025 entries |
| VM hot loop (100k iterations) | ~65 ms |

The benchmark computes `Sin`, `Cos`, `Sqrt`, and `Pow` per iteration with struct field access and buffer operations.

## Why Zero Allocation?

Traditional scripting engines allocate objects on the managed heap, triggering garbage collection pauses. Nova avoids this by:

1. **Compiler** -- Uses `ref struct` components (`Lexer`, `Parser`, `Chunk`, `GlobalSymbolTable`) that live entirely on the stack. No heap allocations during compilation.

2. **VM** -- Evaluation stack and call stack are pre-allocated arrays. Local variables are stack slots, not heap objects.

3. **Heap** -- Structs and buffers use `NativeMemory.Alloc` (unmanaged memory), not the .NET GC heap. A bump allocator with doubling resize avoids per-object allocation overhead.

4. **Strings** -- Interned in a `NativeMemory`-backed pool with deduplication. String concatenation uses `stackalloc` buffers.

## Memory Layout

### NovaValue (8 bytes)

```
Offset 0: ValueType (1 byte)
Offset 4: Data (4 bytes, union: int, float, bool, objectId)
```

### Heap Objects

**Struct:** `[field0: 8 bytes][field1: 8 bytes]...[fieldN: 8 bytes]`
**Buffer:** `[length: 4 bytes][padding: 4 bytes][element0: 8 bytes]...[elementN: 8 bytes]`

### String Pool

```
[length: 4 bytes][UTF-8 bytes...]
```

Strings are deduplicated by linear scan. The pool grows by doubling.

## Optimization Techniques

- **Local slot 0-3 fast path** -- `LoadLocal_0` through `LoadLocal_3` and `StoreLocal_0` through `StoreLocal_3` are single-byte opcodes with no index operand.
- **Constant pool hybrid encoding** -- `PushConst` (2 bytes) for indices 0-255, `PushConst16` (3 bytes) for larger.
- **Jump threading** -- `PatchJump` follows chains up to 10 hops to find the final target.
- **Immediate encoding** -- Small integer constants use `PushConstInt8` (2 bytes) instead of a constant pool lookup.
- **Aggressive inlining** -- All VM handlers are marked `AggressiveInlining`.
- **Register-resident context** -- `VMContext` is a struct designed to stay in CPU registers during the dispatch loop.

## Native AOT

Publishing with Native AOT compiles the entire runtime to native machine code:

```bash
dotnet publish -c Release
```

This eliminates JIT compilation overhead and produces a self-contained executable suitable for embedded runtimes and game engines.

## Roadmap Performance Items

- Call stack depth safety (guard against runaway recursion)
- Optional bounds checking (debug mode)
- Thread-safe VM instances
- Constant pool frequency profiling for L1 cache locality
