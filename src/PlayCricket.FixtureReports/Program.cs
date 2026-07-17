using System.Globalization;
using PlayCricket.FixtureReports;
using PlayCricket.FixtureReports.Data;
using PlayCricket.FixtureReports.Rendering;
using PlayCricket.FixtureReports.Templating;

// Usage:
//   dotnet run -- --month 2026-04 --sample [--output ./output]
//
// --month   Reporting month (yyyy-MM). Defaults to the previous calendar month.
// --sample  Use the built-in sample data source (no database required).
// --output  Output directory for generated PDFs. Defaults to ./output.

DateOnly reportMonth = DateOnly.FromDateTime(DateTime.Today).AddMonths(-1);
reportMonth = new DateOnly(reportMonth.Year, reportMonth.Month, 1);
string outputDir = "output";
bool useSample = false;

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
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 2;
    }
}

IReportDataSource dataSource = useSample
    ? new SampleDataSource()
    : throw new NotSupportedException("SQL data source arrives in Phase 2 — run with --sample for now.");

string baseDir = AppContext.BaseDirectory;
var htmlBuilder = new ReportHtmlBuilder(
    templateDir: Path.Combine(baseDir, "Templates"),
    chartJsPath: Path.Combine(baseDir, "Assets", "chart.umd.js"),
    logoPath: Path.Combine(baseDir, "Assets", "play-cricket-logo.png"));

Directory.CreateDirectory(outputDir);

var reports = await dataSource.GetReportsAsync(reportMonth);
Console.WriteLine($"Generating {reports.Count} report(s) for {reportMonth:MMMM yyyy}...");

await using var renderer = new PdfRenderer();
await renderer.InitialiseAsync();

var today = DateOnly.FromDateTime(DateTime.Today);
int failures = 0;
foreach (var report in reports)
{
    string fileName = ReportFileName.For(report, today);
    string path = Path.Combine(outputDir, fileName);
    try
    {
        string html = htmlBuilder.Build(report);
        await renderer.RenderAsync(html, path);
        Console.WriteLine($"  ok  {fileName} ({new FileInfo(path).Length / 1024} KB)");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"  FAIL {report.LeagueName} ({report.LeagueId}): {ex.Message}");
    }
}

Console.WriteLine(failures == 0
    ? $"Done — {reports.Count} PDF(s) written to {Path.GetFullPath(outputDir)}"
    : $"Completed with {failures} failure(s) out of {reports.Count}.");
return failures == 0 ? 0 : 1;
