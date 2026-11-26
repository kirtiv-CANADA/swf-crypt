namespace SwfCrypt.Core;

public enum SwfCompression
{
    Uncompressed,
    Zlib,
    Lzma
}

public enum ActionScriptKind
{
    None,
    ActionScript1_2,
    ActionScript3,
    Mixed
}

public readonly record struct SwfRect(
    int XMinTwips,
    int XMaxTwips,
    int YMinTwips,
    int YMaxTwips)
{
    public double WidthPixels => (XMaxTwips - XMinTwips) / 20.0;
    public double HeightPixels => (YMaxTwips - YMinTwips) / 20.0;
}

public sealed record SwfTagSummary(
    ushort TagCode,
    int Count,
    long TotalPayloadBytes);

public sealed record SwfAutopsyResult(
    SwfCompression Compression,
    byte Version,
    uint DeclaredLength,
    int ActualPayloadLength,
    SwfRect StageBounds,
    double FrameRate,
    ushort FrameCount,
    ActionScriptKind ActionScriptKind,
    int TotalTagCount,
    IReadOnlyList<SwfTagSummary> Tags);

public sealed record SwfParseResult(
    bool IsSuccess,
    SwfAutopsyResult? Autopsy,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SwfParseResult Success(SwfAutopsyResult autopsy) =>
        new(true, autopsy, null, null);

    public static SwfParseResult Failure(string errorCode, string errorMessage) =>
        new(false, null, errorCode, errorMessage);
}
