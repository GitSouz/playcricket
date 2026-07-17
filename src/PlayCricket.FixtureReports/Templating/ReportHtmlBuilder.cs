using System.Globalization;
using System.Text.Json;
using PlayCricket.FixtureReports.Models;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace PlayCricket.FixtureReports.Templating;

/// <summary>Renders a LeagueReport into a self-contained HTML document (template + inlined Chart.js).</summary>
public sealed class ReportHtmlBuilder
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-GB");

    private readonly Template _template;
    private readonly string _chartJs;
    private readonly string _templateDir;

    public ReportHtmlBuilder(string templateDir, string chartJsPath)
    {
        _templateDir = templateDir;
        _template = Template.Parse(File.ReadAllText(Path.Combine(templateDir, "fixture-report.html")));
        if (_template.HasErrors)
            throw new InvalidOperationException($"Template parse errors: {string.Join("; ", _template.Messages)}");
        _chartJs = File.ReadAllText(chartJsPath);
    }

    public string Build(LeagueReport report)
    {
        string monthName = report.ReportMonth.ToString("MMMM", Culture);

        var model = new ScriptObject
        {
            ["LeagueName"] = report.LeagueName,
            ["MonthTitle"] = report.ReportMonth.ToString("MMMM-yyyy", Culture),
            ["MonthName"] = monthName,
            ["ChartJs"] = _chartJs,
            ["ChartDataJson"] = BuildChartJson(report),
            ["MonthHeadlines"] = report.MonthHeadlines.Select(YearRow).ToList(),
            ["SeasonHeadlines"] = report.SeasonHeadlines.Select(YearRow).ToList(),
            ["MonthDivisions"] = report.MonthDivisions.Select(DivisionRow).ToList(),
            ["SeasonDivisions"] = report.SeasonDivisions.Select(DivisionRow).ToList(),
            ["CancelledWatchList"] = report.CancelledWatchList.Select(r => (object)new ScriptObject
            {
                ["HomeClubName"] = r.HomeClubName,
                ["HomeTeamName"] = r.HomeTeamName,
                ["Cancelled"] = r.Cancelled,
                ["Abandoned"] = r.Abandoned,
                ["Total"] = r.Total,
            }).ToList(),
            ["ConcededWatchList"] = report.ConcededWatchList.Select(r => (object)new ScriptObject
            {
                ["ClubName"] = r.ClubName,
                ["Team"] = r.Team,
                ["Conceded"] = r.Conceded,
                ["ShortSided"] = r.ShortSided,
                ["Total"] = r.Total,
            }).ToList(),
        };

        var context = new TemplateContext { TemplateLoader = new FileTemplateLoader(_templateDir) };
        context.PushGlobal(model);
        return _template.Render(context);
    }

    private static object YearRow(YearStats s) => new ScriptObject
    {
        ["Year"] = s.Year,
        ["Completed"] = s.Completed,
        ["Played"] = Pct(s.PlayedPct),
        ["Cancelled"] = Pct(s.CancelledPct),
        ["Abandoned"] = Pct(s.AbandonedPct),
        ["Conceded"] = Pct(s.ConcededPct),
        ["ShortSided"] = Pct(s.ShortSidedPct),
    };

    private static object DivisionRow(DivisionStats s) => new ScriptObject
    {
        ["Division"] = s.Division,
        ["Completed"] = s.Completed,
        ["Played"] = Pct(s.PlayedPct),
        ["Cancelled"] = Pct(s.CancelledPct),
        ["Abandoned"] = Pct(s.AbandonedPct),
        ["Conceded"] = Pct(s.ConcededPct),
        ["ShortSided"] = Pct(s.ShortSidedPct),
    };

    private static string Pct(decimal value) => value.ToString("0", Culture) + "%";

    private static string BuildChartJson(LeagueReport report)
    {
        var labels = new[] { "1. Played", "2. Cancelled", "3. Abandoned", "4. Conceded", "5. ShortSided" };

        static object Series(IReadOnlyList<YearStats> stats) => new
        {
            labels = new[] { "1. Played", "2. Cancelled", "3. Abandoned", "4. Conceded", "5. ShortSided" },
            series = stats
                .OrderBy(s => s.Year)
                .Select(s => new
                {
                    label = s.Year.ToString(CultureInfo.InvariantCulture),
                    color = s.Year == stats.Max(x => x.Year) ? "#ee1c25" : "#f2c9cd",
                    values = new[] { s.PlayedPct, s.CancelledPct, s.AbandonedPct, s.ConcededPct, s.ShortSidedPct },
                })
                .ToList(),
        };

        return JsonSerializer.Serialize(new
        {
            month = Series(report.MonthHeadlines),
            season = Series(report.SeasonHeadlines),
        });
    }

    /// <summary>Resolves Scriban {{ include 'name' }} against the template directory.</summary>
    private sealed class FileTemplateLoader(string templateDir) : ITemplateLoader
    {
        public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName)
            => Path.Combine(templateDir, templateName + ".html");

        public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
            => File.ReadAllText(templatePath);

        public ValueTask<string?> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
            => new(File.ReadAllText(templatePath));
    }
}
