using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace NovaLang.Runtime;

public unsafe class StringPool
{
    private byte* _data;
    private int _dataSize;
    private int _capacity;

    public StringPool(int initialCapacity = 256 * 1024)
    {
        _capacity = initialCapacity;
        _data = (byte*)NativeMemory.AllocZeroed((nuint)_capacity);
        _dataSize = 0;
    }

    ~StringPool()
    {
        if (_data != null)
            NativeMemory.Free(_data);
    }

    public byte* GetBuffer()
    {
        return _data;
    }

    public int DataSize => _dataSize;

    public int Intern(scoped ReadOnlySpan<char> str)
    {
        int maxBytes = Encoding.UTF8.GetMaxByteCount(str.Length);
        Span<byte> utf8Buf = stackalloc byte[maxBytes];
        int actualLen;
        fixed (char* pStr = str)
        {
            fixed (byte* pBuf = utf8Buf)
            {
                actualLen = Encoding.UTF8.GetBytes(pStr, str.Length, pBuf, maxBytes);
            }
        }

        int readOffset = 0;
        while (readOffset < _dataSize)
        {
            int existingLen = PeekInt(_data, readOffset);
            if (existingLen == actualLen)
            {
                bool match = true;
                for (int i = 0; i < actualLen; i++)
                {
                    if (*(_data + readOffset + 4 + i) != utf8Buf[i])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return readOffset;
            }
            readOffset += 4 + existingLen;
        }

        int needed = _dataSize + 4 + actualLen;
        EnsureCapacity(needed);

        int result = _dataSize;
        WriteInt(_data, _dataSize, actualLen);
        for (int i = 0; i < actualLen; i++)
            *(_data + _dataSize + 4 + i) = utf8Buf[i];
        _dataSize = needed;
        return result;
    }

    public int InternFromBytes(byte* utf8, int length)
    {
        int offset = 0;
        while (offset < _dataSize)
        {
            int existingLen = PeekInt(_data, offset);
            if (existingLen == length)
            {
                bool match = true;
                for (int i = 0; i < length; i++)
                {
                    if (*(_data + offset + 4 + i) != utf8[i])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return offset;
            }
            offset += 4 + existingLen;
        }

        int needed = _dataSize + 4 + length;
        EnsureCapacity(needed);

        int result = _dataSize;
        WriteInt(_data, _dataSize, length);
        for (int i = 0; i < length; i++)
            *(_data + _dataSize + 4 + i) = utf8[i];
        _dataSize = needed;
        return result;
    }

    public string GetString(int offset)
    {
        int len = PeekInt(_data, offset);
        return Encoding.UTF8.GetString(_data + offset + 4, len);
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _capacity)
            return;
        while (_capacity < needed)
            _capacity *= 2;
        byte* newBuf = (byte*)NativeMemory.AllocZeroed((nuint)_capacity);
        Unsafe.CopyBlock(newBuf, _data, (uint)_dataSize);
        NativeMemory.Free(_data);
        _data = newBuf;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadLength(byte* basePtr, int offset)
    {
        return *(int*)(basePtr + offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte* GetData(byte* basePtr, int offset)
    {
        return basePtr + offset + 4;
    }

    private static int PeekInt(byte* buf, int offset)
    {
        return *(int*)(buf + offset);
    }

    private static void WriteInt(byte* buf, int offset, int value)
    {
        *(int*)(buf + offset) = value;
    }
}
