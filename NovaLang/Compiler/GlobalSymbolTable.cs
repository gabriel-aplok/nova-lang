using System.Runtime.CompilerServices;

namespace NovaLang.Compiler;

public enum SymbolKind : byte
{
    UserFunc,
    NativeFunc,
}

public struct SymbolEntry
{
    public SymbolKind Kind;
    public byte Address;
    public int NameStart;
    public int NameLength;
}

public ref struct GlobalSymbolTable
{
    private readonly ReadOnlySpan<char> _source;
    private readonly ReadOnlySpan<string> _nativeNames;
    private readonly Span<SymbolEntry> _entries;
    private int _count;

    public GlobalSymbolTable(
        ReadOnlySpan<char> source,
        ReadOnlySpan<string> nativeNames,
        Span<SymbolEntry> entries
    )
    {
        _source = source;
        _nativeNames = nativeNames;
        _entries = entries;
        _count = 0;
        for (int i = 0; i < entries.Length; i++)
            entries[i].NameStart = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Hash(ReadOnlySpan<char> name)
    {
        uint h = 2166136261;
        for (int i = 0; i < name.Length; i++)
            h = (h ^ name[i]) * 16777619;
        return h;
    }

    public void AddUserFunc(int nameStart, int nameLength, byte address)
    {
        ReadOnlySpan<char> name = _source.Slice(nameStart, nameLength);
        uint h = Hash(name);
        int mask = _entries.Length - 1;
        for (int i = 0; i < _entries.Length; i++)
        {
            int idx = (int)((h + i) & mask);
            if (_entries[idx].NameStart == -1)
            {
                _entries[idx] = new SymbolEntry
                {
                    Kind = SymbolKind.UserFunc,
                    Address = address,
                    NameStart = nameStart,
                    NameLength = nameLength,
                };
                _count++;
                return;
            }
        }
        ThrowFull();
    }

    public void AddNativeFunc(int nativeNameIndex, byte nativeIndex)
    {
        ReadOnlySpan<char> name = _nativeNames[nativeNameIndex].AsSpan();
        uint h = Hash(name);
        int mask = _entries.Length - 1;
        for (int i = 0; i < _entries.Length; i++)
        {
            int idx = (int)((h + i) & mask);
            if (_entries[idx].NameStart == -1)
            {
                _entries[idx] = new SymbolEntry
                {
                    Kind = SymbolKind.NativeFunc,
                    Address = nativeIndex,
                    NameStart = nativeNameIndex,
                    NameLength = _nativeNames[nativeNameIndex].Length,
                };
                _count++;
                return;
            }
        }
        ThrowFull();
    }

    public readonly bool TryGet(ReadOnlySpan<char> name, out SymbolEntry entry)
    {
        uint h = Hash(name);
        int mask = _entries.Length - 1;
        for (int i = 0; i < _entries.Length; i++)
        {
            int idx = (int)((h + i) & mask);
            ref SymbolEntry e = ref _entries[idx];
            if (e.NameStart == -1)
                break;

            ReadOnlySpan<char> entryName =
                e.Kind == SymbolKind.NativeFunc
                    ? _nativeNames[e.NameStart].AsSpan()
                    : _source.Slice(e.NameStart, e.NameLength);

            if (entryName.SequenceEqual(name))
            {
                entry = e;
                return true;
            }
        }
        entry = default;
        return false;
    }

    private static void ThrowFull()
    {
        throw new Exception("Global symbol table capacity exceeded.");
    }
}
