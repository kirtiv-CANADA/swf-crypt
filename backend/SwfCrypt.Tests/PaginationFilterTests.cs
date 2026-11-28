using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SwfCrypt.Api.Services;

namespace SwfCrypt.Tests;

public class PaginationFilterTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task The_public_list_supports_paging_search_and_filters()
    {
        factory.Analyzer.Reset();
        // Calls 1, 3, 5 are AS3 (Haunted); calls 2, 4 are AS2 (Resurrected).
        factory.Analyzer.Handler = (_, call) => new AutopsyData(10, call % 2 == 1 ? "AS3" : "AS2", "{}");

        var member = await factory.RegisterAndLoginAsync();
        var curator = await factory.LoginAsync(TestUsers.CuratorEmail, TestUsers.CuratorPassword);

        var titles = new[] { "Alpha Blaster", "Beta Quest", "Gamma Run", "Delta Dash", "Epsilon Maze" };
        for (var i = 0; i < titles.Length; i++)
        {
            var upload = await member.UploadAsync(ApiClient.NewSwf(300 + i), title: titles[i], year: (2001 + i).ToString());
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

            var id = (await upload.ReadJsonAsync()).GetProperty("id").GetGuid();
            await member.WaitForJobAsync(id, "Succeeded");   // one at a time keeps the call numbers in order
        }

        await curator.ApproveAllPendingAsync();
        var anonymous = factory.CreateClient();

        // Paging: 5 items, 2 per page -> page 3 holds the last one.
        var page3 = await anonymous.GetFromJsonAsync<JsonElement>("/api/files?pageSize=2&page=3");
        Assert.Equal(1, page3.GetProperty("items").GetArrayLength());
        Assert.Equal(5, page3.GetProperty("total").GetInt32());

        // Search and year filter.
        Assert.Equal(1, await TotalAsync(anonymous, "/api/files?q=alpha"));
        var year2003 = await anonymous.GetFromJsonAsync<JsonElement>("/api/files?year=2003");
        Assert.Equal(1, year2003.GetProperty("total").GetInt32());
        Assert.Equal("Gamma Run", year2003.GetProperty("items")[0].GetProperty("title").GetString());

        // ActionScript and verdict filters agree with each other.
        var as3 = await TotalAsync(anonymous, "/api/files?actionScript=AS3");
        var as2 = await TotalAsync(anonymous, "/api/files?actionScript=AS2");
        Assert.Equal(5, as3 + as2);
        Assert.True(as3 > 0 && as2 > 0);
        Assert.Equal(as3, await TotalAsync(anonymous, "/api/files?verdict=Haunted"));
        Assert.Equal(as2, await TotalAsync(anonymous, "/api/files?verdict=Resurrected"));

        // Bad paging input is clamped, never an error.
        var clamped = await anonymous.GetFromJsonAsync<JsonElement>("/api/files?pageSize=1000&page=0");
        Assert.Equal(50, clamped.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, clamped.GetProperty("page").GetInt32());

        factory.Analyzer.Reset();
    }

    private static async Task<int> TotalAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("total").GetInt32();
}
