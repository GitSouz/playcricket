namespace PlayCricket.FixtureReports.Validation;

public sealed record GeneratedReport(string FileName, string LocalPath, long SizeBytes, int LeagueId, string LeagueName);

/// <summary>
/// Automated replacement for the old "eyeball the smallest and largest file"
/// step: flags suspiciously small files and size outliers relative to the run.
/// </summary>
public static class RunValidator
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<GeneratedReport> reports, long minBytes = 20_000)
    {
        var warnings = new List<string>();
        if (reports.Count == 0)
        {
            warnings.Add("No reports were generated — expected at least one league.");
            return warnings;
        }

        long[] sizes = reports.Select(r => r.SizeBytes).Order().ToArray();
        long median = sizes[sizes.Length / 2];

        foreach (var r in reports)
        {
            if (r.SizeBytes < minBytes)
                warnings.Add($"{r.FileName}: only {r.SizeBytes / 1024} KB (minimum expected {minBytes / 1024} KB) — likely a failed/empty render.");
            else if (median > 0 && r.SizeBytes > median * 4)
                warnings.Add($"{r.FileName}: {r.SizeBytes / 1024} KB is more than 4x the run median ({median / 1024} KB) — worth a look.");
        }
        return warnings;
    }
}
