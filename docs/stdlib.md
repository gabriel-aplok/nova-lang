# Standard Library

Nova includes 17 native functions built into the VM. These are available globally without import.

## Output

### Print

```
Print(value)
```

Prints a value to stdout without a newline. Accepts any type.

```
Print("hello ");  // no newline
Print(42);
```

### PrintLn

```
PrintLn(value)
```

Prints a value to stdout with a newline.

```
PrintLn("hello");  // "hello" + newline
PrintLn(42);
```

## Math

All math functions accept `Int` or `Float` arguments. Integer arguments are promoted to float internally.

### Math\_Sqrt

```
Math_Sqrt(x)
```

Square root of `x`.

```
Math_Sqrt(25)  // 5
Math_Sqrt(2)   // 1.4142...
```

### Math\_Abs

```
Math_Abs(x)
```

Absolute value of `x`.

```
Math_Abs(-7)   // 7
Math_Abs(3)    // 3
```

### Math\_Sin

```
Math_Sin(x)
```

Sine of `x` (radians).

### Math\_Cos

```
Math_Cos(x)
```

Cosine of `x` (radians).

### Math\_Pow

```
Math_Pow(base, exp)
```

`base` raised to the power `exp`.

```
Math_Pow(2, 10)  // 1024
Math_Pow(3, 2)   // 9
```

### Math\_Min

```
Math_Min(a, b)
```

Returns the smaller of `a` and `b`.

### Math\_Max

```
Math_Max(a, b)
```

Returns the larger of `a` and `b`.

### Math\_Floor

```
Math_Floor(x)
```

Rounds `x` down to the nearest integer.

### Math\_Ceil

```
Math_Ceil(x)
```

Rounds `x` up to the nearest integer.

### Math\_Clamp

```
Math_Clamp(value, min, max)
```

Clamps `value` to the range `[min, max]`.

```
Math_Clamp(15, 0, 10)  // 10
Math_Clamp(-5, 0, 10)  // 0
Math_Clamp(5, 0, 10)   // 5
```

### Math\_Lerp

```
Math_Lerp(a, b, t)
```

Linear interpolation between `a` and `b` at factor `t`.

```
Math_Lerp(0, 100, 0.5)  // 50
Math_Lerp(10, 20, 0.75)  // 17.5
```

## Buffer Operations

### Len

```
Len(buffer)
```

Returns the number of elements in a buffer.

```
var buf = Buffer(100);
Len(buf)  // 100
```

### Buffer\_Fill

```
Buffer_Fill(buffer, value)
```

Fills all elements of a buffer with `value`.

```
var buf = Buffer(10);
Buffer_Fill(buf, 42);  // all elements are 42
```

### Buffer\_Copy

```
Buffer_Copy(destination, source)
```

Copies all elements from `source` into `destination`. Buffers must be the same length.

```
var src = Buffer(5);
var dst = Buffer(5);
Buffer_Copy(dst, src);
```

### Buffer\_Reverse

```
Buffer_Reverse(buffer)
```

Reverses the elements of a buffer in place.

```
var buf = Buffer(3);
buf[0] = 1; buf[1] = 2; buf[2] = 3;
Buffer_Reverse(buf);
// buf[0]=3, buf[1]=2, buf[2]=1
```
