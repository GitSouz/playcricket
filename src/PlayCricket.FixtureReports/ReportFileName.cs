using System.Text;
using PlayCricket.FixtureReports.Models;

namespace PlayCricket.FixtureReports;

public static class ReportFileName
{
    /// <summary>
    /// Builds the report file name, preserving the historical convention:
    /// FixtureReport__{League_Name_With_Underscores}_{LeagueId}_{yyyyMMdd}.pdf
    /// with the same character sanitisation the old SSRS refresh applied
    /// (&amp; → and, / : + and u0026 handled, everything non-alphanumeric → _).
    /// </summary>
    public static string For(LeagueReport report, DateOnly generatedOn)
    {
        string name = report.LeagueName
            .Replace("u0026", "and")
            .Replace("&", "and")
            .Replace("+", " plus");

        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        string sanitised = sb.ToString();
        while (sanitised.Contains("__"))
            sanitised = sanitised.Replace("__", "_");
        sanitised = sanitised.Trim('_');

        return $"FixtureReport__{sanitised}_{report.LeagueId}_{generatedOn:yyyyMMdd}.pdf";
    }
}
