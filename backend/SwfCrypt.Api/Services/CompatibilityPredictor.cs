using SwfCrypt.Api.Data;

namespace SwfCrypt.Api.Services;

public static class CompatibilityPredictor
{
    public static CompatibilityVerdict Predict(string actionScript, int version) =>
        actionScript switch
        {
            "None" or "ActionScript1" or "ActionScript2" or "AS1" or "AS2" => CompatibilityVerdict.Resurrected,
            "ActionScript3" or "Mixed" or "AS3" => CompatibilityVerdict.Haunted,
            _ => CompatibilityVerdict.Haunted
        };
}
