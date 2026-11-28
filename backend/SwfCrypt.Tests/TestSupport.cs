using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SwfCrypt.Api;
using SwfCrypt.Api.Services;

namespace SwfCrypt.Tests;

/// <summary>The demo accounts seeded from appsettings.json "Seed:DemoUsers". Keep these in sync with that file.</summary>
public static class TestUsers
{
    public const string AdminEmail = "admin@swfcrypt.demo";
    public const string AdminPassword = "Demo-Admin-1!";
    public const string CuratorEmail = "curator@swfcrypt.demo";
    public const string CuratorPassword = "Demo-Curator-1!";
    public const string MemberEmail = "member@swfcrypt.demo";
    public const string MemberPassword = "Demo-Member-1!";
}

/// <summary>A scriptable analyzer so tests never depend on the real parser.</summary>
public sealed class FakeAnalyzer : ISwfAnalyzer
{
    private static readonly Func<byte[], int, AutopsyData> DefaultHandler =
        (_, _) => new AutopsyData(10, "AS2", "{\"fake\":true}");

    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Receives the bytes and the 1-based call number. May return data or throw.</summary>
    public Func<byte[], int, AutopsyData> Handler { get; set; } = DefaultHandler;

    public void Reset()
    {
        Interlocked.Exchange(ref _calls, 0);
        Handler = DefaultHandler;
    }

    public Task<AutopsyData> AnalyzeAsync(byte[] data, CancellationToken ct)
    {
        var call = Interlocked.Increment(ref _calls);
        return Task.FromResult(Handler(data, call));
    }
}

/// <summary>Hosts the real API with a throw-away SQLite file, a temp upload folder and a fake analyzer.</summary>
public class ApiFactory : WebApplicationFactory<ApiMarker>
{
    public const long MaxUploadBytes = 1_048_576;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "swfcrypt-api-" + Guid.NewGuid().ToString("N"));

    public FakeAnalyzer Analyzer { get; } = new();

    protected virtual Dictionary<string, string?> ExtraSettings => new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_root);
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(_root, "test.db")}",
                ["Storage:SwfFolder"] = Path.Combine(_root, "swf"),
                ["Storage:MaxUploadBytes"] = MaxUploadBytes.ToString(),
                ["RateLimit:AuthPerMinute"] = "100000",
                ["RateLimit:UploadPerMinute"] = "100000",
                ["Analysis:BaseDelaySeconds"] = "0"
            };

            foreach (var pair in ExtraSettings)
            {
                settings[pair.Key] = pair.Value;
            }

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISwfAnalyzer>();
            services.AddSingleton<ISwfAnalyzer>(Analyzer);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception)
        {
            // Temp folder cleanup is best effort.
        }
    }
}

/// <summary>Same API, but with a tiny login limit so the rate limiter can be tested.</summary>
public sealed class LimitedApiFactory : ApiFactory
{
    protected override Dictionary<string, string?> ExtraSettings => new() { ["RateLimit:AuthPerMinute"] = "3" };
}

public static class ApiClient
{
    public const string StrongPassword = "Pass-word-1!";

    public static async Task<HttpClient> LoginAsync(this ApiFactory factory, string email, string password)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }

    public static async Task<HttpClient> RegisterAndLoginAsync(this ApiFactory factory, string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@example.org";

        var register = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password = StrongPassword });
        register.EnsureSuccessStatusCode();

        return await factory.LoginAsync(email, StrongPassword);
    }

    /// <summary>A small valid-looking SWF whose bytes differ per seed (so every seed is a different file).</summary>
    public static byte[] NewSwf(int seed) => SwfBuilder.Build(frameCount: (ushort)seed);

    public static async Task<HttpResponseMessage> UploadAsync(
        this HttpClient client,
        byte[] bytes,
        string? title = "Test file",
        string? source = "https://example.org/source",
        string? license = "CC0",
        string? author = null,
        string? year = null)
    {
        using var content = new MultipartFormDataContent();

        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/x-shockwave-flash");
        content.Add(file, "file", "test.swf");

        if (title is not null) content.Add(new StringContent(title), "title");
        if (source is not null) content.Add(new StringContent(source), "source");
        if (license is not null) content.Add(new StringContent(license), "license");
        if (author is not null) content.Add(new StringContent(author), "author");
        if (year is not null) content.Add(new StringContent(year), "year");

        return await client.PostAsync("/api/files", content);
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Polls "my files" until the job of the given file reaches the expected state.</summary>
    public static async Task WaitForJobAsync(this HttpClient owner, Guid fileId, string expected, int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        string? last = null;

        while (DateTime.UtcNow < deadline)
        {
            var page = await owner.GetFromJsonAsync<JsonElement>("/api/me/files?pageSize=50");
            foreach (var item in page.GetProperty("items").EnumerateArray())
            {
                if (item.GetProperty("id").GetGuid() == fileId)
                {
                    last = item.GetProperty("state").GetString();
                    if (last == expected)
                    {
                        return;
                    }
                }
            }

            await Task.Delay(100);
        }

        Assert.Fail($"The job did not reach '{expected}' in time (last state: {last ?? "unknown"}).");
    }

    public static async Task ApproveAllPendingAsync(this HttpClient curator)
    {
        var pending = await curator.GetFromJsonAsync<JsonElement>("/api/review/pending?pageSize=50");
        foreach (var item in pending.GetProperty("items").EnumerateArray())
        {
            var id = item.GetProperty("id").GetGuid();
            var response = await curator.PostAsync($"/api/review/{id}/approve", content: null);
            response.EnsureSuccessStatusCode();
        }
    }
}
