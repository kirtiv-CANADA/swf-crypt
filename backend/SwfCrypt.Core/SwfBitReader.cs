using System.Buffers.Binary;

namespace SwfCrypt.Core;

internal ref struct SwfBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _byteOffset;
    private int _bitMask; // 0 means byte-aligned; 1..7 means bits remaining in current byte

    public SwfBitReader(ReadOnlySpan<byte> data, int startOffset = 0)
    {
        if (startOffset < 0 || startOffset > data.Length)
            throw new ArgumentOutOfRangeException(nameof(startOffset));

        _data = data;
        _byteOffset = startOffset;
        _bitMask = 0;
    }

    public readonly int ByteOffset => _byteOffset;
    public readonly bool IsByteAligned => _bitMask == 0;
    public readonly int RemainingBytes => _data.Length - _byteOffset;

    public void AlignToByte()
    {
        if (_bitMask != 0)
        {
            _bitMask = 0;
            if (_byteOffset < _data.Length)
                _byteOffset++;
        }
    }

    public bool TryReadUnsignedBits(int bitCount, out uint value)
    {
        value = 0;
        if (bitCount < 0 || bitCount > 32)
            return false;

        if (bitCount == 0)
            return true;

        uint result = 0;
        int remainingBits = bitCount;

        while (remainingBits > 0)
        {
            if (_byteOffset >= _data.Length)
                return false;

            if (_bitMask == 0)
                _bitMask = 8;

            int bitsToTake = Math.Min(remainingBits, _bitMask);
            int shiftRight = _bitMask - bitsToTake;
            int mask = ((1 << bitsToTake) - 1) << shiftRight;
            uint extracted = (uint)((_data[_byteOffset] & mask) >> shiftRight);

            result = (result << bitsToTake) | extracted;
            remainingBits -= bitsToTake;
            _bitMask -= bitsToTake;

            if (_bitMask == 0)
                _byteOffset++;
        }

        value = result;
        return true;
    }

    public bool TryReadSignedBits(int bitCount, out int value)
    {
        value = 0;
        if (bitCount <= 0 || bitCount > 32)
            return false;

        if (!TryReadUnsignedBits(bitCount, out uint raw))
            return false;

        if (bitCount < 32)
        {
            uint signBit = 1u << (bitCount - 1);
            if ((raw & signBit) != 0)
            {
                uint signExtension = ~0u << bitCount;
                raw |= signExtension;
            }
        }

        value = unchecked((int)raw);
        return true;
    }

    public bool TryReadRect(out SwfRect rect)
    {
        rect = default;
        AlignToByte();

        if (!TryReadUnsignedBits(5, out uint nBitsRaw))
            return false;

        int nBits = (int)nBitsRaw;

        if (!TryReadSignedBits(nBits, out int xMin) ||
            !TryReadSignedBits(nBits, out int xMax) ||
            !TryReadSignedBits(nBits, out int yMin) ||
            !TryReadSignedBits(nBits, out int yMax))
        {
            return false;
        }

        AlignToByte();
        rect = new SwfRect(xMin, xMax, yMin, yMax);
        return true;
    }

    public bool TryReadFixed8_8(out double value)
    {
        value = 0;
        AlignToByte();

        if (RemainingBytes < 2)
            return false;

        ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(_data[_byteOffset..]);
        _byteOffset += 2;

        // SWF stores 8.8 fixed-point frame rate in little-endian: low byte = fraction, high byte = integer
        byte fractional = (byte)(raw & 0xFF);
        byte whole = (byte)((raw >> 8) & 0xFF);
        value = whole + (fractional / 256.0);
        return true;
    }

    public bool TryReadUInt16LittleEndian(out ushort value)
    {
        value = 0;
        AlignToByte();

        if (RemainingBytes < 2)
            return false;

        value = BinaryPrimitives.ReadUInt16LittleEndian(_data[_byteOffset..]);
        _byteOffset += 2;
        return true;
    }
}
