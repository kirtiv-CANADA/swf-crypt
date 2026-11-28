using System.Net;

namespace SwfCrypt.Tests;

public class UploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Anonymous_users_cannot_upload()
    {
        var response = await factory.CreateClient().UploadAsync(ApiClient.NewSwf(1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_member_upload_creates_a_pending_file_and_a_queued_job()
    {
        var member = await factory.RegisterAndLoginAsync();

        var response = await member.UploadAsync(ApiClient.NewSwf(101), title: "My first game", author: "Me", year: "2005");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.ReadJsonAsync();
        Assert.Equal("Pending", json.GetProperty("status").GetString());
        Assert.False(json.GetProperty("duplicate").GetBoolean());
        Assert.NotEqual(Guid.Empty, json.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Source_and_license_are_mandatory()
    {
        var member = await factory.RegisterAndLoginAsync();

        var response = await member.UploadAsync(ApiClient.NewSwf(102), source: null, license: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("source", out _));
        Assert.True(errors.TryGetProperty("license", out _));
    }

    [Fact]
    public async Task A_file_that_is_not_a_swf_is_rejected()
    {
        var member = await factory.RegisterAndLoginAsync();

        var response = await member.UploadAsync("hello, this is plain text and not a flash file"u8.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("file", out _));
    }

    [Fact]
    public async Task A_file_over_the_size_limit_is_rejected()
    {
        var member = await factory.RegisterAndLoginAsync();
        var tooBig = new byte[ApiFactory.MaxUploadBytes + 10];
        tooBig[0] = (byte)'F';
        tooBig[1] = (byte)'W';
        tooBig[2] = (byte)'S';

        var response = await member.UploadAsync(tooBig);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_invalid_year_is_rejected()
    {
        var member = await factory.RegisterAndLoginAsync();

        var response = await member.UploadAsync(ApiClient.NewSwf(103), year: "1850");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_identical_bytes_again_returns_the_existing_record()
    {
        var first = await factory.RegisterAndLoginAsync();
        var second = await factory.RegisterAndLoginAsync();
        var bytes = ApiClient.NewSwf(104);

        var created = await first.UploadAsync(bytes, title: "Original");
        var duplicate = await second.UploadAsync(bytes, title: "Same bytes, other user");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);

        var createdJson = await created.ReadJsonAsync();
        var duplicateJson = await duplicate.ReadJsonAsync();
        Assert.Equal(createdJson.GetProperty("id").GetGuid(), duplicateJson.GetProperty("id").GetGuid());
        Assert.True(duplicateJson.GetProperty("duplicate").GetBoolean());
    }
}
