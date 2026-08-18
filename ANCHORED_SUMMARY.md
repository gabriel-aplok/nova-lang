## Goal

- Build a zero-allocation, single-pass compiled scripting language with game-dev features (structs, buffers, math, slices, compound assignment, for-each, if/elseif/else chains) and a fast indirect-threaded VM.

## Constraints & Preferences

- Zero-allocation, no-AST, single-pass Pratt parser emitting bytecode directly.
- Raw memory VM loop with indirect-threaded dispatch (function pointer jump table).
- All heap/string-pool memory must be safe from GC relocation (use `NativeMemory.Alloc`).
- Game-dev focus: structs, heap-allocated buffers, math intrinsics, slice/range syntax, compound assignment, for-each iteration.

## Progress

### Done

- **All critical safety bugs fixed:** heap/string-pool dangling pointer (→ `NativeMemory.Alloc`), constant pool overflow (→ `PushConst16` hybrid), culture-invariant parsing, float-safe constant dedup, mixed-type VM ops (via `ToFloat`).
- **Game-dev features:** struct types with field get/set (`Vector3D { var x; var y; var z; }`), buffer indexing (`Buffer(count)`, `buf[index]`), `Len(buf)`, `buf[start..end]` slice + `buf[start..end] = other` slice assignment, compound assignment (`+=`, `-=`, `*=`, `/=`), native math library (`Math_Sin`, `Math_Cos`, `Math_Sqrt`, `Math_Abs`, `Math_Pow`, `Math_Min`, `Math_Max`, `Math_Floor`, `Math_Ceil`, `Math_Clamp`, `Math_Lerp`), buffer utilities (`Buffer_Fill`, `Buffer_Copy`, `Buffer_Reverse`).
- **Control flow:** C-style `for` loops (all four patterns), short-circuit `&&`/`||`, `while` loops, `if`/`else`/`else if` chains (recursive branching via `IfStatement`), `for (var v in buffer)` for-each iteration (expands to while loop with hidden locals for buf/index/len).
- **Performance optimizations:** `PushConst` (1-byte index) + `PushConst16` (2-byte fallback), `LoadLocal_0–3` / `StoreLocal_0–3` dedicated opcodes, `PushConstInt_0` / `PushConstInt_1` (zero-operand) + `PushConstInt8` (sbyte operand) for small-value immediate encoding, jump threading in `PatchJump` (follows `Jump` chains up to 10 links, short-circuits to final target).
- **Safety:** division-by-zero guard (integer `/0` returns `Int(0)` instead of crashing).
- **Stack/slot overlap bug fixed:** Root cause: `VarDeclaration` with `StoreLocal` popped the stack, making `StackTop < _localCount`. Subsequent code wrote to `Stack[StackTop]` which overlapped low-numbered local slots. The condition `for (; j < N; …)` after `var j = 0` was broken — the condition's `PushConstInt8` overwrote j's slot. Fix: emit `LoadLocal` after each `StoreLocal` to restore `StackTop >= _localCount`. Applied to:
  - `VarDeclaration` (user variables)
  - `ForStatement` var init (for-loop variables)
  - `ForEachStatement` hidden locals (buf, idx, len, loop var)
- **For-each buffer-handle overwrite bug fixed:** `ForEachStatement`'s `StoreLocal bufSlot` left `StackTop < _localCount`, then `PushConstInt_0` (idx init) wrote to `Stack[bufSlot]`, destroying the buffer handle. `Len()` received Int(0) instead of the buffer, returning length 0 and skipping the loop body. Now fixed by LoadLocal after each StoreLocal.
- **Nested for loops work correctly:** For-loop var init emits StoreLocal+LoadLocal, ensuring the loop variable value goes to the correct slot regardless of prior variable declarations.
- **`break`/`continue` support added:** Tokens `break` and `continue` recognized by lexer (40 token types). Parsed in `Statement()`, each emits a `Jump` placeholder operand whose offset is recorded in `_loopBreakPatches` / `_loopContinuePatches`. Each loop method (`WhileStatement`, `ForStatement`, `ForEachStatement`) saves/restores outer loop state for nesting and patches placeholders when the loop finishes. Loop state saved/restored via four fields (`_loopBreakPatches`, `_loopContinuePatches`, `_loopExitTarget`, `_inLoop`). Works in all loop types: `while`, `for`, `for-each`.
- **For-loop continue target fixed:** The increment code is moved from between condition and body to after the body. Continue's Jump target was patched to `_chunk.Code.Count` (after the increment), but should target the start of the increment (`Code.Count - incLen`). Now correctly targets the increment start so `continue` re-executes the increment before the backwards Jump.
- **For-loop break operand offset fix:** When the increment code is removed from its original position (via `RemoveRange`), all break/continue Jump operands that were at positions >= `incStart` shift by `-incLen`. These offsets are now corrected after each `RemoveRange` so patches write to the right positions.
- **`HandleGreaterEqual` bug fixed:** The else-branch (line 500 of `VM.cs`) used `a.AsInt >= b.AsInt` instead of `ToFloat(a) >= ToFloat(b)`. Now correctly uses `ToFloat` for non-Int or mixed-type comparisons.
- **`StoreLocalDup` opcodes added:** `StoreLocalDup`, `StoreLocalDup_0/1/2/3` — copies the stack top to a local slot without popping, maintaining `StackTop >= _localCount` naturally. Replaces `StoreLocal`+`LoadLocal` pairs (2-4 bytes) with a single opcode (1-2 bytes), saving ~5000 bytes (12% of bytecode) and ~24% runtime.
- **`for (;;)` infinite loop support:** Empty init/condition/increment clauses all handled correctly. `for (;;)` with `break` works (prints 0,1,2).
- **String escape sequences:** `LexString` handles `\` by skipping the next character, preventing early string termination. Parser processes `\n`/`\t`/`\r`/`\\`/`\"`/`\'`/`\0` using `stackalloc` buffer when `\` is present. Intern's `str` parameter marked `scoped` to allow stackalloc span passing.
- **Call stack depth guard:** `HandleCall` checks `CallStackTop >= _callStackCapacity` before pushing, throwing "Runtime Error: Call stack depth exceeded (max N)." Also guards value-stack overflow (`StackTop + 8 > _valueStackCapacity` → "Value stack overflow (max M slots)"). Both capacities configurable via VM ctor (defaults 65536 value slots / 4096 frames). Prevents runaway recursion and stack overflow.
- **Uninitialized variable access guard:** `VarDeclaration` no-initializer emits `PushNone` (1-byte opcode) instead of `PushConstInt_0`. `PushNone` pushes `ValueType.Null`. All 5 `LoadLocal` handlers check for `Null` and throw "Runtime Error: Cannot read uninitialized variable." Assignment (`=`) via `StoreLocal` overwrites the slot, making subsequent reads safe. Struct field reads (`StructGet`) are NOT guarded (zeroed memory is a valid default).
- **Nested buffer literals:** `[[1, 2], [3, 4]]` works via recursive `ParsePrefixFn(LeftBracket)` — inner `[...]` creates sub-buffers via `BufferFromStack`, outer `[...]` collects handles into a parent buffer. No special handling needed.
- **Buffer bounds checking:** `BufCount()` helper reads element count from heap header at `userHandle - 8`. Added bounds validation to `HandleBufferGet`, `HandleBufferSet`, `HandleBufferSlice`, `HandleBufferSliceAssign`. All throw descriptive errors on OOB access.
- **CLI script runner + binary compiler:** `Program.cs` refactored into dispatcher with 4 modes: benchmark (default), file runner (`NovaLang.exe <file>`), binary compiler (`--compile <src> <out>`), binary runner (`--run <file>`). Binary format: `NOVA` magic + version 1 + string pool (UTF-8 bytes) + constants (type-tagged with inline string data) + raw bytecode. Binary loading reconstructs VM state (strings re-interned, constants remapped).
- **Recursion + forward references:** `ParseCallArguments` no longer throws on an unknown function name; it emits a `Call` with placeholder address `0` and records a fixup (code pos + name index) into `_funcFixupPositions/Start/Length`. `Compile()` calls `ResolveForwardCalls()` after all declarations, patching each placeholder to the now-known `Entry.Address` (or erroring for genuinely undefined names / native-name collisions). Fixup name offsets are captured before arg parsing (it overwrites `_lastIdentifier`). Verified: `Fact(5)=120`, `Fib(10)=55`, mutual recursion `Even/Odd`, forward ref `later()`, `deep(n)` recursion now hits call-stack guard (256) instead of a compile-time failure.

### Blocked

- (none)

## Key Decisions

- **Heap safety**: `NativeMemory.Alloc` over `byte[]`+`fixed` eliminates GC relocation risk. Re-sync `ctx->HeapBase` after every `Alloc`.
- **Constant index hybrid**: Two opcode sizes (`PushConst` 1-byte, `PushConst16` 2-byte) avoids paying cache-miss penalty for large constant pools in the common case.
- **Small-value immediate encoding**: `PushConstInt_0`/`PushConstInt_1` (zero-operand) and `PushConstInt8` (1-byte sbyte operand) eliminate constant pool entries for -128..127, saving ~276 KB memory in the benchmark.
- **For-each expansion**: `for (var v in buffer)` compiles to a while loop with 3 hidden locals (buf, idx, len) and `Len()` called once before the loop. Matching the existing convention, body blocks don't increment `_scopeDepth`.
- **If/elseif/else chains**: Recursive — after `else`, if the next token is `If`, calls `IfStatement()` recursively, composing proper jump chaining.
- **Jump threading**: Applied at patch time in `PatchJump` — follows `Jump` → `Jump` chains (up to 10 hops) and rewrites offset to final target. Only forward `Jump` opcodes (≤127 bytes) are threaded; `JumpIfFalse` is never followed.
- **Buffer utilities**: Implemented as native functions (`Buffer_Fill(val)`, `Buffer_Copy(src)`, `Buffer_Reverse()`) rather than method-call syntax, consistent with existing `Len()` / `Math_*` pattern.
- **Stack/slot separation via `StoreLocalDup`**: Variable declarations use `StoreLocalDup` (store without popping) instead of `StoreLocal+LoadLocal`. This copies the stack top to the local slot while keeping `StackTop` unchanged, naturally maintaining `StackTop >= _localCount` without extra opcodes.
- **Benchmark perf impact:** Bytecode reduced from ~41KB to ~36KB (12% savings), runtime from ~90ms to ~68ms (24% faster) with `StoreLocalDup` replacing `StoreLocal+LoadLocal` pairs.
- **Ternary operator:** `condition ? true_val : false_val` via infix handler in `ParseInfixFn` with `JumpIfFalse`/`Jump` patching. Precedence 2 (same as `&&`). Works with any types.
- **Line/block comments:** Lexer recognizes `//` (skip to EOL) and `/* */` (with proper `*/` matching, line counting, and unterminated error).
- **`++`/`--` operators:** Postfix increment/decrement with correct semantics (returns original value, stores new value). Works for `int` and `float`. Dedicated `Inc`/`Dec` 2-byte opcodes.
- **Struct literal syntax:** `Vector3D { x = 10, y = 20, z = 30 }` — parses in `ParsePrefixFn` identifier handler when a struct name is followed by `{`. Emits `StructNew`, then `Dup`+`value`+`StructSet`+`Pop` per field. Empty `{}` and partial initialization supported.
- **Display fix:** `Print`/`PrintLn` now handle `ValueType.None` (prints `0`) and `ValueType.ObjectRef` (prints `[ObjRef:...]`) separately, fixing display of uninitialized struct fields.
- **try/catch/throw exceptions:** Lexer keywords `try`/`catch`/`throw`. New opcodes `PushExceptionHandler` (2-byte operand = relative catch offset), `PopExceptionHandler`, `Throw`, `PushExceptionValue`. VM keeps a handler stack (`_handlerTargetStack/[SS]tackTop/FrameBase/CallTop`, capacity = callStackCapacity) recording frame/stack/call-depth state at `try` entry. On `throw`, `HandleThrow` unwinds StackTop/FrameBase/CallStackTop to the saved handler state and jumps to its catch block (cross-frame propagation works); unhandled `throw` raises "Unhandled exception: <value>" to the host. `PushExceptionValue` exposes the thrown value so `catch (e)` binds it to a local (slot aligned via `_localCount = baseCount` reset). Parser: `TryStatement` compiles try → Push handlers → Pop + Jump-over-catch → catch → patch; supports nested/rethrow. Verified: basic throw/catch, no-exception skips catch, throw through function frames, nested rethrow, unhandled errors, binary path.
- **Binary string-load bug fixed (pre-existing):** `ExecuteBinary` read string constants via `stringPool.GetString(strIdx)` using a **string-list index** where a **byte offset** was expected, causing an AccessViolation on any binary with ≥2 distinct strings. Fixed by loading strings into `string[] loadedStrings` on load, then `stringPool.Intern(s.AsSpan())` for each constant.

## Next Steps

- All for-loop variants tested and passing (including `for (;;)` with break, `for (; j < N; …)`, `for (var i = 0; …)`).
- For-each expansion verified working with 5-element buffer, nested for loops, and post-for-each variable declarations.
- Break/continue works in all loop types (while, for, for-each).
- `for (;;)` infinite loop support complete.
- Ternary, comments, `++`/`--`, struct literals, and buffer literals added.
- Recursion + forward/mutual function references implemented and verified (`Fact(5)=120`, `Fib(10)=55`, `Even`/`Odd` mutual recursion, forward `later()`, runaway `deep` hits 256-depth guard).
- Consider: nested buffer literals `[[1, 2], [3, 4]]` (buffers of buffers) — now done.
- Verify forward-ref/recursion works through the `--compile`/`--run` binary path — now verified (`f(6)=720`).
- Deeper call stack support — now done: VM value/call stacks made configurable (defaults 65536/4096), deep recursion verified to 4095+, both overflow guards produce dynamic messages.
- Revisit `GetNativeNames().ToArray()` allocation in `CompileToBinary` (zero-allocation principle).
- Consider: try/catch integration with guard errors (buffer bounds, uninitialized reads emit .NET exceptions, not Nova catchable values).
- Consider: method-call syntax for buffers/structs, or `finally` blocks.
- Consider: type system improvements or method-call syntax for buffers/structs.

## Critical Context

- **Heap resize is safe**: `HandleStructNew` and `HandleBufferNew` call `ctx->HeapBase = vm.Heap.GetBuffer()` after every `Alloc`.
- **StringPool uses same `NativeMemory` pattern**: Stable pointer across resizes via `EnsureCapacity` with `NativeMemory.Alloc`+`CopyBlock`+`Free`.
- **Buffer metadata**: `HeapAllocator.AllocBuffer(count)` stores element count in header offset AND a `Dictionary<int,int>` keyed by user handle. Dictionary provides C#-safe access for `Len()`; heap header provides future raw-pointer access.
- **Benchmark perf**: ~2600-line script compiles in ~150ms (zero managed heap allocations); 100k iteration math-heavy loop runs in ~72ms. All test sections pass: for-loop (4 variants), buffer, vertex processing, compound assignment, Len, slice, utilities, if/else, math, stress matrix, for-loop break/continue.
- **For-loop continue target**: When increment exists, continue jumps to `Code.Count - incLen` (start of increment code), then the backwards Jump fires. When no increment, continue jumps to `loopStart` (condition re-evaluation). Verified by for-loop continue test (prints 0,1,3,4 — skips 2).
- **For-loop break target**: Break jumps to `_chunk.Code.Count` after the backwards Jump (exit after for-loop). Verified by for-loop break test (prints 0,1,2 — breaks at 3).
- **ForEachStatement break/continue**: Works via the same save/restore loop-state mechanism. Hidden locals are properly managed.
- **HandleGreaterEqual fix**: Both if-branch (Int vs Int) and else-branch (mixed/non-Int) now use the correct comparison method. Previously the else-branch used `AsInt >= AsInt` which would give wrong results for Float vs Int or Float vs Float comparisons.

## Relevant Files

- **`NovaLang/Runtime/VM.cs`**: Arithmetic/comparison handlers, struct ops, call/ret, `HandleDiv` (zero-guard), dispatch table (~52 entries). **Exception handling:** handler stack arrays (`_handlerTargetStack/_handlerStackTop/_handlerFrameBase/_handlerCallTop`, sized to callStackCapacity), `HandlePushExceptionHandler` (records catch offset + frame/stack/call-top), `HandlePopExceptionHandler`, `HandleThrow` (unwinds to saved handler state, jumps to catch, or raises "Unhandled exception"), `HandlePushExceptionValue`. **VM stacks configurable** — ctor takes `valueStackCapacity` (default 65536) and `callStackCapacity` (default 4096); `HandleCall` guards depth + value-stack overflow.
- **`NovaLang/Compiler/Parser.cs`**: ... `MatchCompoundAssignment`, ternary infix handler, `++`/`--` in identifier prefix handler, `TryStatement` (PushExceptionHandler with patched catch offset → body → Pop+Jump-over-catch → catch; aligns `_localCount`/slot for `catch (e)` binding; nested/rethrow supported), `ThrowStatement`.
- **`NovaLang/Compiler/Lexer.cs`**: `TokenType` enum (49 types: added `Try`/`Catch`/`Throw`), keyword map, `//` comments and `/* */` block comments.
- **`NovaLang/Compiler/Chunk.cs`**: `WriteU16`, `AddConstant` (float-safe dedup), `ReorderConstantsByFrequency()`, `OpCodeSize()` table (adds `PushExceptionHandler`=3, `PopExceptionHandler`/`Throw`/`PushExceptionValue`=1).
- **`NovaLang/Runtime/NovaValue.cs`**: `ExplicitLayout` union struct with `Type`, `AsInt`, `AsFloat`, `AsBool`, `ObjectId`.
- **`NovaLang/Program.cs`**: Native registrations (17 fns), generated benchmark. `ExecuteBinary` now loads strings into `loadedStrings[]` then re-interns (fixes pre-existing binary string-offset crash).
