using System.Net.Http.Json;

namespace SwfCrypt.Tests;

/// <summary>Every role against every protected endpoint. The status codes are the contract.</summary>
public class AuthorizationMatrixTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string UnknownGuid = "11111111-1111-1111-1111-111111111111";

    public static TheoryData<string, string, string, int> Matrix()
    {
        var data = new TheoryData<string, string, string, int>();

        void Row(string method, string url, int anonymous, int member, int curator, int admin)
        {
            data.Add("anonymous", method, url, anonymous);
            data.Add("member", method, url, member);
            data.Add("curator", method, url, curator);
            data.Add("admin", method, url, admin);
        }

        //  method  url                                         anon member curator admin
        Row("GET",  "/health",                                   200, 200, 200, 200);
        Row("GET",  "/api/files",                                200, 200, 200, 200);
        Row("GET",  $"/api/files/{UnknownGuid}",                 404, 404, 404, 404);
        Row("GET",  "/api/me/files",                             401, 200, 200, 200);
        Row("GET",  "/api/review/pending",                       401, 403, 200, 200);
        Row("POST", $"/api/review/{UnknownGuid}/approve",        401, 403, 404, 404);
        Row("GET",  "/api/admin/users",                          401, 403, 403, 200);
        Row("GET",  "/api/admin/audit",                          401, 403, 403, 200);
        Row("PUT",  "/api/admin/users/missing-user/role",        401, 403, 403, 404);

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoint_access_matches_the_role(string role, string method, string url, int expectedStatus)
    {
        var client = await ClientForAsync(role);

        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { role = "Member" });
        }

        var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
    }

    private async Task<HttpClient> ClientForAsync(string role) => role switch
    {
        "anonymous" => factory.CreateClient(),
        "member" => await factory.LoginAsync(TestUsers.MemberEmail, TestUsers.MemberPassword),
        "curator" => await factory.LoginAsync(TestUsers.CuratorEmail, TestUsers.CuratorPassword),
        "admin" => await factory.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };
}
