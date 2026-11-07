using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Restage.Services.Api;

/// <summary>A caller of the running app's local API (the CLI, scripts); see docs/API.md.</summary>
public sealed class LocalApiClient : IDisposable
{
    readonly HttpClient _http;

    public LocalApiInfo Info { get; }

    LocalApiClient(LocalApiInfo info)
    {
        Info = info;
        _http = new HttpClient { BaseAddress = new Uri(info.BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
    }

    /// <summary>A client for the running app, or null when Restage is not running (or its API is off).</summary>
    public static LocalApiClient? Connect() => LocalApiFile.Read() is { } info ? new LocalApiClient(info) : null;

    public async Task<(int Status, string Body)> GetAsync(string path, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(path.TrimStart('/'), ct);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<(int Status, string Body)> PostAsync(string path, object body, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(path.TrimStart('/'), content, ct);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    public void Dispose() => _http.Dispose();
}
