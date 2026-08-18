# Variables

## Declaration

Variables are declared with the `var` keyword:

```
var x = 42;        // initialized integer
var y = 3.14;      // initialized float
var name = "Nova"; // initialized string
var z;             // uninitialized (defaults to null)
```

## Assignment

Use the `=` operator to assign or reassign:

```
var x = 10;
x = 20;           // reassign
x = "now a string"; // types can change (dynamic)
```

## Compound Assignment

Nova supports compound assignment operators:

```
var x = 10;
x += 5;    // x is 15
x -= 3;    // x is 12
x *= 2;    // x is 24
x /= 4;    // x is 6
```

These work on both variables and struct fields / buffer elements:

```
var v = Vector3D();
v.x += 5;
v.y *= 2;

var buf = Buffer(10);
buf[0] += 100;
buf[1] *= 3;
```

## Scope

Variables are function-scoped. There are no block-level scopes -- variables declared inside `if` or `for` blocks are visible for the rest of the function:

```
func Example() {
    if (true) {
        var x = 42;
    }
    PrintLn(x); // still accessible
}
```

## Local Variables

The compiler tracks up to 8192 local variables per function. Locals are stored on the VM's evaluation stack at offsets relative to the function's frame base.

For performance, the first 4 local variables (slots 0-3) use optimized single-byte opcodes (`LoadLocal_0` through `LoadLocal_3`, `StoreLocal_0` through `StoreLocal_3`) that don't need an index operand.

## Global Variables

Variables declared at the top level of a script are global. They are accessible from any function within the same script:

```
var globalValue = 100;

func ReadGlobal() {
    PrintLn(globalValue); // 100
}

ReadGlobal();
```
