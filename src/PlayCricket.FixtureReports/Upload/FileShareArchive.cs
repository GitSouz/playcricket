using Azure.Storage.Files.Shares;

namespace PlayCricket.FixtureReports.Upload;

/// <summary>
/// Archives generated PDFs to the Azure file share
/// (externallysharedfiles / development / PlayCricket / FixtureReports{yyyy}/{Month}),
/// replacing the manual copy step. Directory path is computed per run.
/// </summary>
public sealed class FileShareArchive(string connectionString, string shareName, string basePath)
{
    /// <summary>Uploads a file, creating the month directory tree as needed. Returns the share path.</summary>
    public async Task<string> UploadAsync(DateOnly reportMonth, string fileName, string localPath, CancellationToken ct = default)
    {
        var share = new ShareClient(connectionString, shareName);
        var dir = share.GetRootDirectoryClient();
        string[] segments =
        [
            .. basePath.Split('/', StringSplitOptions.RemoveEmptyEntries),
            $"FixtureReports{reportMonth.Year}",
            reportMonth.ToString("MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")),
        ];
        foreach (string segment in segments)
        {
            dir = dir.GetSubdirectoryClient(segment);
            await dir.CreateIfNotExistsAsync(cancellationToken: ct);
        }

        var file = dir.GetFileClient(fileName);
        await using var stream = File.OpenRead(localPath);
        await file.CreateAsync(stream.Length, cancellationToken: ct);
        await file.UploadAsync(stream, cancellationToken: ct);
        return $"{shareName}/{string.Join('/', segments)}/{fileName}";
    }
}
