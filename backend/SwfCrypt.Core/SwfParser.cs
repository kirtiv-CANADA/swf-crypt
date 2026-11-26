using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SwfCrypt.Core;

public sealed class SwfParserOptions
{
    public int MaxInputBytes { get; init; } = 32 * 1024 * 1024;          // 32 MB
    public int MaxDecompressedBytes { get; init; } = 64 * 1024 * 1024;    // 64 MB
    public int MaxTagCount { get; init; } = 250_000;
}

public static class SwfParser
{
    private const ushort TagEnd = 0;
    private const ushort TagDoAction = 12;
    private const ushort TagDoInitAction = 59;
    private const ushort TagSymbolClass = 76;
    private const ushort TagDoAbc = 82;

    public static SwfParseResult Parse(ReadOnlySpan<byte> swfBytes, SwfParserOptions? options = null)
    {
        options ??= new SwfParserOptions();

        if (swfBytes.Length < 8)
            return SwfParseResult.Failure("INVALID_HEADER", "SWF payload is shorter than the 8-byte header.");

        if (swfBytes.Length > options.MaxInputBytes)
            return SwfParseResult.Failure("INPUT_TOO_LARGE", $"SWF input exceeds limit of {options.MaxInputBytes} bytes.");

        string signature = Encoding.ASCII.GetString(swfBytes[..3]);
        SwfCompression compression = signature switch
        {
            "FWS" => SwfCompression.Uncompressed,
            "CWS" => SwfCompression.Zlib,
            "ZWS" => SwfCompression.Lzma,
            _ => (SwfCompression)255
        };

        if ((byte)compression == 255)
            return SwfParseResult.Failure("UNSUPPORTED_SIGNATURE", $"Unsupported SWF signature '{signature}'.");

        byte version = swfBytes[3];
        uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(swfBytes[4..8]);

        if (declaredLength < 8 || declaredLength > (uint)options.MaxDecompressedBytes)
            return SwfParseResult.Failure("INVALID_DECLARED_LENGTH", $"Declared SWF length {declaredLength} is out of allowed bounds.");

        byte[] bodyBytes;
        try
        {
            bodyBytes = ExtractBody(swfBytes, compression, (int)declaredLength, options.MaxDecompressedBytes);
        }
        catch (InvalidDataException ex)
        {
            return SwfParseResult.Failure("DECOMPRESSION_FAILED", $"Failed to decompress {compression} SWF body: {ex.Message}");
        }
        catch (InternalBufferOverflowException ex)
        {
            return SwfParseResult.Failure("DECOMPRESSION_LIMIT_EXCEEDED", ex.Message);
        }

        // Assemble normalized full buffer [8-byte header + uncompressed body] so bit offsets match SWF spec
        byte[] fullSwf = new byte[8 + bodyBytes.Length];
        swfBytes[..8].CopyTo(fullSwf);
        bodyBytes.CopyTo(fullSwf.AsSpan(8));

        var reader = new SwfBitReader(fullSwf, startOffset: 8);

        if (!reader.TryReadRect(out SwfRect stageBounds))
            return SwfParseResult.Failure("INVALID_RECT", "Unable to decode stage RECT from SWF header.");

        if (!reader.TryReadFixed8_8(out double frameRate))
            return SwfParseResult.Failure("INVALID_FRAME_RATE", "Unable to read SWF frame rate.");

        if (!reader.TryReadUInt16LittleEndian(out ushort frameCount))
            return SwfParseResult.Failure("INVALID_FRAME_COUNT", "Unable to read SWF frame count.");

        var tagStats = new Dictionary<ushort, (int Count, long Bytes)>();
        int totalTags = 0;
        bool hasAs12 = false;
        bool hasAs3 = false;

        while (reader.RemainingBytes >= 2)
        {
            if (totalTags >= options.MaxTagCount)
                return SwfParseResult.Failure("TAG_LIMIT_EXCEEDED", $"Tag count exceeded limit of {options.MaxTagCount}.");

            if (!reader.TryReadUInt16LittleEndian(out ushort recordHeader))
                break;

            ushort tagCode = (ushort)(recordHeader >> 6);
            uint tagLength = (uint)(recordHeader & 0x3F);

            if (tagLength == 0x3F)
            {
                if (reader.RemainingBytes < 4)
                    return SwfParseResult.Failure("TRUNCATED_LONG_TAG", $"Truncated long tag length for tag {tagCode}.");

                tagLength = BinaryPrimitives.ReadUInt32LittleEndian(fullSwf.AsSpan(reader.ByteOffset, 4));
                reader = new SwfBitReader(fullSwf, reader.ByteOffset + 4);
            }

            if (tagLength > (uint)reader.RemainingBytes)
                return SwfParseResult.Failure("TRUNCATED_TAG_PAYLOAD", $"Tag {tagCode} declares {tagLength} bytes but only {reader.RemainingBytes} remain.");

            reader = new SwfBitReader(fullSwf, reader.ByteOffset + (int)tagLength);
            totalTags++;

            if (tagStats.TryGetValue(tagCode, out var existing))
                tagStats[tagCode] = (existing.Count + 1, existing.Bytes + tagLength);
            else
                tagStats[tagCode] = (1, tagLength);

            if (tagCode is TagDoAction or TagDoInitAction)
                hasAs12 = true;
            else if (tagCode is TagDoAbc or TagSymbolClass)
                hasAs3 = true;

            if (tagCode == TagEnd)
                break;
        }

        ActionScriptKind asKind = (hasAs12, hasAs3) switch
        {
            (true, true) => ActionScriptKind.Mixed,
            (true, false) => ActionScriptKind.ActionScript1_2,
            (false, true) => ActionScriptKind.ActionScript3,
            _ => ActionScriptKind.None
        };

        List<SwfTagSummary> summaries = tagStats
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new SwfTagSummary(kvp.Key, kvp.Value.Count, kvp.Value.Bytes))
            .ToList();

        var autopsy = new SwfAutopsyResult(
            Compression: compression,
            Version: version,
            DeclaredLength: declaredLength,
            ActualPayloadLength: fullSwf.Length,
            StageBounds: stageBounds,
            FrameRate: frameRate,
            FrameCount: frameCount,
            ActionScriptKind: asKind,
            TotalTagCount: totalTags,
            Tags: summaries);

        return SwfParseResult.Success(autopsy);
    }

    private static byte[] ExtractBody(
        ReadOnlySpan<byte> swfBytes,
        SwfCompression compression,
        int declaredLength,
        int maxDecompressedBytes)
    {
        int maxBodyBytes = Math.Min(maxDecompressedBytes, declaredLength) - 8;
        if (maxBodyBytes < 0)
            maxBodyBytes = 0;

        ReadOnlySpan<byte> compressedSpan = swfBytes[8..];

        if (compression == SwfCompression.Uncompressed)
        {
            int copyLen = Math.Min(compressedSpan.Length, maxBodyBytes);
            return compressedSpan[..copyLen].ToArray();
        }

        if (compression == SwfCompression.Zlib)
        {
            using var input = new MemoryStream(compressedSpan.ToArray());
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            return ReadUpToLimit(zlib, maxBodyBytes, maxDecompressedBytes);
        }

        // ZWS (LZMA): standard SWF ZWS after 8-byte header has 5-byte LZMA props + 8-byte uncompressed size (often followed by LZMA stream)
        if (compressedSpan.Length < 13)
            throw new InvalidDataException("LZMA SWF header is truncated.");

        throw new InvalidDataException("LZMA (ZWS) decompression is not yet enabled in v1 parser.");
    }

    private static byte[] ReadUpToLimit(Stream stream, int targetBytes, int maxAllowedBytes)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int totalRead = 0;
        int cap = Math.Min(targetBytes, maxAllowedBytes);

        while (totalRead < cap)
        {
            int toRead = Math.Min(buffer.Length, cap - totalRead);
            int read = stream.Read(buffer, 0, toRead);
            if (read <= 0)
                break;

            output.Write(buffer, 0, read);
            totalRead += read;
        }

        return output.ToArray();
    }
}
