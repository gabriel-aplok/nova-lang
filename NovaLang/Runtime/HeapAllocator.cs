using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NovaLang.Runtime;

public unsafe class HeapAllocator
{
    private const int _initialSize = 256 * 1024;

    private byte* _buffer;
    private int _capacity;
    private int _nextFree;

    // Tracks buffer sizes: key = user-visible handle (header offset + 8)
    private readonly Dictionary<int, int> _bufferSizes = [];

    public HeapAllocator()
    {
        _capacity = _initialSize;
        _buffer = (byte*)NativeMemory.AllocZeroed((nuint)_capacity);
    }

    ~HeapAllocator()
    {
        if (_buffer != null)
            NativeMemory.Free(_buffer);
    }

    public int Alloc(int size)
    {
        int start = _nextFree;
        int needed = start + size;
        if (needed > _capacity)
        {
            while (_capacity < needed)
                _capacity *= 2;
            byte* newBuf = (byte*)NativeMemory.AllocZeroed((nuint)_capacity);
            Unsafe.CopyBlock(newBuf, _buffer, (uint)_nextFree);
            NativeMemory.Free(_buffer);
            _buffer = newBuf;
        }
        _nextFree = needed;
        return start;
    }

    public int AllocBuffer(int elementCount)
    {
        int handle = Alloc((elementCount + 1) * 8);
        *(NovaValue*)(_buffer + handle) = NovaValue.Int(elementCount);
        int userHandle = handle + 8;
        _bufferSizes[userHandle] = elementCount;
        return userHandle;
    }

    public int GetBufferLength(int userHandle)
    {
        return _bufferSizes.TryGetValue(userHandle, out int len) ? len : 0;
    }

    public byte* GetBuffer()
    {
        return _buffer;
    }

    public void Reset()
    {
        _nextFree = 0;
        _bufferSizes.Clear();
    }
}
