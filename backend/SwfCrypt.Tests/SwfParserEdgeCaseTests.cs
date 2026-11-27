using System.Diagnostics;
using FsCheck.Xunit;
using SwfCrypt.Core;

namespace SwfCrypt.Tests;

/// <summary>
/// Edge-case tests for the SWF parser.
///
/// ASSUMED API (change ONLY the two helpers below if your names differ):
///   SwfParseResult SwfParser.Parse(byte[] data)
///   result.Success            (bool)
///   result.Autopsy?.ActionScript   (enum ActionScriptKind { None, AS1, AS2, AS3, Mixed })
///   result.Autopsy?.Version        (SWF version byte)
///
/// ASSUMED RULES (adjust the expectations if your spec says otherwise):
///   - FWS and CWS are supported, ZWS is reported as unsupported (Success == false).
///   - AS3  = FileAttributes ActionScript3 flag OR a DoABC/DoABC2 tag.
///   - AS1/AS2 = DoAction/DoInitAction present; SWF version 6 or lower => AS1, 7 or higher => AS2.
///   - Mixed = both AS1/2 code and AS3 code in one file.
///   - Truncated or corrupt input => Success == false (never an exception).
/// </summary>
public class SwfParserEdgeCaseTests
{
    // ---- the only two places that know your real API --------------------------------------
    private static SwfParseResult Parse(byte[] data) => SwfParser.Parse(data);

    private static ActionScriptKind? Kind(SwfParseResult result) => result.Autopsy?.ActionScript;
    // ---------------------------------------------------------------------------------------

    private static void AssertKind(ActionScriptKind expected, byte[] swf)
    {
        var result = Parse(swf);
        Assert.True(result.Success, "expected the file to parse successfully");
        Assert.Equal<ActionScriptKind?>(expected, Kind(result));
    }

    private static void AssertFailsWithoutThrowing(byte[] data)
    {
        SwfParseResult? result = null;
        var exception = Record.Exception(() => result = Parse(data));

        Assert.Null(exception);
        Assert.False(result!.Success);
    }

    // ===== Long tag headers (0x3F) ==========================================================

    [Fact]
    public void Long_form_header_with_a_small_payload_keeps_the_walk_in_sync()
    {
        // If the long header is mishandled, the DoABC tag after it is never seen.
        var swf = SwfBuilder.Build(tags: [SwfBuilder.SetBackgroundColor(forceLongForm: true), SwfBuilder.DoAbc()]);

        AssertKind(ActionScriptKind.AS3, swf);
    }

    [Theory]
    [InlineData(62)]      // largest payload that still fits the short header
    [InlineData(63)]      // smallest payload that needs the long header (0x3F marker)
    [InlineData(64)]
    [InlineData(70_000)]  // larger than 64 KB
    public void Payload_length_boundaries_keep_the_walk_in_sync(int payloadLength)
    {
        var big = SwfBuilder.Tag(SwfBuilder.TagSetBackgroundColor, new byte[payloadLength]);
        var swf = SwfBuilder.Build(tags: [big, SwfBuilder.DoAbc()]);

        AssertKind(ActionScriptKind.AS3, swf);
    }

    [Fact]
    public void Tag_with_an_absurd_declared_length_fails_fast_without_throwing()
    {
        // Long header (type 9, 0x3F marker) declaring 4 GB, with almost no bytes behind it.
        byte[] evilTag = [0x7F, 0x02, 0xFF, 0xFF, 0xFF, 0xFF];
        var swf = SwfBuilder.Build(tags: [evilTag], addEndTag: false);

        var timer = Stopwatch.StartNew();
        AssertFailsWithoutThrowing(swf);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), "parser must reject this quickly");
    }

    // ===== Truncated input ==================================================================

    [Fact]
    public void Tag_declaring_more_bytes_than_remain_fails_without_throwing()
    {
        var full = SwfBuilder.Build(
            tags: [SwfBuilder.Tag(SwfBuilder.TagSetBackgroundColor, new byte[100])],
            addEndTag: false);

        // If your parser returns a partial result with warnings instead, change this
        // expectation (Assert.True(Success) plus a warning assertion) to match your spec.
        AssertFailsWithoutThrowing(full[..^50]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(11)]
    [InlineData(17)]
    [InlineData(20)]
    public void Incomplete_header_fails_without_throwing(int length)
    {
        var full = SwfBuilder.Build(tags: [SwfBuilder.DoAbc()]);

        AssertFailsWithoutThrowing(full[..length]);
    }

    [Fact]
    public void Every_prefix_of_a_valid_file_never_throws()
    {
        var full = SwfBuilder.Build(tags: [SwfBuilder.FileAttributes(true), SwfBuilder.DoAbc()]);

        for (var length = 0; length <= full.Length; length++)
        {
            var prefix = full[..length];
            var exception = Record.Exception(() => Parse(prefix));
            Assert.True(exception is null, $"parser threw for a prefix of {length} bytes: {exception}");
        }
    }

    // ===== ActionScript classification ======================================================

    [Fact]
    public void No_scripts_is_classified_as_None()
    {
        AssertKind(ActionScriptKind.None, SwfBuilder.Build(tags: [SwfBuilder.SetBackgroundColor()]));
    }

    [Theory]
    [InlineData((byte)3)]
    [InlineData((byte)5)]
    [InlineData((byte)6)]
    public void DoAction_in_old_swf_versions_is_AS1(byte version)
    {
        AssertKind(ActionScriptKind.AS1, SwfBuilder.Build(version: version, tags: [SwfBuilder.DoAction()]));
    }

    [Theory]
    [InlineData((byte)7)]
    [InlineData((byte)8)]
    [InlineData((byte)9)]
    public void DoAction_in_swf_7_and_later_is_AS2(byte version)
    {
        AssertKind(ActionScriptKind.AS2, SwfBuilder.Build(version: version, tags: [SwfBuilder.DoAction()]));
    }

    [Fact]
    public void DoInitAction_counts_as_AS2()
    {
        AssertKind(ActionScriptKind.AS2, SwfBuilder.Build(version: 8, tags: [SwfBuilder.DoInitAction()]));
    }

    [Fact]
    public void FileAttributes_flag_alone_is_AS3()
    {
        AssertKind(ActionScriptKind.AS3, SwfBuilder.Build(version: 10, tags: [SwfBuilder.FileAttributes(actionScript3: true)]));
    }

    [Theory]
    [InlineData(SwfBuilder.TagDoAbc)]
    [InlineData(SwfBuilder.TagDoAbc2)]
    public void DoAbc_tag_without_FileAttributes_is_AS3(ushort tagType)
    {
        AssertKind(ActionScriptKind.AS3, SwfBuilder.Build(version: 10, tags: [SwfBuilder.DoAbc(tagType)]));
    }

    [Fact]
    public void FileAttributes_without_the_AS3_flag_does_not_make_a_file_AS3()
    {
        var swf = SwfBuilder.Build(version: 9, tags: [SwfBuilder.FileAttributes(actionScript3: false), SwfBuilder.DoAction()]);

        AssertKind(ActionScriptKind.AS2, swf);
    }

    [Fact]
    public void DoAction_plus_DoAbc_is_Mixed()
    {
        var swf = SwfBuilder.Build(version: 9, tags: [SwfBuilder.DoAction(), SwfBuilder.DoAbc()]);

        AssertKind(ActionScriptKind.Mixed, swf);
    }

    // ===== Invalid signatures and compression ===============================================

    [Theory]
    [InlineData("XWS")]
    [InlineData("ZIP")]
    [InlineData("fws")]   // signatures are case-sensitive
    [InlineData("FWX")]
    [InlineData("\0\0\0")]
    public void Invalid_signature_is_rejected(string signature)
    {
        AssertFailsWithoutThrowing(SwfBuilder.Build(signature: signature));
    }

    [Fact]
    public void Empty_input_is_rejected()
    {
        AssertFailsWithoutThrowing([]);
    }

    [Fact]
    public void ZWS_lzma_files_are_reported_as_unsupported()
    {
        AssertFailsWithoutThrowing(SwfBuilder.Build(signature: "ZWS"));
    }

    [Fact]
    public void Valid_CWS_file_is_decompressed_and_parsed()
    {
        AssertKind(ActionScriptKind.AS3, SwfBuilder.Build(signature: "CWS", tags: [SwfBuilder.DoAbc()]));
    }

    [Fact]
    public void CWS_with_a_corrupt_zlib_body_fails_without_throwing()
    {
        var swf = SwfBuilder.Build(signature: "CWS", tags: [SwfBuilder.DoAbc()]);
        for (var i = 10; i < swf.Length; i++)
        {
            swf[i] = 0xAA;
        }

        AssertFailsWithoutThrowing(swf);
    }

    [Fact]
    public void CWS_declaring_a_4GB_uncompressed_length_is_rejected_quickly()
    {
        var swf = SwfBuilder.Build(signature: "CWS", tags: [SwfBuilder.DoAbc()]);
        BitConverter.GetBytes(uint.MaxValue).CopyTo(swf, 4);

        var timer = Stopwatch.StartNew();
        AssertFailsWithoutThrowing(swf);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), "must reject before decompressing");
    }

    // ===== Property-based tests (FsCheck) ===================================================

    [Property]
    public bool Random_bytes_never_throw(byte[] data) =>
        Record.Exception(() => Parse(data ?? [])) is null;

    [Property]
    public bool Changing_any_single_byte_of_a_valid_file_never_throws(int index, byte value)
    {
        var mutated = SwfBuilder.Build(tags: [SwfBuilder.FileAttributes(true), SwfBuilder.DoAbc()]);
        mutated[Math.Abs(index % mutated.Length)] = value;

        return Record.Exception(() => Parse(mutated)) is null;
    }

    [Property]
    public bool Valid_files_always_report_their_own_version(byte raw)
    {
        var version = (byte)(1 + raw % 40);
        var result = Parse(SwfBuilder.Build(version: version, tags: [SwfBuilder.SetBackgroundColor()]));

        return result.Success && result.Autopsy!.Version == version;
    }
}
