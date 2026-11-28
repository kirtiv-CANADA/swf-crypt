using System.Net;
using System.Net.Http.Json;

namespace SwfCrypt.Tests;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_registered_user_is_a_member_without_staff_access()
    {
        var member = await factory.RegisterAndLoginAsync();

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/me/files")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/review/pending")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Registering_the_same_email_twice_is_rejected()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.org";
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/auth/register", new { email, password = ApiClient.StrongPassword });
        var second = await client.PostAsJsonAsync("/api/auth/register", new { email, password = ApiClient.StrongPassword });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task A_weak_password_is_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/register", new { email = $"weak-{Guid.NewGuid():N}@example.org", password = "123" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        var email = $"lock-{Guid.NewGuid():N}@example.org";
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/register", new { email, password = ApiClient.StrongPassword })).EnsureSuccessStatusCode();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong-password-1!" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        // Even the correct password is refused while the account is locked.
        var correct = await client.PostAsJsonAsync("/api/auth/login", new { email, password = ApiClient.StrongPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
    }
}

public class RateLimitTests(LimitedApiFactory factory) : IClassFixture<LimitedApiFactory>
{
    [Fact]
    public async Task Login_attempts_are_rate_limited()
    {
        var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/login", new { email = "nobody@example.org", password = "Wrong-password-1!" });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
