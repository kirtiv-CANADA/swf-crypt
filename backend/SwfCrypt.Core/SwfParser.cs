using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SwfCrypt.Core;

public static class SwfParser
{
    private const long MaxDecompressedSize = 64L * 1024 * 1024; // 64 MB safety cap
    private const ushort TagEnd = 0;
    private const ushort TagDoAction = 12;
    private const ushort TagSetBackgroundColor = 9;
    private const ushort TagDoInitAction = 59;
    private const ushort TagFileAttributes = 69;
    private const ushort TagDoAbc = 72;
    private const ushort TagDoAbc2 = 82;

    public static SwfParseResult Parse(ReadOnlySpan<byte> data)
    {
        try
        {
            if (data.Length < 8)
            {
                return SwfParseResult.Fail("INVALID_HEADER", "SWF header is incomplete.");
            }

            byte b0 = data[0];
            byte b1 = data[1];
            byte b2 = data[2];
            byte version = data[3];

            if (b1 != (byte)'W' || b2 != (byte)'S')
            {
                return SwfParseResult.Fail("INVALID_SIGNATURE", "Unsupported SWF signature.");
            }

            SwfCompression compression;
            if (b0 == (byte)'F')
            {
                compression = SwfCompression.None;
            }
            else if (b0 == (byte)'C')
            {
                compression = SwfCompression.Zlib;
            }
            else if (b0 == (byte)'Z')
            {
                return SwfParseResult.Fail("UNSUPPORTED_COMPRESSION", "LZMA (ZWS) compression is not supported.");
            }
            else
            {
                return SwfParseResult.Fail("INVALID_SIGNATURE", "Unsupported SWF signature.");
            }

            uint declaredFileLength = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
            ReadOnlySpan<byte> bodySpan = data.Slice(8);
            byte[] bodyBytes;

            if (compression == SwfCompression.None)
            {
                if (declaredFileLength < 8 || data.Length < declaredFileLength)
                {
                    return SwfParseResult.Fail("TRUNCATED_DATA", "Uncompressed SWF data is truncated.");
                }

                int actualBodyLength = (int)Math.Min((long)declaredFileLength - 8, data.Length - 8);
                bodyBytes = bodySpan.Slice(0, actualBodyLength).ToArray();
            }
            else
            {
                if (declaredFileLength < 8 || declaredFileLength > MaxDecompressedSize + 8)
                {
                    return SwfParseResult.Fail("INVALID_LENGTH", "Declared uncompressed SWF length is invalid or too large.");
                }

                int expectedBodyLength = (int)(declaredFileLength - 8);
                bodyBytes = DecompressZlib(bodySpan, expectedBodyLength);
                if (bodyBytes.Length == 0 && bodySpan.Length > 0)
                {
                    return SwfParseResult.Fail("CORRUPT_ZLIB", "Failed to decompress zlib SWF body.");
                }
            }

            if (bodyBytes.Length == 0)
            {
                return SwfParseResult.Fail("EMPTY_BODY", "SWF body is empty.");
            }

            // Skip RECT bit-field header + FrameRate (2 bytes fixed point) + FrameCount (2 bytes)
            int rectOffset = SkipRect(bodyBytes);
            if (rectOffset < 0 || rectOffset + 4 > bodyBytes.Length)
            {
                return SwfParseResult.Fail("TRUNCATED_HEADER", "SWF frame header is incomplete.");
            }

            ushort rawFrameRate = BinaryPrimitives.ReadUInt16LittleEndian(bodyBytes.AsSpan(rectOffset, 2));
            ushort frameCount = BinaryPrimitives.ReadUInt16LittleEndian(bodyBytes.AsSpan(rectOffset + 2, 2));
            double frameRate = rawFrameRate / 256.0;
            int offset = rectOffset + 4;

            bool hasAs1OrAs2 = false;
            bool hasAs3 = false;
            int tagCount = 0;
            List<string> warnings = [];

            while (offset < bodyBytes.Length)
            {
                if (offset + 2 > bodyBytes.Length)
                {
                    return SwfParseResult.Fail("TRUNCATED_TAG_HEADER", "Incomplete SWF tag header.");
                }

                ushort tagCodeAndLength = BinaryPrimitives.ReadUInt16LittleEndian(bodyBytes.AsSpan(offset, 2));
                offset += 2;

                ushort tagType = (ushort)(tagCodeAndLength >> 6);
                long tagLength = tagCodeAndLength & 0x3F;

                if (tagLength == 0x3F)
                {
                    if (offset + 4 > bodyBytes.Length)
                    {
                        return SwfParseResult.Fail("TRUNCATED_LONG_TAG_LENGTH", "Incomplete long tag length.");
                    }

                    tagLength = BinaryPrimitives.ReadUInt32LittleEndian(bodyBytes.AsSpan(offset, 4));
                    offset += 4;
                }

                if (tagLength < 0 || tagLength > bodyBytes.Length - offset)
                {
                    return SwfParseResult.Fail("TRUNCATED_TAG_PAYLOAD", "Tag declares more bytes than available.");
                }

                int payloadOffset = offset;
                int payloadLength = (int)tagLength;
                offset += payloadLength;
                tagCount++;

                if (tagType == TagEnd)
                {
                    break;
                }

                if (tagType == TagDoAction || tagType == TagDoInitAction)
                {
                    hasAs1OrAs2 = true;
                }
                else if (tagType == TagDoAbc || tagType == TagDoAbc2)
                {
                    hasAs3 = true;
                }
                else if (tagType == TagFileAttributes && payloadLength >= 1)
                {
                    byte flags = bodyBytes[payloadOffset];
                    bool actionScript3Flag = (flags & 0x08) != 0;
                    if (actionScript3Flag)
                    {
                        hasAs3 = true;
                    }
                }
                else if (tagType == TagSetBackgroundColor && payloadLength < 3)
                {
                    warnings.Add("SetBackgroundColor tag has fewer than 3 bytes.");
                }
            }

            ActionScriptKind scriptKind = ClassifyActionScript(version, hasAs1OrAs2, hasAs3);
            var autopsy = new SwfAutopsy(
                Version: version,
                Compression: compression,
                ActionScriptKind: scriptKind,
                FrameRate: frameRate,
                FrameCount: frameCount,
                TotalTagCount: tagCount,
                Warnings: warnings);

            return SwfParseResult.Ok(autopsy);
        }
        catch (Exception ex)
        {
            return SwfParseResult.Fail("PARSE_ERROR", ex.Message);
        }
    }

    private static ActionScriptKind ClassifyActionScript(byte version, bool hasAs1OrAs2, bool hasAs3)
    {
        if (hasAs1OrAs2 && hasAs3)
        {
            return ActionScriptKind.Mixed;
        }

        if (hasAs3)
        {
            return ActionScriptKind.AS3;
        }

        if (hasAs1OrAs2)
        {
            return version <= 6 ? ActionScriptKind.AS1 : ActionScriptKind.AS2;
        }

        return ActionScriptKind.None;
    }

    private static int SkipRect(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return -1;
        }

        int nBits = (data[0] >> 3) & 0x1F;
        int totalBits = 5 + (4 * nBits);
        int byteCount = (totalBits + 7) / 8;
        return byteCount <= data.Length ? byteCount : -1;
    }

    private static byte[] DecompressZlib(ReadOnlySpan<byte> compressed, int expectedLength)
    {
        try
        {
            using var input = new MemoryStream(compressed.ToArray());
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(Math.Min(Math.Max(expectedLength, 0), 1024 * 1024));

            byte[] buffer = new byte[8192];
            long totalRead = 0;
            int read;

            while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
            {
                totalRead += read;
                if (totalRead > MaxDecompressedSize)
                {
                    return [];
                }

                output.Write(buffer, 0, read);
            }

            if (expectedLength > 0 && output.Length != expectedLength)
            {
                return [];
            }

            return output.ToArray();
        }
        catch
        {
            return [];
        }
    }
}
