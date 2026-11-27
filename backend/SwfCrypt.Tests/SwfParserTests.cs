using System.Buffers.Binary;
using System.IO.Compression;
using FsCheck.Xunit;
using SwfCrypt.Core;
using Xunit;

namespace SwfCrypt.Tests;

public class SwfParserTests
{
    [Fact]
    public void Parse_ValidUncompressedFws_ExtractsStageFrameRateAndEndTag()
    {
        byte[] body =
        [
            0x0A, 0xA0,
            0x00, 0x18,
            0x01, 0x00,
            0x00, 0x00
        ];

        uint declaredLength = (uint)(8 + body.Length);
        byte[] swf = new byte[declaredLength];
        swf[0] = (byte)'F';
        swf[1] = (byte)'W';
        swf[2] = (byte)'S';
        swf[3] = 9;
        BinaryPrimitives.WriteUInt32LittleEndian(swf.AsSpan(4, 4), declaredLength);
        body.CopyTo(swf.AsSpan(8));

        SwfParseResult result = SwfParser.Parse(swf);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Autopsy);
        Assert.Equal(SwfCompression.Uncompressed, result.Autopsy.Compression);
        Assert.Equal((byte)9, result.Autopsy.Version);
        Assert.Equal(24.0, result.Autopsy.FrameRate);
        Assert.Equal((ushort)1, result.Autopsy.FrameCount);
        Assert.Equal(ActionScriptKind.None, result.Autopsy.ActionScriptKind);
        Assert.Equal(1, result.Autopsy.TotalTagCount);
    }

    [Fact]
    public void Parse_ValidZlibCws_DecompressesAndDetectsAs3()
    {
        byte[] uncompressedBody =
        [
            0x08, 0x00,
            0x00, 0x0C,
            0x05, 0x00,
            0x82, 0x14, 0xAA, 0xBB,
            0x00, 0x00
        ];

        byte[] compressedBody;
        using (var ms = new MemoryStream())
        {
            using (var zlib = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(uncompressedBody);
            }
            compressedBody = ms.ToArray();
        }

        uint declaredLength = (uint)(8 + uncompressedBody.Length);
        byte[] swf = new byte[8 + compressedBody.Length];
        swf[0] = (byte)'C';
        swf[1] = (byte)'W';
        swf[2] = (byte)'S';
        swf[3] = 15;
        BinaryPrimitives.WriteUInt32LittleEndian(swf.AsSpan(4, 4), declaredLength);
        compressedBody.CopyTo(swf.AsSpan(8));

        SwfParseResult result = SwfParser.Parse(swf);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Autopsy);
        Assert.Equal(SwfCompression.Zlib, result.Autopsy.Compression);
        Assert.Equal(12.0, result.Autopsy.FrameRate);
        Assert.Equal(ActionScriptKind.ActionScript3, result.Autopsy.ActionScriptKind);
        Assert.Equal(2, result.Autopsy.TotalTagCount);
    }

    [Property]
    public bool Parse_NeverThrowsOnArbitraryBytes(byte[]? randomBytes)
    {
        SwfParseResult result = SwfParser.Parse(randomBytes ?? []);
        return result is not null &&
               (result.IsSuccess
                   ? result.Autopsy is not null
                   : !string.IsNullOrWhiteSpace(result.ErrorCode));
    }
}
