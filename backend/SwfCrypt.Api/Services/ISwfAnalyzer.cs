namespace SwfCrypt.Api.Services;

public sealed record AutopsyData(int Version, string ActionScript, string ReportJson);

public interface ISwfAnalyzer
{
    Task<AutopsyData> AnalyzeAsync(byte[] data, CancellationToken ct);
}

public sealed class PermanentAnalysisException(string message) : Exception(message);

public sealed class TransientAnalysisException(string message, Exception? inner = null) : Exception(message, inner);
