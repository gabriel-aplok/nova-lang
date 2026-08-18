# Expressions

## Operators

### Arithmetic

| Operator | Description | Example |
|----------|-------------|---------|
| `+` | Addition / string concatenation | `5 + 3` → `8` |
| `-` | Subtraction | `5 - 3` → `2` |
| `*` | Multiplication | `5 * 3` → `15` |
| `/` | Division (truncated for ints) | `7 / 2` → `3` |
| `-expr` | Unary minus | `-5` |

### Comparison

| Operator | Description |
|----------|-------------|
| `==` | Equal |
| `!=` | Not equal |
| `<` | Less than |
| `<=` | Less than or equal |
| `>` | Greater than |
| `>=` | Greater than or equal |

### Logical

| Operator | Description |
|----------|-------------|
| `&&` | Logical AND (short-circuit) |
| `\|\|` | Logical OR (short-circuit) |

### Assignment

| Operator | Description |
|----------|-------------|
| `=` | Assignment |
| `+=` | Add-assign |
| `-=` | Subtract-assign |
| `*=` | Multiply-assign |
| `/=` | Divide-assign |

### Access

| Operator | Description | Example |
|----------|-------------|---------|
| `()` | Function call | `Add(1, 2)` |
| `.` | Struct field access | `v.x` |
| `[]` | Buffer index / slice | `buf[0]`, `buf[1..5]` |
| `..` | Range (for slicing) | `1..10` |

## Precedence

Operators are evaluated in this order (highest first):

| Level | Operators |
|-------|-----------|
| 6 | `()` `.` `[]` (call, access, index) |
| 5 | `*` `/` (multiply, divide) |
| 4 | `+` `-` (add, subtract) |
| 3 | `<` `<=` `>` `>=` (comparison) |
| 2 | `==` `!=` `&&` (equality, logical AND) |
| 1 | `\|\|` (logical OR) |
| 0 | Everything else |

## Short-Circuit Evaluation

`&&` and `||` short-circuit: if the left operand determines the result, the right operand is not evaluated.

```
var x = 0;
if (x != 0 && 10 / x > 2) {
    // safe: division only evaluated if x != 0
}
```

## String Concatenation

The `+` operator concatenates strings:

```
var greeting = "Hello" + " " + "World";
```

## Parentheses

Use parentheses to override precedence:

```
var result = (2 + 3) * 4;  // 20, not 14
```

## Type Rules

- `Int + Int` → `Int`
- `Float + Float` → `Float`
- `Int + Float` → `Float` (promotion)
- `String + String` → `String`
- `String + anything` → `String` (for printing)
- Comparison operators always return `Bool`
