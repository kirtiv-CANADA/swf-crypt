namespace SwfCrypt.Core;

public enum ActionScriptKind
{
    None,
    ActionScript1,
    ActionScript2,
    ActionScript3,
    Mixed,
    AS1 = ActionScript1,
    AS2 = ActionScript2,
    AS3 = ActionScript3
}

public enum SwfCompression
{
    Uncompressed,
    Zlib,
    Lzma,
    None = Uncompressed
}

public sealed record SwfAutopsy(
    byte Version,
    SwfCompression Compression,
    double FrameRate = 0,
    ushort FrameCount = 0,
    ActionScriptKind ActionScriptKind = ActionScriptKind.None,
    int TotalTagCount = 0,
    IReadOnlyList<string>? Warnings = null)
{
    public ActionScriptKind ActionScript => ActionScriptKind;
}

public sealed record SwfParseResult(
    bool IsSuccess,
    SwfAutopsy? Autopsy = null,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool Success => IsSuccess;

    public static SwfParseResult Ok(SwfAutopsy autopsy) =>
        new(true, autopsy);

    public static SwfParseResult Fail(string errorCode, string errorMessage) =>
        new(false, null, errorCode, errorMessage);
}

public readonly record struct SwfRect(
    int XMin,
    int XMax,
    int YMin,
    int YMax)
{
    public int WidthTwips => XMax - XMin;
    public int HeightTwips => YMax - YMin;
    public double WidthPixels => WidthTwips / 20.0;
    public double HeightPixels => HeightTwips / 20.0;
}
