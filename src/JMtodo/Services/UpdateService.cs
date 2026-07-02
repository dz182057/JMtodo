using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace TodoDesktopApp.Services;

public sealed class UpdateService : IDisposable
{
    public const string ManifestUrl = "http://129.211.26.139/jmtodo/update.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        // 更新服务器是固定公网 IP，绕过系统代理可避免代理连接卡住。
        UseProxy = false
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions, cancellationToken);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidOperationException(LocalizationService.Text("Update.InvalidManifest"));
        }

        var currentVersionText = GetCurrentVersionText();
        var currentVersion = ParseVersion(currentVersionText);
        var latestVersion = ParseVersion(manifest.Version);
        var downloadUrl = ResolveDownloadUrl(manifest.DownloadUrl ?? manifest.PackageUrl);
        var releaseNotes = BuildReleaseNotes(manifest);

        return new UpdateCheckResult(
            currentVersionText,
            latestVersion.ToString(),
            latestVersion > currentVersion,
            downloadUrl,
            releaseNotes);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static string GetCurrentVersionText()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+', 2)[0];
        }

        return assembly.GetName().Version?.ToString() ?? "1.0.0";
    }

    private static Version ParseVersion(string versionText)
    {
        var normalized = versionText.Trim().TrimStart('v', 'V').Split('+', 2)[0];
        if (Version.TryParse(normalized, out var version))
        {
            return version;
        }

        throw new InvalidOperationException(LocalizationService.Format("Update.InvalidVersionFormat", versionText));
    }

    private static string? ResolveDownloadUrl(string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        return new Uri(new Uri(ManifestUrl), downloadUrl).ToString();
    }

    private static string BuildReleaseNotes(UpdateManifest manifest)
    {
        var notes = manifest.ReleaseNotes
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .Select(note => $"- {note.Trim()}")
            .ToList();

        if (!string.IsNullOrWhiteSpace(manifest.Notes))
        {
            notes.Add(manifest.Notes.Trim());
        }

        return notes.Count == 0
            ? LocalizationService.Text("Update.NoReleaseNotes")
            : string.Join(Environment.NewLine, notes);
    }
}

public sealed record UpdateCheckResult(
    string CurrentVersion,
    string LatestVersion,
    bool UpdateAvailable,
    string? DownloadUrl,
    string ReleaseNotes);

public sealed class UpdateManifest
{
    public string Version { get; init; } = string.Empty;

    public string? DownloadUrl { get; init; }

    public string? PackageUrl { get; init; }

    public string? Notes { get; init; }

    public List<string> ReleaseNotes { get; init; } = [];
}
