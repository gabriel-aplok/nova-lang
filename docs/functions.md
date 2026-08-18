# Functions

## Declaration

Functions are declared with the `func` keyword:

```
func FunctionName(param1, param2) {
    return param1 + param2;
}
```

Parameters are positional and untyped. There are no type annotations.

## Calling

Functions are called with parentheses:

```
var result = Add(10, 20);
PrintLn(result); // 30
```

## Return Values

Use `return` to return a value:

```
func Square(n) {
    return n * n;
}
```

A function without an explicit `return` returns `null`.

## Recursion and Forward References

Functions may call themselves (recursion) and may call functions declared later in the file (forward references). The compiler emits placeholder call targets and patches them after all function bodies are compiled:

```
func Even(n) { return n == 0 ? 1 : Odd(n - 1); }
func Odd(n)  { return n == 0 ? 0 : Even(n - 1); }

func Fib(n) {
    if (n <= 1) { return n; }
    return Fib(n - 1) + Fib(n - 2);
}

PrintLn(Fib(10));  // 55
```

A runaway recursion is stopped at the VM's call-depth limit with a runtime error: "Call stack depth exceeded (max N)." The limit is configurable via the VM constructor (default 4096 nested calls / 65536 value slots).

## Functions as Values

Currently, Nova does not support first-class functions. Functions cannot be passed as arguments or stored in variables.

## Native Functions

Native functions are implemented in C# and registered at runtime. They are called the same way as user-defined functions:

```
PrintLn("Hello");      // native
var s = Math_Sqrt(4);  // native
var r = Add(1, 2);     // user-defined
```

See [Standard Library](stdlib.md) for the full list of native functions.

## Example

```
func Fib(n) {
    if (n <= 1) {
        return n;
    }
    return Fib(n - 1) + Fib(n - 2);
}

PrintLn(Fib(10)); // 55
```
