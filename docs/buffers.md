# Buffers

Buffers are fixed-size heap-allocated arrays. They are Nova's primary container type.

## Creation

### Buffer constructor

```
var buf = Buffer(100);  // allocate 100-element buffer
```

All elements are initialized to `null`.

### Buffer literal syntax

```
var b = [10, 20, 30];
```

Creates a buffer with three elements. Supports nested (buffers of buffers):

```
var nb = [[1, 2], [3, 4]];
PrintLn(nb[0][0]);  // 1
PrintLn(nb[1][1]);  // 4
```

## Indexing

Use square brackets to read and write elements:

```
buf[0] = 42;
buf[1] = 3.14;
buf[2] = "hello";

var val = buf[0]; // 42
```

## Bounds Checking

All buffer operations (`buf[i]`, `buf[i] = v`, `buf[a..b]`, `buf[a..b] = src`) validate indices at runtime. An out-of-bounds access throws:

```
buf[100] = 0;  // Runtime Error: Buffer index 100 out of bounds (length 10).
```

This prevents memory corruption from invalid index access.

## Length

Use `Len()` to get the number of elements:

```
var len = Len(buf); // 100
```

## Slicing

Slice a buffer to create a copy of a sub-range:

```
var buf = Buffer(10);
// ... fill buf ...

var slice = buf[2..5];  // new buffer with elements at indices 2, 3, 4
```

The slice is a copy, not a view. Modifications to the slice do not affect the original.

## Slice Assignment

Bulk-copy from one buffer into a slice of another:

```
var src = Buffer(5);
src[0] = 10;
src[1] = 20;

var dst = Buffer(10);
dst[0..2] = src;  // copies src into dst[0] and dst[1]
```

## Bulk Operations

Nova provides native functions for common buffer operations:

```
var buf = Buffer(10);

Buffer_Fill(buf, 99);       // fill all elements with 99
Buffer_Reverse(buf);         // reverse in place

var dst = Buffer(10);
Buffer_Copy(dst, buf);       // copy buf into dst
```

## Compound Assignment on Elements

Compound assignment works on individual buffer elements:

```
var buf = Buffer(5);
buf[0] = 10;
buf[0] += 5;    // buf[0] is now 15
buf[0] *= 2;    // buf[0] is now 30
```

## Example: Processing Vertex Data

```
var vertices = Buffer(1000);
var transformed = Buffer(1000);

for (var i = 0; i < 1000; i = i + 1) {
    vertices[i] = i * 0.1;
}

for (var i = 0; i < Len(vertices); i = i + 1) {
    transformed[i] = Math_Sin(vertices[i]) * 100;
}
```
