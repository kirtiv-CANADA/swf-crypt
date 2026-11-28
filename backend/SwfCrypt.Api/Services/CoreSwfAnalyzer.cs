using System.Text.Json;
using System.Text.Json.Serialization;
using SwfCrypt.Core;

namespace SwfCrypt.Api.Services;

public sealed class CoreSwfAnalyzer : ISwfAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<AutopsyData> AnalyzeAsync(byte[] data, CancellationToken ct)
    {
        var result = SwfParser.Parse(data);

        if (!result.IsSuccess || result.Autopsy is null)
        {
            throw new PermanentAnalysisException("The file could not be parsed as a valid SWF.");
        }

        var autopsy = result.Autopsy;
        var json = JsonSerializer.Serialize(autopsy, JsonOptions);

        return Task.FromResult(new AutopsyData(
            Convert.ToInt32(autopsy.Version),
            autopsy.ActionScriptKind.ToString(),
            json));
    }
}
