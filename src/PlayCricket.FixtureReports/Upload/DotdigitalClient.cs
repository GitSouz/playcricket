using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PlayCricket.FixtureReports.Upload;

/// <summary>
/// Minimal Dotdigital v2 API client for document folders and uploads —
/// replaces the manual Postman folder-create step and the
/// PlayCricketDotmailerUploadFixtureReports Logic App.
/// </summary>
public sealed class DotdigitalClient : IDisposable
{
    private readonly HttpClient _http;

    public DotdigitalClient(string baseUrl, string apiUser, string apiPassword)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiUser}:{apiPassword}")));
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Walks/creates the folder path (e.g. ["FixtureReports2026", "April"]) and
    /// returns the id of the deepest folder. When parentFolderId is null the
    /// first segment is looked up anywhere in the folder tree (and created at
    /// the root if absent).
    /// </summary>
    public async Task<long> GetOrCreateFolderPathAsync(long? parentFolderId, IReadOnlyList<string> path, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync("v2/document-folders", ct));
        JsonElement scope = doc.RootElement;

        long? currentId = parentFolderId;
        var remaining = new Queue<string>(path);

        if (parentFolderId is not null)
        {
            var parent = FindFolderById(doc.RootElement, parentFolderId.Value);
            scope = parent ?? throw new InvalidOperationException($"Dotdigital folder id {parentFolderId} not found.");
        }
        else
        {
            // No configured parent: locate the first segment anywhere in the tree.
            var first = remaining.Peek();
            var found = FindFolderByName(doc.RootElement, first);
            if (found is not null)
            {
                remaining.Dequeue();
                currentId = GetId(found.Value);
                scope = found.Value;
            }
        }

        while (remaining.Count > 0)
        {
            string name = remaining.Dequeue();
            JsonElement? child = currentId is null ? null : FindChildByName(scope, name);
            if (child is null && currentId is null)
                child = FindTopLevelByName(doc.RootElement, name);

            if (child is not null)
            {
                currentId = GetId(child.Value);
                scope = child.Value;
                continue;
            }

            currentId = await CreateFolderAsync(currentId, name, ct);
            // Newly created folder has no children; subsequent segments will all be created.
            scope = default;
        }

        return currentId ?? throw new InvalidOperationException("Could not resolve Dotdigital folder path.");
    }

    private async Task<long> CreateFolderAsync(long? parentId, string name, CancellationToken ct)
    {
        // POST v2/document-folders/{parentId} creates a subfolder; without a
        // parent the folder is created at the account root.
        string uri = parentId is null ? "v2/document-folders" : $"v2/document-folders/{parentId}";
        var response = await _http.PostAsJsonAsync(uri, new { Name = name }, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Dotdigital folder create '{name}' failed ({(int)response.StatusCode}): {body}");
        using var doc = JsonDocument.Parse(body);
        return GetId(doc.RootElement);
    }

    public async Task UploadDocumentAsync(long folderId, string fileName, byte[] content, CancellationToken ct = default)
    {
        // Same call shape the retired Logic App used.
        var payload = new { FileName = fileName, Data = Convert.ToBase64String(content) };
        for (int attempt = 1; ; attempt++)
        {
            var response = await _http.PostAsJsonAsync($"v2/document-folders/{folderId}/documents", payload, ct);
            if (response.IsSuccessStatusCode)
                return;

            string body = await response.Content.ReadAsStringAsync(ct);
            bool transient = (int)response.StatusCode >= 500 || (int)response.StatusCode == 429;
            if (!transient || attempt >= 4)
                throw new HttpRequestException($"Dotdigital upload '{fileName}' failed ({(int)response.StatusCode}): {body}");
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
        }
    }

    private static long GetId(JsonElement folder)
        => GetProp(folder, "id")?.GetInt64() ?? throw new InvalidOperationException("Folder response missing id.");

    private static JsonElement? GetProp(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in el.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        return null;
    }

    private static IEnumerable<JsonElement> Children(JsonElement folder)
    {
        var children = GetProp(folder, "childFolders");
        if (children is { ValueKind: JsonValueKind.Array })
            foreach (var c in children.Value.EnumerateArray())
                yield return c;
    }

    private static IEnumerable<JsonElement> TopLevel(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            foreach (var f in root.EnumerateArray())
                yield return f;
    }

    private static JsonElement? FindTopLevelByName(JsonElement root, string name)
    {
        foreach (var f in TopLevel(root))
            if (string.Equals(GetProp(f, "name")?.GetString(), name, StringComparison.OrdinalIgnoreCase))
                return f;
        return null;
    }

    private static JsonElement? FindChildByName(JsonElement folder, string name)
    {
        if (folder.ValueKind != JsonValueKind.Object) return null;
        foreach (var c in Children(folder))
            if (string.Equals(GetProp(c, "name")?.GetString(), name, StringComparison.OrdinalIgnoreCase))
                return c;
        return null;
    }

    private static JsonElement? FindFolderByName(JsonElement root, string name)
    {
        foreach (var f in TopLevel(root))
        {
            if (string.Equals(GetProp(f, "name")?.GetString(), name, StringComparison.OrdinalIgnoreCase))
                return f;
            var nested = FindInSubtree(f, name);
            if (nested is not null) return nested;
        }
        return null;

        static JsonElement? FindInSubtree(JsonElement folder, string name)
        {
            foreach (var c in Children(folder))
            {
                if (string.Equals(GetProp(c, "name")?.GetString(), name, StringComparison.OrdinalIgnoreCase))
                    return c;
                var nested = FindInSubtree(c, name);
                if (nested is not null) return nested;
            }
            return null;
        }
    }

    private static JsonElement? FindFolderById(JsonElement root, long id)
    {
        foreach (var f in TopLevel(root))
        {
            var found = FindInSubtree(f, id);
            if (found is not null) return found;
        }
        return null;

        static JsonElement? FindInSubtree(JsonElement folder, long id)
        {
            if (GetProp(folder, "id")?.GetInt64() == id) return folder;
            foreach (var c in Children(folder))
            {
                var found = FindInSubtree(c, id);
                if (found is not null) return found;
            }
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
