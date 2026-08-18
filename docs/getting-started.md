# Getting Started

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- A terminal / command prompt

## Building from Source

Clone the repository and build the native AOT binary:

```bash
git clone https://github.com/your-username/nova-lang.git
cd nova-lang
dotnet publish -c Release
```

This produces a self-contained native executable in `NovaLang/bin/Release/net10.0/native/`.

## Running a Program

Nova ships with a CLI runner and binary compiler. The entry point is `Program.cs`:

```bash
# Run the built-in benchmark script
dotnet run --project NovaLang

# Compile & run a .nova source file
NovaLang script.nova

# Compile a .nova source file to a binary blob (.novab)
NovaLang --compile script.nova script.novab

# Run a pre-compiled binary blob
NovaLang --run script.novab
```

## Writing Your First Script

Create a file called `hello.nova`:

```
func Greet(name) {
    PrintLn("Hello, " + name + "!");
}

Greet("World");
```

## Embedding in C\#

Nova can be used as an embedded scripting engine:

```csharp
using NovaLang.Runtime;

// Execute source code directly
int result = NovaScriptRuntime.Execute(@"
    var x = 10;
    var y = 20;
    x + y
");

// result == 30
```

The `Execute` method compiles the source, creates a VM, runs it, and returns the top of the evaluation stack as an `int`.

## Project Structure

```
NovaLang/
├── Program.cs                  # Entry point, native function registration
├── Compiler/
│   ├── Lexer.cs                # Tokenizer
│   ├── Parser.cs               # Pratt parser (single-pass)
│   ├── Chunk.cs                # Bytecode container
│   └── GlobalSymbolTable.cs    # Function name resolution
└── Runtime/
    ├── VM.cs                   # Virtual machine
    ├── NovaValue.cs            # 8-byte union value type
    ├── HeapAllocator.cs        # Unmanaged bump allocator
    ├── StringPool.cs           # Zero-allocation string interning
    └── NovaScriptRuntime.cs    # Orchestrator
```

## Editor Setup

Nova source files use the `.nova` extension. There is no official syntax highlighting yet, but the language syntax is similar to JavaScript/C, so setting your editor to C or JavaScript mode provides reasonable highlighting.

A VS Code extension is planned for the roadmap.
