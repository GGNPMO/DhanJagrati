using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DhanJagrati.Api.Tests;

/// <summary>Runs the real API in-process against the given connection string.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "dj-api-tests", Guid.NewGuid().ToString("N"));

    public ApiFactory(string connectionString)
    {
        // Program reads configuration while the host is being built, so environment variables are the
        // reliable way in. Tests using the factory share one collection (no parallel runs).
        Environment.SetEnvironmentVariable("ConnectionStrings__DhanJagrati", connectionString);
        Environment.SetEnvironmentVariable("App__UploadPath", Path.Combine(_work, "uploads"));
        Environment.SetEnvironmentVariable("App__KeysPath", Path.Combine(_work, "keys"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");

    public HttpClient Client()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-DJ-Client", "web");
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

[CollectionDefinition("api", DisableParallelization = true)]
public sealed class ApiCollection;

/// <summary>Small JSON helpers that print the response body when a status is unexpected.</summary>
public static class Http
{
    public static async Task<JsonElement> Expect(this Task<HttpResponseMessage> call, int status)
    {
        var response = await call;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == status,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} -> {(int)response.StatusCode}, expected {status}: {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    public static Task<JsonElement> GetJson(this HttpClient c, string url, int status = 200) => c.GetAsync(url).Expect(status);
    public static Task<JsonElement> PostJson(this HttpClient c, string url, object body, int status = 200) => c.PostAsJsonAsync(url, body).Expect(status);
    public static Task<JsonElement> PutJson(this HttpClient c, string url, object body, int status = 204) => c.PutAsJsonAsync(url, body).Expect(status);

    public static decimal Dec(this JsonElement e, string name) => e.GetProperty(name).GetDecimal();
    public static int Int(this JsonElement e, string name) => e.GetProperty(name).GetInt32();
    public static string Str(this JsonElement e, string name) => e.GetProperty(name).GetString()!;
}
