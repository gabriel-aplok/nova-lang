namespace NovaLang.Compiler;

public enum TokenType : byte
{
    EOF,
    Identifier,
    Int,
    Float,
    String,
    Plus,
    Minus,
    Star,
    Slash,
    Equals,
    EqualsEquals,
    BangEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    Semicolon,
    Comma,
    Dot,
    Var,
    Func,
    Return,
    If,
    While,
    Struct,
    For,
    In,
    Else,
    And,
    Or,
    LeftBracket,
    RightBracket,
    PlusPlus,
    MinusMinus,
    PlusEquals,
    MinusEquals,
    StarEquals,
    SlashEquals,
    DotDot,
    Break,
    Continue,
    Question,
    Colon,
    Try,
    Catch,
    Throw,
    Finally,
}

public readonly struct Token(TokenType type, int start, int length, int line)
{
    public TokenType Type { get; } = type;
    public int Start { get; } = start;
    public int Length { get; } = length;
    public int Line { get; } = line;
}

public ref struct Lexer(string source)
{
    private readonly ReadOnlySpan<char> _source = source.AsSpan();
    private int _position = 0;
    private int _line = 1;

    public Token NextToken()
    {
        SkipWhitespace();
        if (_position >= _source.Length)
            return new Token(TokenType.EOF, _position, 0, _line);

        char c = _source[_position];
        int start = _position;

        if (char.IsLetter(c) || c == '_')
            return LexIdentifierOrKeyword(start);

        if (char.IsDigit(c))
            return LexNumber(start);

        if (c == '"' || c == '\'')
            return LexString(start);

        _position++;
        switch (c)
        {
            case '+':
                if (_position < _source.Length && _source[_position] == '+')
                {
                    _position++;
                    return new Token(TokenType.PlusPlus, start, 2, _line);
                }
                if (_position < _source.Length && _source[_position] == '=')
                {
                    _position++;
                    return new Token(TokenType.PlusEquals, start, 2, _line);
                }
                return new Token(TokenType.Plus, start, 1, _line);
            case '-':
                if (_position < _source.Length && _source[_position] == '-')
                {
                    _position++;
                    return new Token(TokenType.MinusMinus, start, 2, _line);
                }
                if (_position < _source.Length && _source[_position] == '=')
                {
                    _position++;
                    return new Token(TokenType.MinusEquals, start, 2, _line);
                }
                return new Token(TokenType.Minus, start, 1, _line);
            case '*':
                if (_position < _source.Length && _source[_position] == '=')
                {
                    _position++;
                    return new Token(TokenType.StarEquals, start, 2, _line);
                }
                return new Token(TokenType.Star, start, 1, _line);
            case '/':
                if (_position < _source.Length && _source[_position] == '/')
                {
                    _position++;
                    while (_position < _source.Length && _source[_position] != '\n')
                        _position++;
                    return NextToken();
                }
                if (_position < _source.Length && _source[_position] == '*')
                {
                    _position++;
                    while (_position < _source.Length - 1)
                    {
                        if (_source[_position] == '*' && _source[_position + 1] == '/')
                        {
                            _position += 2;
                            return NextToken();
                        }
                        if (_source[_position] == '\n')
                            _line++;
                        _position++;
                    }
                    throw new Exception(
                        $"Compiler Error [Line {_line}]: Unterminated block comment."
                    );
                }
                if (_position < _source.Length && _source[_position] == '=')
                {
                    _position++;
                    return new Token(TokenType.SlashEquals, start, 2, _line);
                }
                return new Token(TokenType.Slash, start, 1, _line);
            case '(':
                return new Token(TokenType.LeftParen, start, 1, _line);
            case ')':
                return new Token(TokenType.RightParen, start, 1, _line);
            case '{':
                return new Token(TokenType.LeftBrace, start, 1, _line);
            case '}':
                return new Token(TokenType.RightBrace, start, 1, _line);
            case ';':
                return new Token(TokenType.Semicolon, start, 1, _line);
            case '[':
                return new Token(TokenType.LeftBracket, start, 1, _line);
            case ']':
                return new Token(TokenType.RightBracket, start, 1, _line);
            case ',':
                return new Token(TokenType.Comma, start, 1, _line);
            case '.':
                if (_position < _source.Length && _source[_position] == '.')
                {
                    _position++;
                    return new Token(TokenType.DotDot, start, 2, _line);
                }
                return new Token(TokenType.Dot, start, 1, _line);
            case '=':
                if (_position < _source.Length && _source[_position] == '=')
                {
                    _position++;
                    return new Token(TokenType.EqualsEquals, start, 2, _line);
                }
                return new Token(TokenType.Equals, start, 1, _line);
            case '<':
                if (_position < _source.Length && _source[_position] == '=')
                {
                    _position++;
                    return new Token(TokenType.LessEqual, start, 2, _line);
                }
                return new Token(TokenType.Less, start, 1, _line);
            case '>':
                if (_position < _source.Length && _source[_position] == '=')
                {
                    _position++;
                    return new Token(TokenType.GreaterEqual, start, 2, _line);
                }
                return new Token(TokenType.Greater, start, 1, _line);
            case '!':
                if (_position < _source.Length && _source[_position] == '=')
                {
                    _position++;
                    return new Token(TokenType.BangEqual, start, 2, _line);
                }
                throw new Exception($"Unexpected character '!' at line {_line}");
            case '&':
                if (_position < _source.Length && _source[_position] == '&')
                {
                    _position++;
                    return new Token(TokenType.And, start, 2, _line);
                }
                throw new Exception($"Unexpected character '&' at line {_line}");
            case '|':
                if (_position < _source.Length && _source[_position] == '|')
                {
                    _position++;
                    return new Token(TokenType.Or, start, 2, _line);
                }
                throw new Exception($"Unexpected character '|' at line {_line}");
            case '?':
                return new Token(TokenType.Question, start, 1, _line);
            case ':':
                return new Token(TokenType.Colon, start, 1, _line);
            default:
                throw new Exception($"Unexpected character '{c}' at line {_line}");
        }
    }

    private void SkipWhitespace()
    {
        while (_position < _source.Length)
        {
            char c = _source[_position];
            if (c == '\n')
            {
                _line++;
                _position++;
            }
            else if (char.IsWhiteSpace(c))
            {
                _position++;
            }
            else if (c == '/' && _position + 1 < _source.Length && _source[_position + 1] == '/')
            {
                _position += 2;

                while (_position < _source.Length && _source[_position] != '\n')
                {
                    _position++;
                }
            }
            else
            {
                break;
            }
        }
    }

    private Token LexNumber(int start)
    {
        bool hasDot = false;
        while (
            _position < _source.Length
            && (
                char.IsDigit(_source[_position])
                || (
                    !hasDot
                    && _source[_position] == '.'
                    && _position + 1 < _source.Length
                    && char.IsDigit(_source[_position + 1])
                )
            )
        )
        {
            if (_source[_position] == '.')
                hasDot = true;
            _position++;
        }
        return new Token(hasDot ? TokenType.Float : TokenType.Int, start, _position - start, _line);
    }

    private Token LexIdentifierOrKeyword(int start)
    {
        while (
            _position < _source.Length
            && (char.IsLetterOrDigit(_source[_position]) || _source[_position] == '_')
        )
        {
            _position++;
        }

        ReadOnlySpan<char> text = _source.Slice(start, _position - start);
        TokenType type = text switch
        {
            "var" => TokenType.Var,
            "func" => TokenType.Func,
            "return" => TokenType.Return,
            "while" => TokenType.While,
            "if" => TokenType.If,
            "in" => TokenType.In,
            "else" => TokenType.Else,
            "for" => TokenType.For,
            "struct" => TokenType.Struct,
            "break" => TokenType.Break,
            "continue" => TokenType.Continue,
            "try" => TokenType.Try,
            "catch" => TokenType.Catch,
            "throw" => TokenType.Throw,
            "finally" => TokenType.Finally,
            _ => TokenType.Identifier,
        };
        return new Token(type, start, _position - start, _line);
    }

    private Token LexString(int start)
    {
        char delimiter = _source[_position];
        _position++;

        while (_position < _source.Length && _source[_position] != delimiter)
        {
            if (_source[_position] == '\\')
            {
                _position++;
                if (_position < _source.Length && _source[_position] == '\n')
                    _line++;
                _position++;
            }
            else
            {
                if (_source[_position] == '\n')
                    _line++;
                _position++;
            }
        }

        if (_position >= _source.Length)
        {
            throw new Exception($"Compiler Error [Line {_line}]: Unterminated string literal.");
        }

        _position++;
        return new Token(TokenType.String, start, _position - start, _line);
    }
}
