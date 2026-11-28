namespace SwfCrypt.Api.Auth;

public static class Roles
{
    public const string Member = "Member";
    public const string Curator = "Curator";
    public const string Admin = "Admin";

    public static readonly string[] All = [Member, Curator, Admin];
}

public static class Policies
{
    public const string MemberOrAbove = nameof(MemberOrAbove);
    public const string CuratorOrAbove = nameof(CuratorOrAbove);
    public const string AdminOnly = nameof(AdminOnly);
}
