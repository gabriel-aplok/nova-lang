# Bytecode Reference

This document describes the VM opcodes for advanced users and contributors. Most users do not need this.

## Opcodes

| OpCode | Size | Description |
|--------|------|-------------|
| `Return` | 1 | Halt the VM |
| `PushConst` | 2 | Push constant pool entry (1-byte index) |
| `PushConst16` | 3 | Push constant pool entry (2-byte index) |
| `PushConstInt_0` | 1 | Push `Int(0)` (zero-operand) |
| `PushConstInt_1` | 1 | Push `Int(1)` (zero-operand) |
| `PushConstInt8` | 2 | Push immediate `Int(sbyte)` |
| `Add` | 1 | Pop two, push sum (int+int, float+float, or string concat) |
| `Sub` | 1 | Pop two, push difference |
| `Mul` | 1 | Pop two, push product |
| `Div` | 1 | Pop two, push quotient (div-by-zero returns 0) |
| `Equal` | 1 | Pop two, push `Bool(a == b)` |
| `NotEqual` | 1 | Pop two, push `Bool(a != b)` |
| `Less` | 1 | Pop two, push `Bool(a < b)` |
| `Greater` | 1 | Pop two, push `Bool(a > b)` |
| `LessEqual` | 1 | Pop two, push `Bool(a <= b)` |
| `GreaterEqual` | 1 | Pop two, push `Bool(a >= b)` |
| `JumpIfFalse` | 2 | Pop top; if false, jump forward by offset |
| `Jump` | 2 | Unconditional jump (signed sbyte offset) |
| `LoadLocal` | 2 | Push local variable by index |
| `StoreLocal` | 2 | Pop and store to local by index |
| `LoadLocal_0` | 1 | Push local 0 (optimized) |
| `LoadLocal_1` | 1 | Push local 1 (optimized) |
| `LoadLocal_2` | 1 | Push local 2 (optimized) |
| `LoadLocal_3` | 1 | Push local 3 (optimized) |
| `StoreLocal_0` | 1 | Store to local 0 (optimized) |
| `StoreLocal_1` | 1 | Store to local 1 (optimized) |
| `StoreLocal_2` | 1 | Store to local 2 (optimized) |
| `StoreLocal_3` | 1 | Store to local 3 (optimized) |
| `CallNative` | 2 | Call native function by index |
| `Call` | 3 | Call user function (2-byte address + 1-byte arg count) |
| `RetUserFunc` | 1 | Return from user function (restores frame) |
| `Pop` | 1 | Discard top of stack |
| `Dup` | 1 | Duplicate top of stack |
| `Dup2` | 2 | Duplicate top two values |
| `StructNew` | 2 | Allocate struct (1-byte field count) |
| `StructGet` | 2 | Read struct field (handle on stack, 1-byte field index) |
| `StructSet` | 2 | Write struct field (value + handle on stack, 1-byte field index) |
| `BufferNew` | 1 | Allocate buffer (count on stack) |
| `BufferGet` | 1 | Read buffer element (index + handle on stack) |
| `BufferSet` | 1 | Write buffer element (value + index + handle on stack) |
| `BufferSlice` | 1 | Slice buffer (end + start + handle on stack) |
| `BufferSliceAssign` | 1 | Bulk copy into buffer slice |

## Constant Pool

Constants are stored in a deduplicated pool. The compiler uses two encoding sizes:

- **`PushConst`** (2 bytes): opcode + 1-byte index (for constants 0-255)
- **`PushConst16`** (3 bytes): opcode + 2-byte index (for constants 256+)

Constants are type-aware: two `Float` values with the same bit pattern are considered duplicates, as are two `Int` values with the same value.

## Jump Threading

The `PatchJump` implementation follows jump chains (up to 10 hops) to short-circuit to the final target. This reduces branch overhead for deeply nested if/else chains.

## VM Dispatch

The VM uses indirect-threaded dispatch via a function pointer jump table:

```
dispatch[opcode](vm, context)
```

Each handler is marked `AggressiveInlining` and operates on raw pointers for maximum throughput.
