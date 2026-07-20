using Microsoft.Data.SqlClient;
using PlayCricket.FixtureReports.Models;

namespace PlayCricket.FixtureReports.Data;

/// <summary>
/// Live data source querying the same SSRS.* views the old SSRS report used
/// (see docs/legacy-rdl-notes.md for the mapping and formulas).
///
/// Like the old report, the month views (Leagues_ThisMonth, *_Last5Games) are
/// self-relative to the run date, so this source describes the current
/// reporting window regardless of the --month argument; the argument is used
/// for labelling. Parameterising the views by month is a planned follow-up.
/// </summary>
public sealed class SqlReportDataSource(string connectionString) : IReportDataSource
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
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var leagues = new List<(int Id, string Name)>();
        await using (var cmd = new SqlCommand(
            """
            SELECT ls.ID_League, ls.Name
            FROM SSRS.League_Sites ls
            WHERE ls.ID_League IN (SELECT League_ID FROM SSRS.Leagues_ThisMonth)
            ORDER BY ls.Name
            """, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                leagues.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        var reports = new List<LeagueReport>(leagues.Count);
        foreach (var (id, name) in leagues)
            reports.Add(await LoadLeagueAsync(conn, id, name, reportMonth, ct));
        return reports;
    }

    private static async Task<LeagueReport> LoadLeagueAsync(SqlConnection conn, int leagueId, string leagueName, DateOnly reportMonth, CancellationToken ct)
    {
        int currentYear = reportMonth.Year;

        var monthHeadlines = await QueryYearStatsAsync(conn,
            """
            SELECT Year_Of_Fixture,
                   SUM(ISNULL([No Result], 0)), SUM(ISNULL(Abandoned, 0)), SUM(ISNULL(Cancelled, 0)),
                   SUM(ISNULL(Conceded, 0)), SUM(ISNULL(InProgress, 0)), SUM(ISNULL(Win, 0)), SUM(ISNULL(ShortSided, 0))
            FROM SSRS.League_SeasonCurrLast
            WHERE Name = @LeagueName AND Year_Of_Fixture IN (@Year, @Year - 1)
            GROUP BY Year_Of_Fixture
            ORDER BY Year_Of_Fixture DESC
            """, leagueName, currentYear, ct);

        var seasonHeadlines = await QueryYearStatsAsync(conn,
            """
            SELECT Year_of_Fixture,
                   SUM(ISNULL([No Result], 0)), SUM(ISNULL(Abandoned, 0)), SUM(ISNULL(Cancelled, 0)),
                   SUM(ISNULL(Conceded, 0)), SUM(ISNULL(InProgress, 0)), SUM(ISNULL(Win, 0)), SUM(ISNULL(ShortSided, 0))
            FROM SSRS.League_SeasonCurrLast_STD
            WHERE Name = @LeagueName AND Year_of_Fixture IN (@Year, @Year - 1)
            GROUP BY Year_of_Fixture
            ORDER BY Year_of_Fixture DESC
            """, leagueName, currentYear, ct);

        var monthDivisions = await QueryDivisionStatsAsync(conn,
            """
            SELECT Division_Of_Fixture,
                   ISNULL([No Result], 0), ISNULL(Abandoned, 0), ISNULL(Cancelled, 0),
                   ISNULL(Conceded, 0), ISNULL(InProgress, 0), ISNULL(Win, 0), ISNULL(ShortSided, 0)
            FROM SSRS.Leagues_Divisions_ThisMonth
            WHERE Name = @LeagueName
            ORDER BY Division_Of_Fixture
            """, leagueName, ct);

        var seasonDivisions = await QueryDivisionStatsAsync(conn,
            """
            SELECT Division_Of_Fixture,
                   ISNULL([No Result], 0), ISNULL(Abandoned, 0), ISNULL(Cancelled, 0),
                   ISNULL(Conceded, 0), ISNULL(InProgress, 0), ISNULL(Win, 0), ISNULL(ShortSided, 0)
            FROM SSRS.Leagues_Divisions_SeasonTD
            WHERE Name = @LeagueName
            ORDER BY Division_Of_Fixture
            """, leagueName, ct);

        var cancelledWatch = new List<CancelledWatchEntry>();
        await using (var cmd = new SqlCommand(
            """
            SELECT Home_Club_Name, Home_Team_Name, Cancelled, ISNULL(Abandoned, 0)
            FROM SSRS.Leagues_CanxAbandonened_Last5Games
            WHERE Name = @LeagueName
            ORDER BY Home_Club_Name, Home_Team_Name
            """, conn))
        {
            cmd.Parameters.AddWithValue("@LeagueName", leagueName);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                cancelledWatch.Add(new CancelledWatchEntry
                {
                    HomeClubName = reader.GetString(0),
                    HomeTeamName = reader.GetString(1),
                    Cancelled = Convert.ToInt32(reader.GetValue(2)),
                    Abandoned = Convert.ToInt32(reader.GetValue(3)),
                });
        }

        var concededWatch = new List<ConcededWatchEntry>();
        await using (var cmd = new SqlCommand(
            """
            SELECT Home_Club_Name, Home_Team_Name, Conceded, ISNULL(ShortSided, 0)
            FROM SSRS.Leagues_ConcShortTeams_Last5Games
            WHERE Name = @LeagueName
            ORDER BY Home_Club_Name, Home_Team_Name
            """, conn))
        {
            cmd.Parameters.AddWithValue("@LeagueName", leagueName);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                concededWatch.Add(new ConcededWatchEntry
                {
                    ClubName = reader.GetString(0),
                    Team = reader.GetString(1),
                    Conceded = Convert.ToInt32(reader.GetValue(2)),
                    ShortSided = Convert.ToInt32(reader.GetValue(3)),
                });
        }

        return new LeagueReport
        {
            LeagueId = leagueId,
            LeagueName = leagueName,
            ReportMonth = reportMonth,
            MonthHeadlines = monthHeadlines,
            SeasonHeadlines = seasonHeadlines,
            MonthDivisions = monthDivisions,
            SeasonDivisions = seasonDivisions,
            CancelledWatchList = cancelledWatch,
            ConcededWatchList = concededWatch,
        };
    }

    private static async Task<IReadOnlyList<YearStats>> QueryYearStatsAsync(SqlConnection conn, string sql, string leagueName, int year, CancellationToken ct)
    {
        var result = new List<YearStats>();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@LeagueName", leagueName);
        cmd.Parameters.AddWithValue("@Year", year);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var c = ReadCounts(reader, offset: 1);
            result.Add(new YearStats
            {
                Year = Convert.ToInt32(reader.GetValue(0)),
                Completed = c.Completed,
                PlayedPct = c.PlayedPct,
                CancelledPct = c.Pct(c.Cancelled),
                AbandonedPct = c.Pct(c.Abandoned),
                ConcededPct = c.Pct(c.Conceded),
                ShortSidedPct = c.Pct(c.ShortSided),
            });
        }
        return result;
    }

    private static async Task<IReadOnlyList<DivisionStats>> QueryDivisionStatsAsync(SqlConnection conn, string sql, string leagueName, CancellationToken ct)
    {
        var result = new List<DivisionStats>();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@LeagueName", leagueName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var c = ReadCounts(reader, offset: 1);
            result.Add(new DivisionStats
            {
                Division = reader.GetString(0),
                Completed = c.Completed,
                PlayedPct = c.PlayedPct,
                CancelledPct = c.Pct(c.Cancelled),
                AbandonedPct = c.Pct(c.Abandoned),
                ConcededPct = c.Pct(c.Conceded),
                ShortSidedPct = c.Pct(c.ShortSided),
            });
        }
        return result;
    }

    private static Counts ReadCounts(SqlDataReader reader, int offset) => new(
        NoResult: Convert.ToInt32(reader.GetValue(offset)),
        Abandoned: Convert.ToInt32(reader.GetValue(offset + 1)),
        Cancelled: Convert.ToInt32(reader.GetValue(offset + 2)),
        Conceded: Convert.ToInt32(reader.GetValue(offset + 3)),
        InProgress: Convert.ToInt32(reader.GetValue(offset + 4)),
        Win: Convert.ToInt32(reader.GetValue(offset + 5)),
        ShortSided: Convert.ToInt32(reader.GetValue(offset + 6)));
}
