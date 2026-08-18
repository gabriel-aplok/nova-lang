using NovaLang.Runtime;

namespace NovaLang.Compiler;

public struct Local
{
    public int NameStart;
    public int NameLength;
    public int Depth;
    public int StructIndex; // -1 for scalar values
}

public struct StructLayout
{
    public int NameStart;
    public int NameLength;
    public int FieldCount;
    public int FieldStart; // index into _structFields
}

public struct StructField
{
    public int NameStart;
    public int NameLength;
}

public ref struct Parser
{
    private Lexer _lexer;
    private Token _current;
    private Token _previous;
    private Token _lastIdentifier;
    private readonly ReadOnlySpan<char> _source;
    private readonly Chunk _chunk;

    private readonly Span<Local> _locals;
    private int _localCount;
    private int _scopeDepth;

    private GlobalSymbolTable _symbolTable;
    private readonly Span<StructLayout> _structs;
    private readonly Span<StructField> _structFields;
    private int _structCount;
    private int _structFieldCount;
    private int _lastIdentifierSlot; // -1 if last identifier was not a local

    // Loop tracking for break/continue — saved/restored for nesting
    private List<int> _loopBreakPatches = [];
    private List<int> _loopContinuePatches = [];
    private int _loopExitTarget;
    private bool _inLoop;
    private readonly Runtime.StringPool _stringPool;

    // Forward function reference fixups — recorded when a user function is
    // called before its body address is known, resolved once all functions
    // are declared. Stores the byte offset of the Call's address operand.
    private readonly List<int> _funcFixupPositions = [];
    private readonly List<int> _funcFixupNameStart = [];
    private readonly List<int> _funcFixupNameLength = [];

    public Parser(
        string source,
        Chunk targetChunk,
        Span<Local> localsBuffer,
        Span<SymbolEntry> symbolEntries,
        Span<StructLayout> structLayouts,
        Span<StructField> structFields,
        ReadOnlySpan<string> nativeNames,
        Runtime.StringPool stringPool
    )
    {
        _source = source.AsSpan();
        _lexer = new Lexer(source);
        _chunk = targetChunk;
        _locals = localsBuffer;
        _localCount = 0;
        _scopeDepth = 0;
        _current = default;
        _previous = default;
        _lastIdentifier = default;
        _structs = structLayouts;
        _structFields = structFields;
        _structCount = 0;
        _structFieldCount = 0;
        _lastIdentifierSlot = -1;
        _stringPool = stringPool;

        _symbolTable = new GlobalSymbolTable(_source, nativeNames, symbolEntries);
        for (int i = 0; i < nativeNames.Length; i++)
            _symbolTable.AddNativeFunc(i, (byte)i);

        Advance();
    }

    private void Advance()
    {
        _previous = _current;
        _current = _lexer.NextToken();
    }

    private void Consume(TokenType type, string msg)
    {
        if (_current.Type == type)
        {
            Advance();
            return;
        }
        throw new Exception($"Compiler Error [Line {_current.Line}]: {msg}");
    }

    public void Compile()
    {
        while (_current.Type != TokenType.EOF)
            Declaration();
        ResolveForwardCalls();
        _chunk.WriteOp(OpCode.Return, _previous.Line);
    }

    // Resolves Call operands that referenced user functions whose body address
    // was not yet known at the time of the call (forward references / recursion).
    private void ResolveForwardCalls()
    {
        for (int i = 0; i < _funcFixupPositions.Count; i++)
        {
            int codePos = _funcFixupPositions[i];
            ReadOnlySpan<char> funcName = _source.Slice(
                _funcFixupNameStart[i],
                _funcFixupNameLength[i]
            );

            if (_symbolTable.TryGet(funcName, out SymbolEntry entry))
            {
                if (entry.Kind == SymbolKind.UserFunc)
                    _chunk.Code[codePos] = entry.Address;
                else
                    throw new Exception(
                        $"Compiler Error: Expected user function but found native function for '{funcName.ToString()}'."
                    );
            }
            else
                throw new Exception(
                    $"Compiler Error: Undefined function reference: '{funcName.ToString()}'"
                );
        }

        _funcFixupPositions.Clear();
        _funcFixupNameStart.Clear();
        _funcFixupNameLength.Clear();
    }

    private void Declaration()
    {
        if (_current.Type == TokenType.Var)
        {
            Advance();
            VarDeclaration();
        }
        else if (_current.Type == TokenType.Func)
        {
            Advance();
            FuncDeclaration();
        }
        else if (_current.Type == TokenType.Struct)
        {
            Advance();
            StructDeclaration();
        }
        else
            Statement();
    }

    private void StructDeclaration()
    {
        Consume(TokenType.Identifier, "Expect struct name.");
        Token nameToken = _previous;
        Consume(TokenType.LeftBrace, "Expect '{' before struct body.");
        int fieldStart = _structFieldCount;
        int fieldCount = 0;
        while (_current.Type != TokenType.RightBrace && _current.Type != TokenType.EOF)
        {
            Consume(TokenType.Var, "Expect field declaration.");
            Consume(TokenType.Identifier, "Expect field identifier name.");
            _structFields[_structFieldCount++] = new StructField
            {
                NameStart = _previous.Start,
                NameLength = _previous.Length,
            };
            Consume(TokenType.Semicolon, "Expect ';' after field variable definition.");
            fieldCount++;
        }
        Consume(TokenType.RightBrace, "Expect '}' after struct layout.");
        _structs[_structCount++] = new StructLayout
        {
            NameStart = nameToken.Start,
            NameLength = nameToken.Length,
            FieldCount = fieldCount,
            FieldStart = fieldStart,
        };
    }

    private void FuncDeclaration()
    {
        Consume(TokenType.Identifier, "Expect function name.");
        Token nameToken = _previous;

        _chunk.WriteOp(OpCode.Jump, nameToken.Line);
        int jumpOffsetPosition = _chunk.Write(0, nameToken.Line);
        int functionAddress = _chunk.Code.Count;

        Consume(TokenType.LeftParen, "Expect '(' after function name.");
        _scopeDepth++;

        int outerLocalCount = _localCount;
        _localCount = 0; // Relative activation record frame generation

        if (_current.Type != TokenType.RightParen)
        {
            do
            {
                Consume(TokenType.Identifier, "Expect parameter identifier.");
                _locals[_localCount++] = new Local
                {
                    NameStart = _previous.Start,
                    NameLength = _previous.Length,
                    Depth = _scopeDepth,
                };
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after parameters.");
        Consume(TokenType.LeftBrace, "Expect '{' before function body.");

        while (_current.Type != TokenType.RightBrace && _current.Type != TokenType.EOF)
            Declaration();
        Consume(TokenType.RightBrace, "Expect '}' after function body.");

        _chunk.WriteOp(OpCode.RetUserFunc, _previous.Line);
        _scopeDepth--;
        _localCount = outerLocalCount; // Restore baseline tracking

        int jumpDistance = _chunk.Code.Count - jumpOffsetPosition - 1;
        _chunk.PatchJump(jumpOffsetPosition, jumpDistance);
        _symbolTable.AddUserFunc(nameToken.Start, nameToken.Length, (byte)functionAddress);
    }

    private void VarDeclaration()
    {
        Consume(TokenType.Identifier, "Expect variable name.");
        Token nameToken = _previous;
        int structIndex = -1;

        if (_current.Type == TokenType.Equals)
        {
            Advance();

            if (_current.Type == TokenType.Identifier)
            {
                structIndex = FindStruct(_source.Slice(_current.Start, _current.Length));
            }

            if (structIndex >= 0)
            {
                Advance();
                if (_current.Type == TokenType.LeftBrace)
                {
                    Advance();
                    ParseStructLiteral(structIndex);
                }
                else
                {
                    Consume(TokenType.LeftParen, "Expect '(' after struct type.");
                    Consume(TokenType.RightParen, "Expect ')' after struct constructor.");
                    _chunk.WriteOp(OpCode.StructNew, nameToken.Line);
                    _chunk.Write((byte)_structs[structIndex].FieldCount, nameToken.Line);
                }
            }
            else
            {
                ParseExpression(0);
            }
            EmitStoreLocalDup(_localCount, nameToken.Line);
            // StoreLocalDup copies to slot without popping, maintains StackTop >= _localCount.
        }
        else
        {
            _chunk.WriteOp(OpCode.PushNone, nameToken.Line);
            // PushNone value already at StackTop which == slot after register.
        }
        Consume(TokenType.Semicolon, "Expect ';' after variable declaration.");
        _locals[_localCount++] = new Local
        {
            NameStart = nameToken.Start,
            NameLength = nameToken.Length,
            Depth = _scopeDepth,
            StructIndex = structIndex,
        };
    }

    private void Statement()
    {
        if (_current.Type == TokenType.For)
        {
            Advance();
            ForStatement();
        }
        else if (_current.Type == TokenType.While)
        {
            Advance();
            WhileStatement();
        }
        else if (_current.Type == TokenType.If)
        {
            Advance();
            IfStatement();
        }
        else if (_current.Type == TokenType.Return)
        {
            Advance();
            ReturnStatement();
        }
        else if (_current.Type == TokenType.Break)
        {
            if (!_inLoop)
                throw new Exception("'break' outside loop");
            Advance();
            Consume(TokenType.Semicolon, "Expect ';' after 'break'.");
            _chunk.WriteOp(OpCode.Jump, _previous.Line);
            _loopBreakPatches.Add(_chunk.Write(0, _previous.Line));
        }
        else if (_current.Type == TokenType.Continue)
        {
            if (!_inLoop)
                throw new Exception("'continue' outside loop");
            Advance();
            Consume(TokenType.Semicolon, "Expect ';' after 'continue'.");
            _chunk.WriteOp(OpCode.Jump, _previous.Line);
            _loopContinuePatches.Add(_chunk.Write(0, _previous.Line));
        }
        else if (_current.Type == TokenType.Try)
        {
            Advance();
            TryStatement();
        }
        else if (_current.Type == TokenType.Throw)
        {
            Advance();
            ThrowStatement();
        }
        else
            ExpressionStatement();
    }

    private void ReturnStatement()
    {
        if (_current.Type != TokenType.Semicolon)
            ParseExpression(0);
        Consume(TokenType.Semicolon, "Expect ';' after return operations.");
        _chunk.WriteOp(OpCode.RetUserFunc, _previous.Line);
    }

    private void WhileStatement()
    {
        // Save outer loop state for nesting
        List<int> savedBreakPatches = _loopBreakPatches;
        List<int> savedContinuePatches = _loopContinuePatches;
        var savedExitTarget = _loopExitTarget;
        var savedInLoop = _inLoop;

        _loopBreakPatches = [];
        _loopContinuePatches = [];
        _inLoop = true;

        int loopStart = _chunk.Code.Count;
        Consume(TokenType.LeftParen, "Expect '(' after 'while'.");
        ParseExpression(0);
        Consume(TokenType.RightParen, "Expect ')' after loop conditions.");

        int exitJumpOp = _chunk.WriteOp(OpCode.JumpIfFalse, _previous.Line);
        int exitJumpOffset = _chunk.Write(0, _previous.Line);

        Consume(TokenType.LeftBrace, "Expect '{' to open statement blocks.");
        while (_current.Type != TokenType.EOF && _current.Type != TokenType.RightBrace)
            Declaration();
        Consume(TokenType.RightBrace, "Expect '}' to close statement blocks.");

        // Patch continue jumps → loopStart (condition re-eval)
        foreach (int offset in _loopContinuePatches)
            _chunk.Code[offset] = (byte)(sbyte)(loopStart - (offset + 1));

        _chunk.WriteOp(OpCode.Jump, _previous.Line);
        int backwardJumpDistance = loopStart - (_chunk.Code.Count + 1);
        _chunk.Write((byte)backwardJumpDistance, _previous.Line);

        // Patch exit jump
        _chunk.PatchJump(exitJumpOffset, _chunk.Code.Count - exitJumpOffset - 1);

        // Patch break jumps → exit (after backwards jump)
        foreach (int offset in _loopBreakPatches)
            _chunk.Code[offset] = (byte)(sbyte)(_chunk.Code.Count - (offset + 1));

        // Restore outer loop state
        _loopBreakPatches = savedBreakPatches;
        _loopContinuePatches = savedContinuePatches;
        _loopExitTarget = savedExitTarget;
        _inLoop = savedInLoop;
    }

    private void IfStatement()
    {
        Consume(TokenType.LeftParen, "Expect '(' after 'if'.");
        ParseExpression(0);
        Consume(TokenType.RightParen, "Expect ')' after condition.");

        int exitJumpOffset = -1;
        {
            int opPos = _chunk.WriteOp(OpCode.JumpIfFalse, _previous.Line);
            exitJumpOffset = _chunk.Write(0, _previous.Line);
        }

        Consume(TokenType.LeftBrace, "Expect '{' to open block.");
        while (_current.Type != TokenType.EOF && _current.Type != TokenType.RightBrace)
            Declaration();
        Consume(TokenType.RightBrace, "Expect '}' to close block.");

        if (Match(TokenType.Else))
        {
            int elseJumpOffset = -1;
            {
                int opPos = _chunk.WriteOp(OpCode.Jump, _previous.Line);
                elseJumpOffset = _chunk.Write(0, _previous.Line);
            }

            _chunk.PatchJump(exitJumpOffset, _chunk.Code.Count - exitJumpOffset - 1);

            if (Match(TokenType.If))
            {
                IfStatement();
            }
            else
            {
                Consume(TokenType.LeftBrace, "Expect '{' to open block.");
                while (_current.Type != TokenType.EOF && _current.Type != TokenType.RightBrace)
                    Declaration();
                Consume(TokenType.RightBrace, "Expect '}' to close block.");
            }

            _chunk.PatchJump(elseJumpOffset, _chunk.Code.Count - elseJumpOffset - 1);
        }
        else
        {
            _chunk.PatchJump(exitJumpOffset, _chunk.Code.Count - exitJumpOffset - 1);
        }
    }

    private void ThrowStatement()
    {
        if (_current.Type != TokenType.Semicolon)
        {
            ParseExpression(0);
            _chunk.WriteOp(OpCode.Throw, _previous.Line);
        }
        Consume(TokenType.Semicolon, "Expect ';' after throw.");
    }

    private void TryStatement()
    {
        // record the local/stack base so the catch block can bind the exception
        // variable at a slot aligned with the unwound runtime stack.
        int baseCount = _localCount;
        _scopeDepth++;

        Consume(TokenType.LeftBrace, "Expect '{' to open try block.");

        // Peek ahead to see whether this construct declares a catch and/or a
        // finally clause. We look at the raw token stream that follows so we can
        // lay out the handler stack (finally wraps catch) before the try body.
        (bool hasCatch, bool hasFinally) = ScanTryClause();

        int finallyHandlerPos = -1;
        int catchHandlerPos = -1;
        int tryNormalJumpOffset = -1; // try body normal completion jump operand pos
        int catchNormalJumpOffset = -1; // catch normal completion jump operand pos
        bool hasExceptionVar = false;

        // Outer exception handler: when an exception is not handled by the inner
        // catch (or has none), unwinding lands here to run the finally body with
        // a rethrow marker, then continues propagating once finally completes.
        if (hasFinally)
        {
            _chunk.WriteOp(OpCode.PushExceptionHandler, _previous.Line);
            finallyHandlerPos = _chunk.WriteU16(0, _previous.Line);
        }

        // Inner handler for the catch clause (differs from outer so exceptions
        // thrown in the try body are caught first, before the finally rethrow).
        if (hasCatch)
        {
            _chunk.WriteOp(OpCode.PushExceptionHandler, _previous.Line);
            catchHandlerPos = _chunk.WriteU16(0, _previous.Line);
        }

        while (_current.Type != TokenType.EOF && _current.Type != TokenType.RightBrace)
            Declaration();
        Consume(TokenType.RightBrace, "Expect '}' to close try block.");

        // Normal completion of the try body: release the catch handler and (if
        // present) the finally handler, then run the finally body (or jump past
        // the catch block when there is no finally).
        if (hasCatch)
            _chunk.WriteOp(OpCode.PopExceptionHandler, _previous.Line);
        if (hasFinally)
            _chunk.WriteOp(OpCode.PopExceptionHandler, _previous.Line);

        if (hasFinally || !hasCatch)
        {
            _chunk.WriteOp(OpCode.PushConstInt_0, _previous.Line);
            _chunk.WriteOp(OpCode.Jump, _previous.Line);
            tryNormalJumpOffset = _chunk.Write(0, _previous.Line);
        }
        else
        {
            _chunk.WriteOp(OpCode.Jump, _previous.Line);
            tryNormalJumpOffset = _chunk.Write(0, _previous.Line);
        }

        if (hasCatch)
        {
            // Patch the inner handler's catch target to the start of the catch
            // block, then compile it.
            int catchStart = _chunk.Code.Count;
            _chunk.Code[catchHandlerPos] = (byte)(catchStart & 0xFF);
            _chunk.Code[catchHandlerPos + 1] = (byte)((catchStart >> 8) & 0xFF);

            Consume(TokenType.Catch, "Expect 'catch' after try block.");

            _localCount = baseCount;

            if (Match(TokenType.LeftParen))
            {
                hasExceptionVar = true;
                Consume(TokenType.Identifier, "Expect exception variable name.");
                Token excToken = _previous;
                Consume(TokenType.RightParen, "Expect ')' after exception variable.");
                _chunk.WriteOp(OpCode.PushExceptionValue, _previous.Line);
                EmitStoreLocalDup(_localCount, _previous.Line);
                _locals[_localCount++] = new Local
                {
                    NameStart = excToken.Start,
                    NameLength = excToken.Length,
                    Depth = _scopeDepth,
                    StructIndex = -1,
                };
            }
            else
            {
                _chunk.WriteOp(OpCode.PushExceptionValue, _previous.Line);
                _chunk.WriteOp(OpCode.Pop, _previous.Line);
            }

            Consume(TokenType.LeftBrace, "Expect '{' to open catch block.");
            while (_current.Type != TokenType.EOF && _current.Type != TokenType.RightBrace)
                Declaration();
            Consume(TokenType.RightBrace, "Expect '}' to close catch block.");

            // The catch block is a self-contained scope: its exception variable
            // (if any) is popped after the block and _localCount resets so that
            // locals declared after the try/catch are allocated/slot-aligned again
            // (on both the normal and exceptional paths).
            if (hasExceptionVar)
            {
                _chunk.WriteOp(OpCode.Pop, _previous.Line);
                _localCount = baseCount;
            }

            // Normal completion of the catch block: the catch handler was already
            // consumed by unwinding. Release the finally handler (if any) and run
            // the finally body through the normal (non-rethrow) path.
            if (hasFinally)
            {
                _chunk.WriteOp(OpCode.PopExceptionHandler, _previous.Line);
                _chunk.WriteOp(OpCode.PushConstInt_0, _previous.Line);
                _chunk.WriteOp(OpCode.Jump, _previous.Line);
                catchNormalJumpOffset = _chunk.Write(0, _previous.Line);
            }
        }

        if (!hasFinally)
        {
            // No finally: the try/catch construct is done. Resolve the try-body
            // normal-completion jump to skip past the catch block.
            int end = _chunk.Code.Count;
            _chunk.PatchJump(tryNormalJumpOffset, end - tryNormalJumpOffset - 1);
        }
        else
        {
            // Exception path for the finally handler: unwinding lands here,
            // pushes a rethrow marker, and enters the shared finally body.
            int exceptEntry = _chunk.Code.Count;
            _chunk.Code[finallyHandlerPos] = (byte)(exceptEntry & 0xFF);
            _chunk.Code[finallyHandlerPos + 1] = (byte)((exceptEntry >> 8) & 0xFF);
            _chunk.WriteOp(OpCode.PushConstInt_1, _previous.Line);

            // Shared finally body (runs on both normal and exceptional paths).
            int finallyBody = _chunk.Code.Count;
            Consume(TokenType.Finally, "Expect 'finally' after try block.");
            Consume(TokenType.LeftBrace, "Expect '{' to open finally block.");
            while (_current.Type != TokenType.EOF && _current.Type != TokenType.RightBrace)
                Declaration();
            Consume(TokenType.RightBrace, "Expect '}' to close finally block.");
            _chunk.WriteOp(OpCode.EndFinally, _previous.Line);

            // Patch the try-body and catch-body normal-completion jumps to land
            // at the finally body (skipping the rethrow prologue).
            _chunk.PatchJump(tryNormalJumpOffset, finallyBody - tryNormalJumpOffset - 1);
            if (catchNormalJumpOffset != -1)
                _chunk.PatchJump(catchNormalJumpOffset, finallyBody - catchNormalJumpOffset - 1);
        }

        _scopeDepth--;
    }

    // Scans the raw token stream following a try to determine whether a catch
    // and/or finally clause is declared at the construct level. Must NOT disturb
    // the real lexer position, so it operates on a copy of the lexer state.
    private (bool hasCatch, bool hasFinally) ScanTryClause()
    {
        Lexer probe = _lexer;
        int depth = 1; // we are inside the try { ... } block
        bool c = false;
        bool f = false;
        while (true)
        {
            Token t = probe.NextToken();
            if (t.Type == TokenType.EOF)
                break;
            if (t.Type == TokenType.LeftBrace)
                depth++;
            else if (t.Type == TokenType.RightBrace)
                depth--;
            else if (depth == 0)
            {
                if (t.Type == TokenType.Catch)
                    c = true;
                else if (t.Type == TokenType.Finally)
                    f = true;
                else if (t.Type == TokenType.Semicolon || t.Type == TokenType.EOF)
                    break;
            }
        }
        return (c, f);
    }

    private void ForStatement()
    {
        Consume(TokenType.LeftParen, "Expect '(' after 'for'.");

        // initialzer clause — check for for-each pattern (before loop state save)
        if (Match(TokenType.Var))
        {
            Consume(TokenType.Identifier, "Expect variable name.");
            Token nameToken = _previous;
            if (Match(TokenType.In))
            {
                ForEachStatement(nameToken);
                return;
            }

            // regular for-loop var init: must have '=' or ';'
            if (Match(TokenType.Equals))
            {
                if (_current.Type == TokenType.Identifier)
                {
                    int si = FindStruct(_source.Slice(_current.Start, _current.Length));
                    if (si >= 0)
                    {
                        Advance();
                        if (_current.Type == TokenType.LeftBrace)
                        {
                            Advance();
                            ParseStructLiteral(si);
                        }
                        else
                        {
                            Consume(TokenType.LeftParen, "Expect '(' after struct type.");
                            Consume(TokenType.RightParen, "Expect ')' after struct constructor.");
                            _chunk.WriteOp(OpCode.StructNew, nameToken.Line);
                            _chunk.Write((byte)_structs[si].FieldCount, nameToken.Line);
                        }
                    }
                    else
                    {
                        ParseExpression(0);
                    }
                }
                else
                {
                    ParseExpression(0);
                }
            }
            else
            {
                _chunk.WriteOp(OpCode.PushNone, nameToken.Line);
            }
            EmitStoreLocalDup(_localCount, nameToken.Line);
            Consume(TokenType.Semicolon, "Expect ';' after variable declaration.");
            _locals[_localCount++] = new Local
            {
                NameStart = nameToken.Start,
                NameLength = nameToken.Length,
                Depth = _scopeDepth,
                StructIndex = -1,
            };
        }
        else if (_current.Type != TokenType.Semicolon)
            ExpressionStatement();
        else
            Advance();

        // Save outer loop state for break/continue (after for-each check)
        List<int> savedBreakPatches = _loopBreakPatches;
        List<int> savedContinuePatches = _loopContinuePatches;
        var savedExitTarget = _loopExitTarget;
        var savedInLoop = _inLoop;

        _loopBreakPatches = [];
        _loopContinuePatches = [];
        _inLoop = true;

        // condition clause
        int loopStart = _chunk.Code.Count;
        int exitJumpOffset = -1;
        if (_current.Type != TokenType.Semicolon)
            ParseExpression(0);
        Consume(TokenType.Semicolon, "Expect ';' after loop condition.");
        if (loopStart < _chunk.Code.Count)
        {
            _chunk.WriteOp(OpCode.JumpIfFalse, _previous.Line);
            exitJumpOffset = _chunk.Write(0, _previous.Line);
        }

        // increment clause. parse now, emit later (after body)
        int incStart = -1;
        int incEnd = -1;
        if (_current.Type != TokenType.RightParen)
        {
            incStart = _chunk.Code.Count;
            ParseExpression(0);
            incEnd = _chunk.Code.Count;
        }
        Consume(TokenType.RightParen, "Expect ')' after for clauses.");

        // body
        Consume(TokenType.LeftBrace, "Expect '{' to open for body.");
        while (_current.Type != TokenType.EOF && _current.Type != TokenType.RightBrace)
            Declaration();
        Consume(TokenType.RightBrace, "Expect '}' to close for body.");

        // move increment bytecode to after body (if any)
        if (incStart >= 0)
        {
            int incLen = incEnd - incStart;
            List<byte> incCode = _chunk.Code.GetRange(incStart, incLen);
            List<int> incLines = _chunk.Lines.GetRange(incStart, incLen);
            _chunk.Code.RemoveRange(incStart, incLen);
            _chunk.Lines.RemoveRange(incStart, incLen);
            _chunk.Code.AddRange(incCode);
            _chunk.Lines.AddRange(incLines);

            // Fix break/continue offsets that shifted when increment was removed
            for (int i = 0; i < _loopBreakPatches.Count; i++)
                if (_loopBreakPatches[i] >= incStart)
                    _loopBreakPatches[i] -= incLen;
            for (int i = 0; i < _loopContinuePatches.Count; i++)
                if (_loopContinuePatches[i] >= incStart)
                    _loopContinuePatches[i] -= incLen;

            bool incNeedsPop = NeedsPop(incCode);
            if (incNeedsPop)
                _chunk.WriteOp(OpCode.Pop, _previous.Line);

            // Patch continue jumps → start of increment code (before backwards Jump)
            int continueTarget = _chunk.Code.Count - incLen - (incNeedsPop ? 1 : 0);
            foreach (int offset in _loopContinuePatches)
                _chunk.Code[offset] = (byte)(sbyte)(continueTarget - (offset + 1));
        }
        else
        {
            // No increment: continue jumps to condition re-eval
            foreach (int offset in _loopContinuePatches)
                _chunk.Code[offset] = (byte)(sbyte)(loopStart - (offset + 1));
        }

        // jump back to condition
        _chunk.WriteOp(OpCode.Jump, _previous.Line);
        _chunk.Write((byte)(loopStart - (_chunk.Code.Count + 1)), _previous.Line);

        // patch exit jump
        if (exitJumpOffset >= 0)
            _chunk.PatchJump(exitJumpOffset, _chunk.Code.Count - exitJumpOffset - 1);

        // Patch break jumps → exit (after backwards Jump)
        foreach (int offset in _loopBreakPatches)
            _chunk.Code[offset] = (byte)(sbyte)(_chunk.Code.Count - (offset + 1));

        // Restore outer loop state
        _loopBreakPatches = savedBreakPatches;
        _loopContinuePatches = savedContinuePatches;
        _loopExitTarget = savedExitTarget;
        _inLoop = savedInLoop;
    }

    private void ForEachStatement(Token loopVarToken)
    {
        // Save outer loop state for nesting
        List<int> savedBreakPatches = _loopBreakPatches;
        List<int> savedContinuePatches = _loopContinuePatches;
        var savedExitTarget = _loopExitTarget;
        var savedInLoop = _inLoop;

        _loopBreakPatches = [];
        _loopContinuePatches = [];
        _inLoop = true;

        ParseExpression(0);
        Consume(TokenType.RightParen, "Expect ')' after for-each expression.");

        int bufSlot = _localCount;
        int idxSlot = _localCount + 1;
        int lenSlot = _localCount + 2;
        _localCount += 3;

        // Store buf and maintain StackTop >= _localCount invariant
        _chunk.WriteOp(OpCode.StoreLocal, loopVarToken.Line);
        _chunk.Write((byte)bufSlot, loopVarToken.Line);
        EmitLoadLocal(bufSlot, loopVarToken.Line);

        // Store idx = 0 and maintain invariant
        _chunk.WriteOp(OpCode.PushConstInt_0, loopVarToken.Line);
        _chunk.WriteOp(OpCode.StoreLocal, loopVarToken.Line);
        _chunk.Write((byte)idxSlot, loopVarToken.Line);
        EmitLoadLocal(idxSlot, loopVarToken.Line);

        // Load buf for Len(buf), call Len, store and maintain invariant
        EmitLoadLocal(bufSlot, loopVarToken.Line);
        if (!_symbolTable.TryGet("Len".AsSpan(), out SymbolEntry lenEntry))
            throw new Exception("Compiler Error: 'Len' function not found.");
        _chunk.WriteOp(OpCode.CallNative, loopVarToken.Line);
        _chunk.Write(lenEntry.Address, loopVarToken.Line);
        _chunk.WriteOp(OpCode.StoreLocal, loopVarToken.Line);
        _chunk.Write((byte)lenSlot, loopVarToken.Line);
        EmitLoadLocal(lenSlot, loopVarToken.Line);

        int loopStart = _chunk.Code.Count;

        EmitLoadLocal(idxSlot, loopVarToken.Line);
        EmitLoadLocal(lenSlot, loopVarToken.Line);
        _chunk.WriteOp(OpCode.Less, loopVarToken.Line);

        _chunk.WriteOp(OpCode.JumpIfFalse, loopVarToken.Line);
        int exitJumpOffset = _chunk.Write(0, loopVarToken.Line);

        EmitLoadLocal(bufSlot, loopVarToken.Line);
        EmitLoadLocal(idxSlot, loopVarToken.Line);
        _chunk.WriteOp(OpCode.BufferGet, loopVarToken.Line);

        int varSlot = _localCount;
        _locals[_localCount++] = new Local
        {
            NameStart = loopVarToken.Start,
            NameLength = loopVarToken.Length,
            Depth = _scopeDepth,
            StructIndex = -1,
        };
        _chunk.WriteOp(OpCode.StoreLocal, loopVarToken.Line);
        _chunk.Write((byte)varSlot, loopVarToken.Line);
        EmitLoadLocal(varSlot, loopVarToken.Line);

        Consume(TokenType.LeftBrace, "Expect '{' to open block.");
        while (_current.Type != TokenType.EOF && _current.Type != TokenType.RightBrace)
            Declaration();
        Consume(TokenType.RightBrace, "Expect '}' to close block.");

        // idx++ and maintain invariant
        EmitLoadLocal(idxSlot, loopVarToken.Line);
        _chunk.WriteOp(OpCode.PushConstInt_1, loopVarToken.Line);
        _chunk.WriteOp(OpCode.Add, loopVarToken.Line);
        _chunk.WriteOp(OpCode.StoreLocal, loopVarToken.Line);
        _chunk.Write((byte)idxSlot, loopVarToken.Line);

        // Patch continue jumps → after idx++ (before backwards Jump)
        foreach (int offset in _loopContinuePatches)
            _chunk.Code[offset] = (byte)(sbyte)(_chunk.Code.Count - (offset + 1));

        _chunk.WriteOp(OpCode.Jump, loopVarToken.Line);
        _chunk.Write((byte)(loopStart - (_chunk.Code.Count + 1)), loopVarToken.Line);

        _chunk.PatchJump(exitJumpOffset, _chunk.Code.Count - exitJumpOffset - 1);

        // Patch break jumps → exit (after backwards Jump)
        foreach (int offset in _loopBreakPatches)
            _chunk.Code[offset] = (byte)(sbyte)(_chunk.Code.Count - (offset + 1));

        // Restore outer loop state
        _loopBreakPatches = savedBreakPatches;
        _loopContinuePatches = savedContinuePatches;
        _loopExitTarget = savedExitTarget;
        _inLoop = savedInLoop;
    }

    private static bool NeedsPop(List<byte> code)
    {
        if (code.Count == 0)
            return false;
        byte b1 = code[^1];
        byte b2 = code.Count >= 2 ? code[^2] : (byte)0;
        if (
            b1 == (byte)OpCode.StoreLocal_0
            || b1 == (byte)OpCode.StoreLocal_1
            || b1 == (byte)OpCode.StoreLocal_2
            || b1 == (byte)OpCode.StoreLocal_3
            || b1 == (byte)OpCode.CallNative
        )
            return false;
        if (
            b2 == (byte)OpCode.StoreLocal
            || b2 == (byte)OpCode.StructSet
            || b2 == (byte)OpCode.BufferSet
        )
            return false;
        return true;
    }

    private void ExpressionStatement()
    {
        int before = _chunk.Code.Count;
        ParseExpression(0);
        Consume(TokenType.Semicolon, "Expect ';' after expression statement.");
        if (BeforeWouldSkipPop(before))
            return;
        _chunk.WriteOp(OpCode.Pop, _previous.Line);
    }

    private readonly bool BeforeWouldSkipPop(int before)
    {
        if (before >= _chunk.Code.Count - 1)
            return false;
        byte b2 = _chunk.Code[^2];
        byte b1 = _chunk.Code[^1];
        if (
            b1 == (byte)OpCode.StoreLocal_0
            || b1 == (byte)OpCode.StoreLocal_1
            || b1 == (byte)OpCode.StoreLocal_2
            || b1 == (byte)OpCode.StoreLocal_3
        )
            return true;
        if (
            b2 == (byte)OpCode.CallNative
            || b2 == (byte)OpCode.StoreLocal
            || b2 == (byte)OpCode.StructSet
            || b2 == (byte)OpCode.BufferSet
        )
            return true;
        return false;
    }

    public void ParseExpression(int precedence = 0)
    {
        Advance();
        ParsePrefixFn(_previous.Type);
        while (precedence < GetPrecedence(_current.Type))
        {
            Advance();
            ParseInfixFn(_previous.Type);
        }
    }

    private void ParsePrefixFn(TokenType type)
    {
        switch (type)
        {
            case TokenType.Int:
            {
                ReadOnlySpan<char> text = _source.Slice(_previous.Start, _previous.Length);
                int value = int.Parse(
                    text,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture
                );
                switch (value)
                {
                    case 0:
                        _chunk.WriteOp(OpCode.PushConstInt_0, _previous.Line);
                        break;
                    case 1:
                        _chunk.WriteOp(OpCode.PushConstInt_1, _previous.Line);
                        break;
                    default:
                        if (value >= -128 && value <= 127)
                        {
                            _chunk.WriteOp(OpCode.PushConstInt8, _previous.Line);
                            _chunk.Write((byte)(sbyte)value, _previous.Line);
                        }
                        else
                        {
                            int constantIndex = _chunk.AddConstant(NovaValue.Int(value));
                            EmitPushConst(constantIndex, _previous.Line);
                        }
                        break;
                }
                break;
            }
            case TokenType.Float:
            {
                ReadOnlySpan<char> text = _source.Slice(_previous.Start, _previous.Length);
                float value = float.Parse(
                    text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture
                );
                int constantIndex = _chunk.AddConstant(NovaValue.Float(value));
                EmitPushConst(constantIndex, _previous.Line);
                break;
            }

            case TokenType.LeftBracket:
            {
                int count = 0;
                if (_current.Type != TokenType.RightBracket)
                {
                    do
                    {
                        ParseExpression(0);
                        count++;
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightBracket, "Expect ']' after buffer literal.");
                _chunk.WriteOp(OpCode.BufferFromStack, _previous.Line);
                _chunk.Write((byte)count, _previous.Line);
                break;
            }

            case TokenType.String:
            {
                ReadOnlySpan<char> raw = _source.Slice(_previous.Start + 1, _previous.Length - 2);
                int poolOff;
                if (raw.Contains('\\'))
                {
                    Span<char> buf = stackalloc char[raw.Length];
                    int wp = 0;
                    for (int i = 0; i < raw.Length; i++)
                    {
                        if (raw[i] == '\\' && i + 1 < raw.Length)
                        {
                            buf[wp++] = raw[i + 1] switch
                            {
                                'n' => '\n',
                                't' => '\t',
                                'r' => '\r',
                                '0' => '\0',
                                '\\' => '\\',
                                '"' => '"',
                                '\'' => '\'',
                                _ => raw[i + 1],
                            };
                            i++;
                        }
                        else
                        {
                            buf[wp++] = raw[i];
                        }
                    }
                    poolOff = _stringPool.Intern(buf[..wp]);
                }
                else
                {
                    poolOff = _stringPool.Intern(raw);
                }
                int constantIndex = _chunk.AddConstant(NovaValue.String(poolOff));
                EmitPushConst(constantIndex, _previous.Line);
                break;
            }

            case TokenType.Identifier:
            {
                _lastIdentifier = _previous;
                ReadOnlySpan<char> name = _source.Slice(_previous.Start, _previous.Length);

                int structIdx = FindStruct(name);
                if (structIdx >= 0 && Match(TokenType.LeftBrace))
                {
                    ParseStructLiteral(structIdx);
                    break;
                }

                int slot = ResolveLocal(name);
                _lastIdentifierSlot = slot;

                if (slot != -1)
                {
                    if (Match(TokenType.PlusPlus))
                    {
                        _chunk.WriteOp(OpCode.Inc, _previous.Line);
                        _chunk.Write((byte)slot, _previous.Line);
                    }
                    else if (Match(TokenType.MinusMinus))
                    {
                        _chunk.WriteOp(OpCode.Dec, _previous.Line);
                        _chunk.Write((byte)slot, _previous.Line);
                    }
                    else if (Match(TokenType.Equals))
                    {
                        ParseExpression(0);
                        EmitStoreLocal(slot, _previous.Line);
                    }
                    else if (MatchCompoundAssignment(out OpCode compoundOp))
                    {
                        EmitLoadLocal(slot, _previous.Line);
                        ParseExpression(0);
                        _chunk.WriteOp(compoundOp, _previous.Line);
                        EmitStoreLocal(slot, _previous.Line);
                    }
                    else
                    {
                        EmitLoadLocal(slot, _previous.Line);
                    }
                }
                break;
            }

            case TokenType.Minus:
                ParseExpression(6);
                _chunk.WriteOp(OpCode.Sub, _previous.Line);
                break;

            case TokenType.LeftParen:
                ParseExpression(0);
                Consume(TokenType.RightParen, "Expect ')' after grouped expression.");
                break;
        }
    }

    private void ParseInfixFn(TokenType type)
    {
        switch (type)
        {
            case TokenType.Plus:
                ParseExpression(4);
                _chunk.WriteOp(OpCode.Add, _previous.Line);
                break;
            case TokenType.Minus:
                ParseExpression(4);
                _chunk.WriteOp(OpCode.Sub, _previous.Line);
                break;
            case TokenType.Star:
                ParseExpression(5);
                _chunk.WriteOp(OpCode.Mul, _previous.Line);
                break;
            case TokenType.Slash:
                ParseExpression(5);
                _chunk.WriteOp(OpCode.Div, _previous.Line);
                break;
            case TokenType.Less:
                ParseExpression(3);
                _chunk.WriteOp(OpCode.Less, _previous.Line);
                break;
            case TokenType.Greater:
                ParseExpression(3);
                _chunk.WriteOp(OpCode.Greater, _previous.Line);
                break;
            case TokenType.LessEqual:
                ParseExpression(3);
                _chunk.WriteOp(OpCode.LessEqual, _previous.Line);
                break;
            case TokenType.GreaterEqual:
                ParseExpression(3);
                _chunk.WriteOp(OpCode.GreaterEqual, _previous.Line);
                break;
            case TokenType.EqualsEquals:
                ParseExpression(2);
                _chunk.WriteOp(OpCode.Equal, _previous.Line);
                break;
            case TokenType.BangEqual:
                ParseExpression(2);
                _chunk.WriteOp(OpCode.NotEqual, _previous.Line);
                break;
            case TokenType.And:
            {
                // Stack: [left]
                // If left is falsy, short-circuit to push false
                _chunk.WriteOp(OpCode.JumpIfFalse, _previous.Line);
                int endPos = _chunk.Write(0, _previous.Line);

                ParseExpression(1);

                _chunk.WriteOp(OpCode.Jump, _previous.Line);
                int donePos = _chunk.Write(0, _previous.Line);

                int falseIdx = _chunk.AddConstant(NovaValue.Bool(false));
                EmitPushConst(falseIdx, _previous.Line);

                _chunk.PatchJump(endPos, _chunk.Code.Count - endPos - 1);
                _chunk.PatchJump(donePos, _chunk.Code.Count - donePos - 1);
                break;
            }
            case TokenType.Or:
            {
                // Stack: [left]
                // If left is falsy, evaluate right operand
                _chunk.WriteOp(OpCode.JumpIfFalse, _previous.Line);
                int elsePos = _chunk.Write(0, _previous.Line);

                int trueIdx = _chunk.AddConstant(NovaValue.Bool(true));
                EmitPushConst(trueIdx, _previous.Line);

                _chunk.WriteOp(OpCode.Jump, _previous.Line);
                int donePos = _chunk.Write(0, _previous.Line);

                ParseExpression(1);

                _chunk.PatchJump(elsePos, _chunk.Code.Count - elsePos - 1);
                _chunk.PatchJump(donePos, _chunk.Code.Count - donePos - 1);
                break;
            }
            case TokenType.Question:
            {
                // condition is on stack (left expression). JumpIfFalse → else.
                _chunk.WriteOp(OpCode.JumpIfFalse, _previous.Line);
                int elseJump = _chunk.Write(0, _previous.Line);

                ParseExpression(1); // true branch
                Consume(TokenType.Colon, "Expect ':' in ternary expression.");

                _chunk.WriteOp(OpCode.Jump, _previous.Line);
                int endJump = _chunk.Write(0, _previous.Line);

                _chunk.PatchJump(elseJump, _chunk.Code.Count - elseJump - 1);

                ParseExpression(1); // false branch

                _chunk.PatchJump(endJump, _chunk.Code.Count - endJump - 1);
                break;
            }
            case TokenType.LeftParen:
                ParseCallArguments();
                break;
            case TokenType.Dot:
                ParseStructFieldAccess();
                break;
            case TokenType.LeftBracket:
                ParseBufferIndex();
                break;
        }
    }

    private void ParseBufferIndex()
    {
        ParseExpression(0);
        if (Match(TokenType.DotDot))
        {
            ParseExpression(0);
            Consume(TokenType.RightBracket, "Expect ']' after buffer slice end.");
            if (Match(TokenType.Equals))
            {
                ParseExpression(0);
                _chunk.WriteOp(OpCode.BufferSliceAssign, _previous.Line);
            }
            else
            {
                _chunk.WriteOp(OpCode.BufferSlice, _previous.Line);
            }
        }
        else
        {
            Consume(TokenType.RightBracket, "Expect ']' after buffer index.");
            if (Match(TokenType.Equals))
            {
                ParseExpression(0);
                _chunk.WriteOp(OpCode.BufferSet, _previous.Line);
            }
            else if (MatchCompoundAssignment(out OpCode compoundOp))
            {
                _chunk.WriteOp(OpCode.Dup2, _previous.Line);
                _chunk.WriteOp(OpCode.BufferGet, _previous.Line);
                ParseExpression(0);
                _chunk.WriteOp(compoundOp, _previous.Line);
                _chunk.WriteOp(OpCode.BufferSet, _previous.Line);
            }
            else
            {
                _chunk.WriteOp(OpCode.BufferGet, _previous.Line);
            }
        }
    }

    private void ParseStructFieldAccess()
    {
        Consume(TokenType.Identifier, "Expect field name after '.'.");
        Token fieldToken = _previous;

        int fieldIndex = -1;
        if (_lastIdentifierSlot >= 0)
        {
            int structIdx = _locals[_lastIdentifierSlot].StructIndex;
            if (structIdx >= 0)
            {
                ReadOnlySpan<char> fieldName = _source.Slice(fieldToken.Start, fieldToken.Length);
                ref readonly StructLayout layout = ref _structs[structIdx];
                for (int i = 0; i < layout.FieldCount; i++)
                {
                    ref readonly StructField sf = ref _structFields[layout.FieldStart + i];
                    if (fieldName.SequenceEqual(_source.Slice(sf.NameStart, sf.NameLength)))
                    {
                        fieldIndex = i;
                        break;
                    }
                }
            }
        }

        if (fieldIndex < 0)
            throw new Exception($"Compiler Error [Line {_previous.Line}]: Unknown field.");

        if (Match(TokenType.Equals))
        {
            ParseExpression(0);
            _chunk.WriteOp(OpCode.StructSet, _previous.Line);
            _chunk.Write((byte)fieldIndex, _previous.Line);
        }
        else if (MatchCompoundAssignment(out OpCode compoundOp))
        {
            _chunk.WriteOp(OpCode.Dup, _previous.Line);
            _chunk.WriteOp(OpCode.StructGet, _previous.Line);
            _chunk.Write((byte)fieldIndex, _previous.Line);
            ParseExpression(0);
            _chunk.WriteOp(compoundOp, _previous.Line);
            _chunk.WriteOp(OpCode.StructSet, _previous.Line);
            _chunk.Write((byte)fieldIndex, _previous.Line);
        }
        else
        {
            _chunk.WriteOp(OpCode.StructGet, _previous.Line);
            _chunk.Write((byte)fieldIndex, _previous.Line);
        }
    }

    private void ParseStructLiteral(int structIdx)
    {
        ref readonly StructLayout layout = ref _structs[structIdx];

        _chunk.WriteOp(OpCode.StructNew, _previous.Line);
        _chunk.Write((byte)layout.FieldCount, _previous.Line);

        if (_current.Type != TokenType.RightBrace)
        {
            do
            {
                Consume(TokenType.Identifier, "Expect field name in struct literal.");
                ReadOnlySpan<char> fieldName = _source.Slice(_previous.Start, _previous.Length);
                int fieldIndex = -1;
                for (int i = 0; i < layout.FieldCount; i++)
                {
                    ref readonly StructField sf = ref _structFields[layout.FieldStart + i];
                    if (fieldName.SequenceEqual(_source.Slice(sf.NameStart, sf.NameLength)))
                    {
                        fieldIndex = i;
                        break;
                    }
                }
                if (fieldIndex < 0)
                    throw new Exception(
                        $"Compiler Error [Line {_previous.Line}]: Struct '{_source.Slice(layout.NameStart, layout.NameLength).ToString()}' has no field '{fieldName.ToString()}'."
                    );

                Consume(TokenType.Equals, "Expect '=' after field name in struct literal.");

                _chunk.WriteOp(OpCode.Dup, _previous.Line);
                ParseExpression(0);
                _chunk.WriteOp(OpCode.StructSet, _previous.Line);
                _chunk.Write((byte)fieldIndex, _previous.Line);
                _chunk.WriteOp(OpCode.Pop, _previous.Line);
            } while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightBrace, "Expect '}' after struct literal.");
    }

    private void ParseCallArguments()
    {
        ReadOnlySpan<char> funcName = _source.Slice(_lastIdentifier.Start, _lastIdentifier.Length);
        int funcNameStart = _lastIdentifier.Start;
        int funcNameLength = _lastIdentifier.Length;

        // Check for struct constructor call
        for (int i = 0; i < _structCount; i++)
        {
            if (
                funcName.SequenceEqual(_source.Slice(_structs[i].NameStart, _structs[i].NameLength))
            )
            {
                Consume(TokenType.RightParen, "Expect ')' after struct constructor.");
                _chunk.WriteOp(OpCode.StructNew, _lastIdentifier.Line);
                _chunk.Write((byte)_structs[i].FieldCount, _lastIdentifier.Line);
                return;
            }
        }

        // check for bffer constructor
        if (funcName.SequenceEqual("Buffer"))
        {
            ParseExpression(0);
            Consume(TokenType.RightParen, "Expect ')' after Buffer count.");
            _chunk.WriteOp(OpCode.BufferNew, _lastIdentifier.Line);
            return;
        }

        int argumentCount = 0;
        if (_current.Type != TokenType.RightParen)
        {
            do
            {
                ParseExpression(0);
                argumentCount++;
            } while (Match(TokenType.Comma));
        }
        Consume(TokenType.RightParen, "Expect ')' after function arguments.");

        if (_symbolTable.TryGet(funcName, out SymbolEntry entry))
        {
            if (entry.Kind == SymbolKind.UserFunc)
            {
                _chunk.WriteOp(OpCode.Call, _lastIdentifier.Line);
                _chunk.Write(entry.Address, _lastIdentifier.Line);
                _chunk.Write((byte)argumentCount, _lastIdentifier.Line);
            }
            else
            {
                _chunk.WriteOp(OpCode.CallNative, _lastIdentifier.Line);
                _chunk.Write(entry.Address, _lastIdentifier.Line);
            }
        }
        else
        {
            // Forward reference: emit Call with a placeholder address and record
            // a fixup so it can be patched once the function body is compiled.
            _chunk.WriteOp(OpCode.Call, _lastIdentifier.Line);
            int addressPos = _chunk.Write(0, _lastIdentifier.Line);
            _chunk.Write((byte)argumentCount, _lastIdentifier.Line);

            _funcFixupPositions.Add(addressPos);
            _funcFixupNameStart.Add(funcNameStart);
            _funcFixupNameLength.Add(funcNameLength);
        }
    }

    private readonly int FindStruct(ReadOnlySpan<char> name)
    {
        for (int i = 0; i < _structCount; i++)
        {
            if (name.SequenceEqual(_source.Slice(_structs[i].NameStart, _structs[i].NameLength)))
                return i;
        }
        return -1;
    }

    private bool Match(TokenType type)
    {
        if (_current.Type != type)
            return false;
        Advance();
        return true;
    }

    private readonly int ResolveLocal(ReadOnlySpan<char> name)
    {
        for (int i = _localCount - 1; i >= 0; i--)
        {
            if (name.SequenceEqual(_source.Slice(_locals[i].NameStart, _locals[i].NameLength)))
                return i;
        }
        return -1;
    }

    private readonly void EmitLoadLocal(int slot, int line)
    {
        switch (slot)
        {
            case 0:
                _chunk.WriteOp(OpCode.LoadLocal_0, line);
                break;
            case 1:
                _chunk.WriteOp(OpCode.LoadLocal_1, line);
                break;
            case 2:
                _chunk.WriteOp(OpCode.LoadLocal_2, line);
                break;
            case 3:
                _chunk.WriteOp(OpCode.LoadLocal_3, line);
                break;
            default:
                _chunk.WriteOp(OpCode.LoadLocal, line);
                _chunk.Write((byte)slot, line);
                break;
        }
    }

    private readonly void EmitStoreLocal(int slot, int line)
    {
        switch (slot)
        {
            case 0:
                _chunk.WriteOp(OpCode.StoreLocal_0, line);
                break;
            case 1:
                _chunk.WriteOp(OpCode.StoreLocal_1, line);
                break;
            case 2:
                _chunk.WriteOp(OpCode.StoreLocal_2, line);
                break;
            case 3:
                _chunk.WriteOp(OpCode.StoreLocal_3, line);
                break;
            default:
                _chunk.WriteOp(OpCode.StoreLocal, line);
                _chunk.Write((byte)slot, line);
                break;
        }
    }

    private readonly void EmitStoreLocalDup(int slot, int line)
    {
        switch (slot)
        {
            case 0:
                _chunk.WriteOp(OpCode.StoreLocalDup_0, line);
                break;
            case 1:
                _chunk.WriteOp(OpCode.StoreLocalDup_1, line);
                break;
            case 2:
                _chunk.WriteOp(OpCode.StoreLocalDup_2, line);
                break;
            case 3:
                _chunk.WriteOp(OpCode.StoreLocalDup_3, line);
                break;
            default:
                _chunk.WriteOp(OpCode.StoreLocalDup, line);
                _chunk.Write((byte)slot, line);
                break;
        }
    }

    private bool MatchCompoundAssignment(out OpCode op)
    {
        if (Match(TokenType.PlusEquals))
        {
            op = OpCode.Add;
            return true;
        }
        if (Match(TokenType.MinusEquals))
        {
            op = OpCode.Sub;
            return true;
        }
        if (Match(TokenType.StarEquals))
        {
            op = OpCode.Mul;
            return true;
        }
        if (Match(TokenType.SlashEquals))
        {
            op = OpCode.Div;
            return true;
        }
        op = OpCode.Add;
        return false;
    }

    private readonly void EmitPushConst(int constantIndex, int line)
    {
        _chunk.IncrementConstantRefCount(constantIndex);
        if (constantIndex <= 255)
        {
            _chunk.WriteOp(OpCode.PushConst, line);
            _chunk.Write((byte)constantIndex, line);
        }
        else
        {
            _chunk.WriteOp(OpCode.PushConst16, line);
            _chunk.WriteU16((ushort)constantIndex, line);
        }
    }

    private static int GetPrecedence(TokenType type)
    {
        return type switch
        {
            TokenType.LeftParen or TokenType.Dot or TokenType.LeftBracket => 6,
            TokenType.Star or TokenType.Slash => 5,
            TokenType.Plus or TokenType.Minus => 4,
            TokenType.Less or TokenType.LessEqual or TokenType.Greater or TokenType.GreaterEqual =>
                3,
            TokenType.EqualsEquals or TokenType.BangEqual or TokenType.And => 2,
            TokenType.Or => 1,
            TokenType.Question => 2,
            _ => 0,
        };
    }
}
