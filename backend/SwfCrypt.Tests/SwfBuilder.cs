using System.IO.Compression;
using System.Text;

namespace SwfCrypt.Tests;

/// <summary>
/// Builds small, valid SWF byte arrays in code, so tests never need binary fixtures.
/// Tag type numbers follow the public SWF File Format Specification; verify them if you change anything.
/// </summary>
internal static class SwfBuilder
{
    public const ushort TagEnd = 0;
    public const ushort TagSetBackgroundColor = 9;
    public const ushort TagDoAction = 12;
    public const ushort TagDoInitAction = 59;
    public const ushort TagFileAttributes = 69;
    public const ushort TagDoAbc = 72;
    public const ushort TagDoAbc2 = 82;

    /// <summary>Builds one tag. Short header when payload &lt; 63 bytes, long header otherwise (or when forced).</summary>
    public static byte[] Tag(ushort type, byte[] payload, bool forceLongForm = false)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        if (!forceLongForm && payload.Length < 0x3F)
        {
            w.Write((ushort)((type << 6) | payload.Length));
        }
        else
        {
            w.Write((ushort)((type << 6) | 0x3F));
            w.Write((uint)payload.Length);
        }

        w.Write(payload);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>FileAttributes tag. The ActionScript3 flag is bit 3 (0x08) of the first byte.</summary>
    public static byte[] FileAttributes(bool actionScript3) =>
        Tag(TagFileAttributes, [(byte)(actionScript3 ? 0x08 : 0x00), 0, 0, 0]);

    public static byte[] DoAction() => Tag(TagDoAction, [0]);

    public static byte[] DoInitAction() => Tag(TagDoInitAction, [0, 0, 0]);

    public static byte[] DoAbc(ushort type = TagDoAbc2) => Tag(type, new byte[8]);

    public static byte[] SetBackgroundColor(bool forceLongForm = false) =>
        Tag(TagSetBackgroundColor, [255, 255, 255], forceLongForm);

    /// <summary>RECT: 5-bit field size, then four signed fields, padded to a whole byte.</summary>
    private static byte[] Rect(int nbits, int xmin, int xmax, int ymin, int ymax)
    {
        var bits = new List<bool>();

        void Put(int value, int count)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                bits.Add(((value >> i) & 1) == 1);
            }
        }

        Put(nbits, 5);
        Put(xmin, nbits);
        Put(xmax, nbits);
        Put(ymin, nbits);
        Put(ymax, nbits);

        while (bits.Count % 8 != 0)
        {
            bits.Add(false);
        }

        var bytes = new byte[bits.Count / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                bytes[i / 8] |= (byte)(0x80 >> (i % 8));
            }
        }

        return bytes;
    }

    /// <summary>
    /// Builds a whole SWF. <paramref name="signature"/> must be exactly 3 characters.
    /// "CWS" compresses the body with zlib; any other signature writes the body as-is.
    /// </summary>
    public static byte[] Build(
        string signature = "FWS",
        byte version = 10,
        IEnumerable<byte[]>? tags = null,
        bool addEndTag = true,
        ushort frameCount = 1)
    {
        var body = new MemoryStream();
        var bw = new BinaryWriter(body);

        bw.Write(Rect(16, 0, 11000, 0, 8000));   // 550 x 400 px, in twips
        bw.Write((ushort)(24 << 8));              // frame rate 24.0 (8.8 fixed point)
        bw.Write(frameCount);

        foreach (var tag in tags ?? Array.Empty<byte[]>())
        {
            bw.Write(tag);
        }

        if (addEndTag)
        {
            bw.Write(Tag(TagEnd, []));
        }

        bw.Flush();
        var bodyBytes = body.ToArray();

        var output = new MemoryStream();
        var ow = new BinaryWriter(output);
        ow.Write(Encoding.ASCII.GetBytes(signature));
        ow.Write(version);
        ow.Write((uint)(8 + bodyBytes.Length));
        ow.Flush();

        if (signature == "CWS")
        {
            using var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true);
            z.Write(bodyBytes);
        }
        else
        {
            ow.Write(bodyBytes);
            ow.Flush();
        }

        return output.ToArray();
    }
}
