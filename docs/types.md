# Type System

Nova is dynamically typed with 6 value types. Every value occupies exactly 8 bytes in memory using a union struct layout.

## Value Types

| Type | Description | Example |
|------|-------------|---------|
| `Null` | Uninitialized / no value | `null` |
| `Int` | 32-bit signed integer | `42`, `-7`, `0` |
| `Float` | 32-bit IEEE 754 float | `3.14`, `-0.5`, `1e10` |
| `Bool` | Boolean | `true`, `false` |
| `String` | Interned string (pool offset) | `"hello"`, `'world'` |
| `ObjectRef` | Reference to heap object | structs, buffers |

## Literals

The literal determines the type at parse time:

```
var a = 42;        // Int
var b = 3.14;      // Float
var c = "hello";   // String
var d = true;      // Bool
var e;             // Null (default)
```

## Type Coercion

Mixed-type arithmetic automatically promotes to `Float`:

```
var x = 5;       // Int
var y = 2.0;     // Float
var z = x + y;   // Float (7.0)
```

Integer-only operations remain `Int`:

```
var a = 10;
var b = 3;
var c = a + b;   // Int (13)
var d = a / b;   // Int (3, truncated)
```

## Null

Uninitialized variables default to `null`:

```
var x;
if (x == null) {
    PrintLn("x is null");
}
```

## String Concatenation

The `+` operator concatenates strings:

```
var first = "Hello";
var second = " World";
var greeting = first + " " + second;  // "Hello World"
```

Non-string operands are converted to string representation for printing.

## Object References

Structs and buffers are heap-allocated and accessed by reference:

```
var v = Vector3D();  // ObjectRef
var buf = Buffer(10); // ObjectRef
```

Comparing object references checks identity (pointer equality), not structural equality.
