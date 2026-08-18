using System.Runtime.InteropServices;

namespace NovaLang.Runtime;

public enum ValueType : byte
{
    Null,
    Int,
    Float,
    Bool,
    ObjectRef,
    String,
}

[StructLayout(LayoutKind.Explicit)]
public struct NovaValue
{
    [FieldOffset(0)]
    public ValueType Type;

    [FieldOffset(4)]
    public int AsInt;

    [FieldOffset(4)]
    public float AsFloat;

    [FieldOffset(4)]
    public bool AsBool;

    [FieldOffset(4)]
    public int ObjectId;

    public static NovaValue Int(int val)
    {
        return new() { Type = ValueType.Int, AsInt = val };
    }

    public static NovaValue Float(float val)
    {
        return new() { Type = ValueType.Float, AsFloat = val };
    }

    public static NovaValue Bool(bool val)
    {
        return new() { Type = ValueType.Bool, AsBool = val };
    }

    public static NovaValue String(int poolOffset)
    {
        return new() { Type = ValueType.String, ObjectId = poolOffset };
    }
}
