using System.Text.Json;

using System.Net;
using System.Net.Http.Json;

namespace SwfCrypt.Tests;

public class ReviewWorkflowTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ReviewWorkflowTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Analyzer.Reset();
    }

    [Fact]
    public async Task Upload_analysis_review_and_public_play_end_to_end()
    {
        var member = await _factory.RegisterAndLoginAsync();
        var curator = await _factory.LoginAsync(TestUsers.CuratorEmail, TestUsers.CuratorPassword);
        var anonymous = _factory.CreateClient();
        var bytes = ApiClient.NewSwf(201);

        // 1. Member uploads; the background worker analyses the file.
        var upload = await member.UploadAsync(bytes, title: "Workflow game");
        var id = (await upload.ReadJsonAsync()).GetProperty("id").GetGuid();
        await member.WaitForJobAsync(id, "Succeeded");

        // 2. Still pending: invisible to the public.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/files/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/files/{id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/files/{id}")).StatusCode);

        // 3. The curator sees it in the queue and approves it.
        var pending = await curator.GetFromJsonAsync<JsonElement>("/api/review/pending?pageSize=50");
        Assert.Contains(pending.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);

        var approve = await curator.PostAsync($"/api/review/{id}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        // 4. Now anyone can see the autopsy and download the SWF for Ruffle.
        var detail = await anonymous.GetFromJsonAsync<JsonElement>($"/api/files/{id}");
        Assert.Equal("Approved", detail.GetProperty("status").GetString());
        Assert.Equal("Resurrected", detail.GetProperty("report").GetProperty("verdict").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("job").ValueKind);   // job details are not public

        var content = await anonymous.GetAsync($"/api/files/{id}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal(bytes, await content.Content.ReadAsByteArrayAsync());

        var list = await anonymous.GetFromJsonAsync<JsonElement>("/api/files?pageSize=50");
        Assert.Contains(list.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);

        // 5. The decision was written to the audit log.
        var admin = await _factory.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword);
        var audit = await admin.GetFromJsonAsync<JsonElement>("/api/admin/audit?pageSize=50");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), entry =>
            entry.GetProperty("action").GetString() == "review.approve" &&
            entry.GetProperty("entityId").GetString() == id.ToString());
    }

    [Fact]
    public async Task Rejecting_requires_a_note_and_hides_the_file()
    {
        var member = await _factory.RegisterAndLoginAsync();
        var curator = await _factory.LoginAsync(TestUsers.CuratorEmail, TestUsers.CuratorPassword);

        var upload = await member.UploadAsync(ApiClient.NewSwf(202), title: "Rejected game");
        var id = (await upload.ReadJsonAsync()).GetProperty("id").GetGuid();
        await member.WaitForJobAsync(id, "Succeeded");

        var withoutNote = await curator.PostAsJsonAsync($"/api/review/{id}/reject", new { note = "" });
        Assert.Equal(HttpStatusCode.BadRequest, withoutNote.StatusCode);

        var withNote = await curator.PostAsJsonAsync($"/api/review/{id}/reject", new { note = "License could not be verified." });
        Assert.Equal(HttpStatusCode.OK, withNote.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateClient().GetAsync($"/api/files/{id}")).StatusCode);

        // A decision is final: reviewing it again is a conflict.
        var again = await curator.PostAsync($"/api/review/{id}/approve", content: null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_file_cannot_be_approved_before_its_analysis_finishes()
    {
        var member = await _factory.RegisterAndLoginAsync();
        var curator = await _factory.LoginAsync(TestUsers.CuratorEmail, TestUsers.CuratorPassword);

        // A permanent failure means no report will ever exist.
        _factory.Analyzer.Handler = (_, _) => throw new SwfCrypt.Api.Services.PermanentAnalysisException("Not a real SWF.");

        var upload = await member.UploadAsync(ApiClient.NewSwf(203), title: "Broken game");
        var id = (await upload.ReadJsonAsync()).GetProperty("id").GetGuid();
        await member.WaitForJobAsync(id, "Failed");

        var approve = await curator.PostAsync($"/api/review/{id}/approve", content: null);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);

        _factory.Analyzer.Reset();
    }

    [Fact]
    public async Task Admin_can_change_a_role_and_the_change_is_audited()
    {
        var admin = await _factory.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword);
        var email = $"promote-{Guid.NewGuid():N}@example.org";
        var promoted = await _factory.RegisterAndLoginAsync(email);

        var users = await admin.GetFromJsonAsync<JsonElement>("/api/admin/users?pageSize=50");
        var userId = users.GetProperty("items").EnumerateArray()
            .First(u => u.GetProperty("email").GetString() == email)
            .GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.Forbidden, (await promoted.GetAsync("/api/review/pending")).StatusCode);

        var change = await admin.PutAsJsonAsync($"/api/admin/users/{userId}/role", new { role = "Curator" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        // A fresh login picks up the new role.
        var promotedAgain = await _factory.LoginAsync(email, ApiClient.StrongPassword);
        Assert.Equal(HttpStatusCode.OK, (await promotedAgain.GetAsync("/api/review/pending")).StatusCode);
    }
}
