# Control Flow

## If / Else If / Else

```
if (condition) {
    // do something
} else if (otherCondition) {
    // do something else
} else {
    // default
}
```

Chains of `else if` are supported to any depth. Conditions are evaluated top-to-bottom; the first matching branch executes.

```
var score = 85;

if (score >= 90) {
    PrintLn("A");
} else if (score >= 80) {
    PrintLn("B");
} else if (score >= 70) {
    PrintLn("C");
} else {
    PrintLn("D");
}
// prints "B"
```

## While Loop

```
while (condition) {
    // body
}
```

```
var i = 0;
while (i < 10) {
    PrintLn(i);
    i = i + 1;
}
```

## For Loop

```
for (init; condition; increment) {
    // body
}
```

All three parts are optional:

```
// Full form
for (var i = 0; i < 10; i = i + 1) { ... }

// No init
var j = 0;
for (; j < 5; j = j + 1) { ... }

// No increment
for (var k = 0; k < 3;) { ... }

// No init, no increment
for (; condition;) { ... }
```

The init and increment are parsed even when omitted. The compiler handles placing the increment after the body automatically.

## For-Each

Iterate over buffer elements:

```
var buf = Buffer(5);
buf[0] = 10;
buf[1] = 20;
buf[2] = 30;

for (var val in buf) {
    PrintLn(val);
}
// prints 10, 20, 30
```

For-each expands to a while loop internally. It calls `Len()` once before the loop and uses a hidden index variable.

## Break

Exit a loop early:

```
for (var i = 0; i < 100; i = i + 1) {
    if (i == 5) {
        break;
    }
    PrintLn(i);
}
// prints 0, 1, 2, 3, 4
```

## Continue

Skip to the next iteration:

```
for (var i = 0; i < 5; i = i + 1) {
    if (i == 2) {
        continue;
    }
    PrintLn(i);
}
// prints 0, 1, 3, 4
```

## Nesting

Loops can be nested to any depth. `break` and `continue` apply to the innermost loop:

```
for (var i = 0; i < 3; i = i + 1) {
    for (var j = 0; j < 3; j = j + 1) {
        if (j == 1) continue;
        PrintLn(i * 3 + j);
    }
}
```
