# Exceptions

Nova supports `try`/`catch`/`throw` for runtime error handling.

## Throwing

Use `throw` to raise a value. Any Nova value can be thrown (strings are most common as messages):

```
throw "something went wrong";
throw 42;
```

## Catching

Wrap risky code in `try` and handle it with `catch`:

```
try {
    var danger = MayFail();
} catch (e) {
    PrintLn("Handled: " + e);
}
```

The optional `catch (e)` binding exposes the thrown value. A bare `catch` without a parenthesized variable is also allowed.

```
try {
    throw "boom";
} catch (e) {
    PrintLn("caught: " + e);   // caught: boom
}
```

## Behavior

- When code inside the `try` body (or any function it calls) throws, the runtime unwinds to the nearest active handler and jumps to its `catch` block.
- Nested `try`/`catch` blocks work; an inner handler can re-`throw` to be caught by an outer handler.
- A `throw` with no active handler raises a runtime error to the host: "Unhandled exception: &lt;value&gt;".
- A `try` that completes without throwing skips the `catch` block.

## Example

```
func SafeDivide(a, b) {
    if (b == 0) {
        throw "division by zero";
    }
    return a / b;
}

try {
    PrintLn(SafeDivide(10, 2));   // 5
    PrintLn(SafeDivide(5, 0));    // throws
} catch (e) {
    PrintLn("error: " + e);       // error: division by zero
}
```

Thrown runtime guard errors (buffer bounds, call-stack depth, uninitialized reads) surface as host runtime exceptions and are not, by default, caught by Nova `catch`.
