namespace SwfCrypt.Api.Config;

public sealed class StorageOptions
{
    public string SwfFolder { get; set; } = "data/swf";
    public long MaxUploadBytes { get; set; } = 26_214_400;
}

public sealed class AnalysisOptions
{
    public int MaxAttempts { get; set; } = 3;
    public double BaseDelaySeconds { get; set; } = 1;
    public int QueueCapacity { get; set; } = 200;
}

public sealed class RateLimitSettings
{
    public int AuthPerMinute { get; set; } = 10;
    public int UploadPerMinute { get; set; } = 10;
}

public sealed class SeedOptions
{
    public List<SeedUser> DemoUsers { get; set; } = [];
}

public sealed class SeedUser
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "Member";
    public string? DisplayName { get; set; }
}
