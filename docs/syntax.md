# Syntax Reference

## Comments

Single-line (`//`) and block (`/* */`) comments are supported:

```
// This is a comment
var x = 42; // inline comment

/* Block comment
   spanning multiple lines */
```

## Identifiers

Identifiers must start with a letter or underscore, followed by letters, digits, or underscores:

```
myVariable
_private
count2
```

## Keywords

| Keyword | Purpose |
|---------|---------|
| `var` | Variable declaration |
| `func` | Function declaration |
| `struct` | Struct type definition |
| `return` | Return from function |
| `if` | Conditional branch |
| `else` | Else branch |
| `while` | While loop |
| `for` | For loop / for-each |
| `in` | For-each membership |
| `break` | Break out of loop |
| `continue` | Skip to next iteration |

## Literals

```
42          // integer
3.14        // float
"hello"     // string (double quotes)
'hello'     // string (single quotes)
"line1\nline2"  // escape sequences: \n, \t, \\, \", \', \r, \0
true        // boolean
false       // boolean
null        // null (default uninitialized value)
```

## Whitespace

Whitespace (spaces, tabs, newlines) is used for separation and is otherwise insignificant. Statements are separated by semicolons or newlines.

```
var x = 42
var y = 10
```

Semicolons are optional but recommended for clarity.

## Blocks

Blocks are delimited by curly braces `{}` and contain zero or more statements:

```
if (x > 0) {
    PrintLn("positive");
}
```

## Operators

| Category | Operators | Associativity |
|----------|-----------|---------------|
| Postfix | `++` `--` `.` `()` `[]` | Left |
| Unary | `-` | Right |
| Multiplicative | `*` `/` | Left |
| Additive | `+` `-` | Left |
| Comparison | `<` `>` `<=` `>=` | Left |
| Equality | `==` `!=` | Left |
| Logical AND | `&&` | Left |
| Logical OR | `\|\|` | Left |
| Ternary | `? :` | Right |
| Assignment | `=` `+=` `-=` `*=` `/=` | Right |

## Expressions

```
// Arithmetic
x + y    x - y    x * y    x / y

// Comparison
x < y    x > y    x <= y    x >= y    x == y    x != y

// Logical (short-circuit)
x && y    x || y

// Ternary
x > 0 ? "positive" : "negative"

// Increment / Decrement (postfix)
var i = 0;
i++;
i--;

// Compound assignment
x += 5    x -= 3    x *= 2    x /= 4
```

## Struct Literals

```
var v = Vector3D { x = 10, y = 20, z = 30 };
var empty = Vector3D {};
```

## Buffer Literals

```
var b = [10, 20, 30];
PrintLn(b[0]); // 10
PrintLn(Len(b)); // 3
```
