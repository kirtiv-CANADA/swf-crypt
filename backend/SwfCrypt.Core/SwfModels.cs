namespace SwfCrypt.Core;

public enum ActionScriptKind
{
    None,
    AS1,
    AS2,
    AS3,
    Mixed
}

public enum SwfCompression
{
    None,
    Zlib,
    Lzma
}

public sealed record SwfAutopsy(
    byte Version,
    SwfCompression Compression,
    ActionScriptKind ActionScript,
    double FrameRate = 0,
    int TotalTagCount = 0,
    IReadOnlyList<string>? Warnings = null);

public sealed record SwfParseResult(
    bool Success,
    SwfAutopsy? Autopsy = null,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
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
