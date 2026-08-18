# Nova Language

**A zero-allocation, single-pass compiled scripting language for real-time applications.**

Nova is designed for game scripting, low-latency embedded runtimes, and performance-critical automation. It compiles directly to bytecode and executes on a stack-frame virtual machine with indirect-threaded dispatch -- all without touching the .NET garbage collector.

## Key Features

- **Zero GC pressure** -- All compilation and execution uses stack-allocated `ref struct` components and raw native memory via `NativeMemory.Alloc`.
- **Single-pass compilation** -- No AST. The Pratt parser emits bytecode directly as it reads source, keeping compile times under ~150ms for typical scripts.
- **6 value types** -- `Int`, `Float`, `Bool`, `String`, `ObjectRef` (structs), and `Null`, packed into an 8-byte union.
- **Structs** -- User-defined heap-allocated types with named fields resolved at compile time.
- **Buffers** -- Fixed-size heap arrays with slicing, bulk copy, and in-place operations.
- **17 native functions** -- Math, string, and buffer operations built into the VM.
- **C-style control flow** -- `if`/`else`, `while`, `for`, `for-each`, `break`, `continue`.
- **Exceptions** -- `try`/`catch`/`throw` with catch-variable binding and cross-frame unwinding.
- **Native AOT ready** -- Targets .NET 10.0 with `PublishAot` for direct machine code compilation.

## Quick Example

```
struct Vector3D {
    var x;
    var y;
    var z;
}

func Distance(v) {
    return Math_Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
}

var pos = Vector3D();
pos.x = 3.0;
pos.y = 4.0;
pos.z = 0.0;

PrintLn(Distance(pos));  // 5
```

## Documentation

| Document                              | Description                                    |
| ------------------------------------- | ---------------------------------------------- |
| [Getting Started](getting-started.md) | Build, install, and run your first program     |
| [Syntax Reference](syntax.md)         | Keywords, operators, comments, and grammar     |
| [Type System](types.md)               | Value types, literals, and type rules          |
| [Variables](variables.md)             | Declaration, scoping, and assignment           |
| [Functions](functions.md)             | Function declaration, calls, and return values |
| [Structs](structs.md)                 | User-defined types with named fields           |
| [Buffers](buffers.md)                 | Heap arrays, slicing, and bulk operations      |
| [Control Flow](control-flow.md)       | Conditionals, loops, and jump statements       |
| [Exceptions](exceptions.md)           | try/catch/throw error handling                 |
| [Expressions](expressions.md)         | Operators, precedence, and evaluation          |
| [Standard Library](stdlib.md)         | Built-in native functions                      |
| [Bytecode Reference](opcodes.md)      | Virtual machine opcodes                        |
| [Performance](performance.md)         | Benchmarks and memory characteristics          |

## License

zlib License -- see [LICENSE.txt](../LICENSE.txt) for details.
