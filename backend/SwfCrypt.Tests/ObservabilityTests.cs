using System.Net;

namespace SwfCrypt.Tests;

public class ObservabilityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_reports_ok_and_returns_a_correlation_id()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task A_safe_incoming_correlation_id_is_echoed_back()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-ID", "test-123");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal("test-123", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task An_unsafe_incoming_correlation_id_is_replaced()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", "bad value with spaces");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.NotEqual("bad value with spaces", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task Unknown_routes_return_problem_details()
    {
        var response = await factory.CreateClient().GetAsync("/api/nothing-here");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("problem+json", response.Content.Headers.ContentType?.ToString() ?? "");
    }
}
