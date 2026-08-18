# Structs

Structs are user-defined heap-allocated types with named fields.

## Declaration

```
struct Vector3D {
    var x;
    var y;
    var z;
}
```

Fields are declared with `var` but do not have initializers. All fields default to `null`.

## Construction

### Constructor syntax
Call the struct name as a function with no arguments:

```
var v = Vector3D();
```

This allocates a struct on the heap and returns an `ObjectRef`.

### Struct literal syntax
Initialize fields inline using `TypeName { field = expr, ... }`:

```
var v = Vector3D { x = 10, y = 20, z = 30 };
```

Fields can be set in any order. Unset fields default to `null` (displayed as `0`):

```
var partial = Vector3D { z = 99 };
PrintLn(partial.x); // 0 (unset)
PrintLn(partial.z); // 99
```

An empty literal creates a zero-initialized struct:

```
var empty = Vector3D {};
```

## Field Access

Use dot notation to read and write fields:

```
v.x = 10;
v.y = 20;
v.z = 30;

var val = v.x; // 10
```

Fields support compound assignment:

```
v.x += 5;    // v.x is now 15
v.y *= 2;    // v.y is now 40
```

## How It Works

Fields are stored as an array of 8-byte `NovaValue` slots on the heap. Field names are resolved to integer indices at compile time, so field access at runtime is simple offset arithmetic: `handle + fieldIndex * 8`.

## Structs as Function Arguments

Structs are passed by reference (as `ObjectRef`):

```
func Translate(v, dx, dy, dz) {
    v.x += dx;
    v.y += dy;
    v.z += dz;
}

var pos = Vector3D();
pos.x = 10;
pos.y = 20;
pos.z = 30;

Translate(pos, 5, 5, 5);
PrintLn(pos.x); // 15
```

## Returning Structs

Functions can return structs:

```
func MakeVector(x, y, z) {
    var v = Vector3D();
    v.x = x;
    v.y = y;
    v.z = z;
    return v;
}

var v = MakeVector(1, 2, 3);
```

## Nested Structs

Structs cannot currently contain other struct fields directly. Each field is a `NovaValue` slot, which can hold an `ObjectRef` to another struct:

```
struct Transform {
    var position;
    var rotation;
}

var t = Transform();
t.position = MakeVector(0, 0, 0);
t.rotation = MakeVector(0, 0, 0);
```
