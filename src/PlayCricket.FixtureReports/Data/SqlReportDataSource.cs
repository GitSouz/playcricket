using Microsoft.Data.SqlClient;
using PlayCricket.FixtureReports.Models;

namespace PlayCricket.FixtureReports.Data;

/// <summary>
/// Live data source computing all report figures directly from fact.Fixture,
/// replacing the legacy refresh procs + snapshot tables + SSRS views chain.
/// The queries in the Sql/ directory are line-for-line ports of the legacy
/// stored procedures (see docs/legacy-procs/ and docs/legacy-rdl-notes.md);
/// percentages use the formulas from the RDL layout.
///
/// Fully parameterised by reporting month, so any historical month can be
/// re-run — except the season-to-date windows, which still come from
/// lkp.Season rows flagged C (current) and L (last), as the legacy procs did.
/// </summary>
public sealed class SqlReportDataSource(string connectionString, string sqlDir) : IReportDataSource
{
    private sealed record Counts(int NoResult, int Abandoned, int Cancelled, int Conceded, int InProgress, int Win, int ShortSided)
    {
        /// <summary>Denominator per the RDL: ShortSided is intentionally excluded.</summary>
        public int Completed => NoResult + Abandoned + Cancelled + Conceded + InProgress + Win;
        public decimal Pct(int numerator) => Completed == 0 ? 0 : Math.Round(numerator * 100m / Completed);
        public decimal PlayedPct => Pct(NoResult + InProgress + Win);
    }

    public async Task<IReadOnlyList<LeagueReport>> GetReportsAsync(DateOnly reportMonth, CancellationToken ct = default)
    {
        var monthStart = new DateOnly(reportMonth.Year, reportMonth.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var prevYearMonthStart = monthStart.AddYears(-1);
        var prevYearMonthEnd = prevYearMonthStart.AddMonths(1).AddDays(-1);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var leagues = new List<(int Id, string Name)>();
        await using (var cmd = Command(conn, "LeagueList.sql", p =>
        {
            p.AddWithValue("@MonthStart", monthStart);
            p.AddWithValue("@MonthEnd", monthEnd);
        }))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                leagues.Add((Convert.ToInt32(reader.GetValue(0)), reader.GetString(1)));
        }

        var monthHeadlines = await ReadYearCountsAsync(conn, "HeadlineMonthCounts.sql", p =>
        {
            p.AddWithValue("@MonthStart", monthStart);
            p.AddWithValue("@MonthEnd", monthEnd);
            p.AddWithValue("@PrevYearMonthStart", prevYearMonthStart);
            p.AddWithValue("@PrevYearMonthEnd", prevYearMonthEnd);
        }, ct);

        var seasonHeadlines = await ReadYearCountsAsync(conn, "SeasonCounts.sql", p =>
        {
            p.AddWithValue("@ReportMonthNumber", reportMonth.Month);
        }, ct);

        var monthDivisions = await ReadDivisionCountsAsync(conn, "DivisionMonthCounts.sql", p =>
        {
            p.AddWithValue("@MonthStart", monthStart);
            p.AddWithValue("@MonthEnd", monthEnd);
        }, ct);

        var seasonDivisions = await ReadDivisionCountsAsync(conn, "DivisionSeasonCounts.sql", p =>
        {
            p.AddWithValue("@ReportMonthNumber", reportMonth.Month);
            p.AddWithValue("@ReportYear", reportMonth.Year);
        }, ct);

        var cancelledWatch = new Dictionary<int, List<CancelledWatchEntry>>();
        await using (var cmd = Command(conn, "WatchListCancelled.sql", p =>
        {
            p.AddWithValue("@MonthStart", monthStart);
            p.AddWithValue("@MonthEnd", monthEnd);
        }))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                Bucket(cancelledWatch, Convert.ToInt32(reader.GetValue(0))).Add(new CancelledWatchEntry
                {
                    HomeClubName = reader.GetString(1),
                    HomeTeamName = reader.GetString(2),
                    Cancelled = Convert.ToInt32(reader.GetValue(3)),
                    Abandoned = Convert.ToInt32(reader.GetValue(4)),
                });
        }

        var concededWatch = new Dictionary<int, List<ConcededWatchEntry>>();
        await using (var cmd = Command(conn, "WatchListConceded.sql", p =>
        {
            p.AddWithValue("@MonthStart", monthStart);
            p.AddWithValue("@MonthEnd", monthEnd);
        }))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                Bucket(concededWatch, Convert.ToInt32(reader.GetValue(0))).Add(new ConcededWatchEntry
                {
                    ClubName = reader.GetString(1),
                    Team = reader.GetString(2),
                    Conceded = Convert.ToInt32(reader.GetValue(3)),
                    ShortSided = Convert.ToInt32(reader.GetValue(4)),
                });
        }

        return leagues.Select(l => new LeagueReport
        {
            LeagueId = l.Id,
            LeagueName = l.Name,
            ReportMonth = reportMonth,
            MonthHeadlines = YearStatsFor(monthHeadlines, l.Id),
            SeasonHeadlines = YearStatsFor(seasonHeadlines, l.Id),
            MonthDivisions = DivisionStatsFor(monthDivisions, l.Id),
            SeasonDivisions = DivisionStatsFor(seasonDivisions, l.Id),
            CancelledWatchList = cancelledWatch.TryGetValue(l.Id, out var cw) ? cw : [],
            ConcededWatchList = concededWatch.TryGetValue(l.Id, out var xw) ? xw : [],
        }).ToList();
    }

    private SqlCommand Command(SqlConnection conn, string sqlFile, Action<SqlParameterCollection> addParams)
    {
        var cmd = new SqlCommand(File.ReadAllText(Path.Combine(sqlDir, sqlFile)), conn) { CommandTimeout = 300 };
        addParams(cmd.Parameters);
        return cmd;
    }

    private async Task<Dictionary<int, List<(int Year, Counts Counts)>>> ReadYearCountsAsync(
        SqlConnection conn, string sqlFile, Action<SqlParameterCollection> addParams, CancellationToken ct)
    {
        var result = new Dictionary<int, List<(int, Counts)>>();
        await using var cmd = Command(conn, sqlFile, addParams);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            Bucket(result, Convert.ToInt32(reader.GetValue(0)))
                .Add((Convert.ToInt32(reader.GetValue(1)), ReadCounts(reader, offset: 2)));
        return result;
    }

    private async Task<Dictionary<int, List<(string Division, Counts Counts)>>> ReadDivisionCountsAsync(
        SqlConnection conn, string sqlFile, Action<SqlParameterCollection> addParams, CancellationToken ct)
    {
        var result = new Dictionary<int, List<(string, Counts)>>();
        await using var cmd = Command(conn, sqlFile, addParams);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            Bucket(result, Convert.ToInt32(reader.GetValue(0)))
                .Add((reader.GetString(1), ReadCounts(reader, offset: 2)));
        return result;
    }

    private static List<T> Bucket<T>(Dictionary<int, List<T>> map, int key)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        return list;
    }

    private static IReadOnlyList<YearStats> YearStatsFor(Dictionary<int, List<(int Year, Counts Counts)>> map, int leagueId)
        => !map.TryGetValue(leagueId, out var rows)
            ? []
            : rows.OrderByDescending(r => r.Year).Select(r => new YearStats
            {
                Year = r.Year,
                Completed = r.Counts.Completed,
                PlayedPct = r.Counts.PlayedPct,
                CancelledPct = r.Counts.Pct(r.Counts.Cancelled),
                AbandonedPct = r.Counts.Pct(r.Counts.Abandoned),
                ConcededPct = r.Counts.Pct(r.Counts.Conceded),
                ShortSidedPct = r.Counts.Pct(r.Counts.ShortSided),
            }).ToList();

    private static IReadOnlyList<DivisionStats> DivisionStatsFor(Dictionary<int, List<(string Division, Counts Counts)>> map, int leagueId)
        => !map.TryGetValue(leagueId, out var rows)
            ? []
            : rows.OrderBy(r => r.Division).Select(r => new DivisionStats
            {
                Division = r.Division,
                Completed = r.Counts.Completed,
                PlayedPct = r.Counts.PlayedPct,
                CancelledPct = r.Counts.Pct(r.Counts.Cancelled),
                AbandonedPct = r.Counts.Pct(r.Counts.Abandoned),
                ConcededPct = r.Counts.Pct(r.Counts.Conceded),
                ShortSidedPct = r.Counts.Pct(r.Counts.ShortSided),
            }).ToList();

    private static Counts ReadCounts(SqlDataReader reader, int offset) => new(
        NoResult: Convert.ToInt32(reader.GetValue(offset)),
        Abandoned: Convert.ToInt32(reader.GetValue(offset + 1)),
        Cancelled: Convert.ToInt32(reader.GetValue(offset + 2)),
        Conceded: Convert.ToInt32(reader.GetValue(offset + 3)),
        InProgress: Convert.ToInt32(reader.GetValue(offset + 4)),
        Win: Convert.ToInt32(reader.GetValue(offset + 5)),
        ShortSided: Convert.ToInt32(reader.GetValue(offset + 6)));
}
