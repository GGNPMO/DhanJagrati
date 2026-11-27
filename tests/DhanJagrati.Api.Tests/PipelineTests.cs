namespace DhanJagrati.Api.Tests;

/// <summary>Request pipeline checks that need no database (an unreachable server is configured).</summary>
[Collection("api")]
public sealed class PipelineTests : IDisposable
{
    private readonly ApiFactory _factory = new("Server=127.0.0.1,1;Database=None;User ID=x;Password=y;Connect Timeout=2;TrustServerCertificate=True");

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Health_reports_database_unavailable()
    {
        var body = await _factory.Client().GetJson("/api/health", 503);
        Assert.False(body.GetProperty("database").GetBoolean());
    }

    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/members")]
    [InlineData("/api/loans")]
    [InlineData("/api/accounts/ledgers")]
    [InlineData("/api/loan-reports/closures")]
    public async Task Protected_endpoints_require_login(string url)
    {
        await _factory.Client().GetJson(url, 401);
    }

    [Fact]
    public async Task Unsafe_requests_need_the_client_header()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/auth/login", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(400, (int)response.StatusCode);
        Assert.Contains("X-DJ-Client", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_with_database_down_returns_503_not_500()
    {
        await _factory.Client().PostJson("/api/auth/login", new { userName = "SYSTEM", password = "Whatever1" }, 503);
    }

    [Fact]
    public async Task Security_headers_are_set()
    {
        var response = await _factory.Client().GetAsync("/api/auth/me");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
