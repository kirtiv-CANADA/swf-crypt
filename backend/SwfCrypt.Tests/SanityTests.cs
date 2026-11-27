using FsCheck.Xunit;

namespace SwfCrypt.Tests;

public class SanityTests
{
    [Fact]
    public void Fact_runs() => Assert.True(true);

    [Property]
    public bool Property_runs(int a, int b) => a + b == b + a;
}