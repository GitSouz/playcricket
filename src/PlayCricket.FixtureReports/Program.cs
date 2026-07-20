using System.Globalization;
using PlayCricket.FixtureReports;
using PlayCricket.FixtureReports.Data;
using PlayCricket.FixtureReports.Rendering;
using PlayCricket.FixtureReports.Templating;
using PlayCricket.FixtureReports.Upload;
using PlayCricket.FixtureReports.Validation;

// Usage:
//   dotnet run -- [--month 2026-04] [--sample] [--output ./output] [--upload]
//
// --month   Reporting month (yyyy-MM). Defaults to the previous calendar month.
// --sample  Use the built-in sample data source (no database required).
// --output  Output directory for generated PDFs. Defaults to ./output.
// --upload  After generating, archive PDFs to the Azure file share and upload
//           them to Dotdigital (requires the environment variables below).
//
// Environment variables:
//   PLAYCRICKET_SQL_CONNECTION   Azure SQL connection string (omit with --sample).
//                                Use "Authentication=Active Directory Default"
//                                for managed identity.
//   ARCHIVE_STORAGE_CONNECTION   Storage connection string for the file share.
//   ARCHIVE_SHARE                File share name        (default: development)
//   ARCHIVE_BASE_PATH            Base directory in share (default: PlayCricket)
//   DOTDIGITAL_BASE_URL          API base                (default: https://r1-api.dotmailer.com)
//   DOTDIGITAL_API_USER          API user email
//   DOTDIGITAL_API_PASSWORD     API user password
//   DOTDIGITAL_PARENT_FOLDER_ID  Optional folder id under which FixtureReports{yyyy} lives
//   CHROMIUM_EXECUTABLE          Optional path to a Chromium binary

DateOnly reportMonth = DateOnly.FromDateTime(DateTime.Today).AddMonths(-1);
reportMonth = new DateOnly(reportMonth.Year, reportMonth.Month, 1);
string outputDir = "output";
bool useSample = false;
bool upload = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--month":
            reportMonth = DateOnly.ParseExact(args[++i] + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            break;
        case "--output":
            outputDir = args[++i];
            break;
        case "--sample":
            useSample = true;
            break;
        case "--upload":
            upload = true;
            break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 2;
    }
}

IReportDataSource dataSource;
if (useSample)
{
    dataSource = new SampleDataSource();
}
else
{
    string? connectionString = Environment.GetEnvironmentVariable("PLAYCRICKET_SQL_CONNECTION");
    if (string.IsNullOrEmpty(connectionString))
    {
        Console.Error.WriteLine("PLAYCRICKET_SQL_CONNECTION is not set. Set it, or run with --sample.");
        return 2;
    }
    dataSource = new SqlReportDataSource(connectionString, Path.Combine(AppContext.BaseDirectory, "Sql"));
}

// Fail fast on missing upload configuration before doing any expensive work.
FileShareArchive? archive = null;
DotdigitalClient? dotdigital = null;
long? dotdigitalParentFolderId = null;
if (upload)
{
    string? storageConnection = Environment.GetEnvironmentVariable("ARCHIVE_STORAGE_CONNECTION");
    string? ddUser = Environment.GetEnvironmentVariable("DOTDIGITAL_API_USER");
    string? ddPassword = Environment.GetEnvironmentVariable("DOTDIGITAL_API_PASSWORD");
    if (string.IsNullOrEmpty(storageConnection) || string.IsNullOrEmpty(ddUser) || string.IsNullOrEmpty(ddPassword))
    {
        Console.Error.WriteLine("--upload requires ARCHIVE_STORAGE_CONNECTION, DOTDIGITAL_API_USER and DOTDIGITAL_API_PASSWORD.");
        return 2;
    }
    archive = new FileShareArchive(
        storageConnection,
        Environment.GetEnvironmentVariable("ARCHIVE_SHARE") ?? "development",
        Environment.GetEnvironmentVariable("ARCHIVE_BASE_PATH") ?? "PlayCricket");
    dotdigital = new DotdigitalClient(
        Environment.GetEnvironmentVariable("DOTDIGITAL_BASE_URL") ?? "https://r1-api.dotmailer.com",
        ddUser, ddPassword);
    string? parentIdRaw = Environment.GetEnvironmentVariable("DOTDIGITAL_PARENT_FOLDER_ID");
    if (long.TryParse(parentIdRaw, out long parentId))
        dotdigitalParentFolderId = parentId;
}

string baseDir = AppContext.BaseDirectory;
var htmlBuilder = new ReportHtmlBuilder(
    templateDir: Path.Combine(baseDir, "Templates"),
    chartJsPath: Path.Combine(baseDir, "Assets", "chart.umd.js"),
    logoPath: Path.Combine(baseDir, "Assets", "play-cricket-logo.png"));

Directory.CreateDirectory(outputDir);

var reports = await dataSource.GetReportsAsync(reportMonth);
Console.WriteLine($"Generating {reports.Count} report(s) for {reportMonth.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"))}...");

await using var renderer = new PdfRenderer();
await renderer.InitialiseAsync();

var today = DateOnly.FromDateTime(DateTime.Today);
var generated = new List<GeneratedReport>();
var failures = new List<string>();
foreach (var report in reports)
{
    string fileName = ReportFileName.For(report, today);
    string path = Path.Combine(outputDir, fileName);
    try
    {
        string html = htmlBuilder.Build(report);
        await renderer.RenderAsync(html, path);
        long size = new FileInfo(path).Length;
        generated.Add(new GeneratedReport(fileName, path, size, report.LeagueId, report.LeagueName));
        Console.WriteLine($"  ok  {fileName} ({size / 1024} KB)");
    }
    catch (Exception ex)
    {
        failures.Add($"{report.LeagueName} ({report.LeagueId}): {ex.Message}");
        Console.Error.WriteLine($"  FAIL {report.LeagueName} ({report.LeagueId}): {ex.Message}");
    }
}

var warnings = RunValidator.Validate(generated);

if (upload && generated.Count > 0)
{
    Console.WriteLine("Archiving to file share...");
    foreach (var r in generated)
    {
        try
        {
            string sharePath = await archive!.UploadAsync(reportMonth, r.FileName, r.LocalPath);
            Console.WriteLine($"  ok  {sharePath}");
        }
        catch (Exception ex)
        {
            failures.Add($"Archive {r.FileName}: {ex.Message}");
            Console.Error.WriteLine($"  FAIL archive {r.FileName}: {ex.Message}");
        }
    }

    Console.WriteLine("Uploading to Dotdigital...");
    try
    {
        string monthName = reportMonth.ToString("MMMM", CultureInfo.GetCultureInfo("en-GB"));
        long folderId = await dotdigital!.GetOrCreateFolderPathAsync(
            dotdigitalParentFolderId, [$"FixtureReports{reportMonth.Year}", monthName]);
        Console.WriteLine($"  Dotdigital folder id {folderId} (FixtureReports{reportMonth.Year}/{monthName})");
        foreach (var r in generated)
        {
            try
            {
                await dotdigital.UploadDocumentAsync(folderId, r.FileName, await File.ReadAllBytesAsync(r.LocalPath));
                Console.WriteLine($"  ok  {r.FileName}");
            }
            catch (Exception ex)
            {
                failures.Add($"Dotdigital {r.FileName}: {ex.Message}");
                Console.Error.WriteLine($"  FAIL dotdigital {r.FileName}: {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        failures.Add($"Dotdigital folder setup: {ex.Message}");
        Console.Error.WriteLine($"  FAIL dotdigital folder setup: {ex.Message}");
    }
}

dotdigital?.Dispose();

Console.WriteLine();
Console.WriteLine($"=== Run summary: {reportMonth:yyyy-MM} ===");
Console.WriteLine($"Reports generated: {generated.Count}/{reports.Count}");
foreach (string warning in warnings)
    Console.WriteLine($"  WARN {warning}");
foreach (string failure in failures)
    Console.WriteLine($"  FAIL {failure}");
Console.WriteLine(failures.Count == 0 && generated.Count == reports.Count
    ? "Result: SUCCESS"
    : "Result: FAILED");

return failures.Count == 0 && generated.Count == reports.Count ? 0 : 1;
