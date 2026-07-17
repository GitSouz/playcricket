using PlayCricket.FixtureReports.Models;

namespace PlayCricket.FixtureReports.Data;

/// <summary>
/// In-memory data source used for local development and template work,
/// modelled on the real Warwickshire Cricket Foundation Women and Girls
/// Leagues report (league 29287) plus a second league with populated
/// watch lists so every template section can be previewed.
/// </summary>
public sealed class SampleDataSource : IReportDataSource
{
    public Task<IReadOnlyList<LeagueReport>> GetReportsAsync(DateOnly reportMonth, CancellationToken ct = default)
    {
        int year = reportMonth.Year;
        int priorYear = year - 1;

        var warwickshire = new LeagueReport
        {
            LeagueId = 29287,
            LeagueName = "Warwickshire Cricket Foundation Women and Girls Leagues",
            ReportMonth = reportMonth,
            MonthHeadlines =
            [
                new YearStats { Year = year, Completed = 17, PlayedPct = 100, CancelledPct = 0, AbandonedPct = 0, ConcededPct = 0, ShortSidedPct = 0 },
                new YearStats { Year = priorYear, Completed = 6, PlayedPct = 100, CancelledPct = 0, AbandonedPct = 0, ConcededPct = 0, ShortSidedPct = 0 },
            ],
            SeasonHeadlines =
            [
                new YearStats { Year = year, Completed = 17, PlayedPct = 100, CancelledPct = 0, AbandonedPct = 0, ConcededPct = 0, ShortSidedPct = 0 },
                new YearStats { Year = priorYear, Completed = 6, PlayedPct = 100, CancelledPct = 0, AbandonedPct = 0, ConcededPct = 0, ShortSidedPct = 0 },
            ],
            MonthDivisions =
            [
                new DivisionStats { Division = "Dynamos Girls Festivals", Completed = 14, PlayedPct = 100, CancelledPct = 0, AbandonedPct = 0, ConcededPct = 0, ShortSidedPct = 0 },
                new DivisionStats { Division = "Womens Softball Indoor League Finals Day", Completed = 3, PlayedPct = 100, CancelledPct = 0, AbandonedPct = 0, ConcededPct = 0, ShortSidedPct = 0 },
            ],
            SeasonDivisions =
            [
                new DivisionStats { Division = "Dynamos Girls Festivals", Completed = 14, PlayedPct = 100, CancelledPct = 0, AbandonedPct = 0, ConcededPct = 0, ShortSidedPct = 0 },
                new DivisionStats { Division = "Womens Softball Indoor League Finals Day", Completed = 3, PlayedPct = 100, CancelledPct = 0, AbandonedPct = 0, ConcededPct = 0, ShortSidedPct = 0 },
            ],
            CancelledWatchList = [],
            ConcededWatchList = [],
        };

        var sample = new LeagueReport
        {
            LeagueId = 12345,
            LeagueName = "Sample County Premier League",
            ReportMonth = reportMonth,
            MonthHeadlines =
            [
                new YearStats { Year = year, Completed = 128, PlayedPct = 86, CancelledPct = 9, AbandonedPct = 2, ConcededPct = 2, ShortSidedPct = 1 },
                new YearStats { Year = priorYear, Completed = 141, PlayedPct = 91, CancelledPct = 5, AbandonedPct = 1, ConcededPct = 2, ShortSidedPct = 1 },
            ],
            SeasonHeadlines =
            [
                new YearStats { Year = year, Completed = 342, PlayedPct = 84, CancelledPct = 10, AbandonedPct = 3, ConcededPct = 2, ShortSidedPct = 1 },
                new YearStats { Year = priorYear, Completed = 355, PlayedPct = 89, CancelledPct = 6, AbandonedPct = 2, ConcededPct = 2, ShortSidedPct = 1 },
            ],
            MonthDivisions =
            [
                new DivisionStats { Division = "Premier Division", Completed = 36, PlayedPct = 92, CancelledPct = 5, AbandonedPct = 0, ConcededPct = 3, ShortSidedPct = 0 },
                new DivisionStats { Division = "Division One", Completed = 34, PlayedPct = 88, CancelledPct = 9, AbandonedPct = 0, ConcededPct = 3, ShortSidedPct = 0 },
                new DivisionStats { Division = "Division Two", Completed = 30, PlayedPct = 83, CancelledPct = 10, AbandonedPct = 3, ConcededPct = 2, ShortSidedPct = 2 },
                new DivisionStats { Division = "Division Three", Completed = 28, PlayedPct = 79, CancelledPct = 14, AbandonedPct = 4, ConcededPct = 1, ShortSidedPct = 2 },
            ],
            SeasonDivisions =
            [
                new DivisionStats { Division = "Premier Division", Completed = 96, PlayedPct = 90, CancelledPct = 6, AbandonedPct = 1, ConcededPct = 3, ShortSidedPct = 0 },
                new DivisionStats { Division = "Division One", Completed = 90, PlayedPct = 86, CancelledPct = 9, AbandonedPct = 2, ConcededPct = 3, ShortSidedPct = 0 },
                new DivisionStats { Division = "Division Two", Completed = 82, PlayedPct = 82, CancelledPct = 11, AbandonedPct = 4, ConcededPct = 2, ShortSidedPct = 1 },
                new DivisionStats { Division = "Division Three", Completed = 74, PlayedPct = 77, CancelledPct = 15, AbandonedPct = 4, ConcededPct = 2, ShortSidedPct = 2 },
            ],
            CancelledWatchList =
            [
                new CancelledWatchEntry { HomeClubName = "Oakfield CC", HomeTeamName = "Oakfield CC - 2nd XI", Cancelled = 3, Abandoned = 0 },
                new CancelledWatchEntry { HomeClubName = "Riverside CC", HomeTeamName = "Riverside CC - 3rd XI", Cancelled = 2, Abandoned = 1 },
            ],
            ConcededWatchList =
            [
                new ConcededWatchEntry { ClubName = "Hillview CC", Team = "Hillview CC - 2nd XI", Conceded = 2, ShortSided = 1 },
            ],
        };

        return Task.FromResult<IReadOnlyList<LeagueReport>>([warwickshire, sample]);
    }
}
