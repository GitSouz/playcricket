namespace PlayCricket.FixtureReports.Models;

/// <summary>All data needed to render one league's monthly fixture report.</summary>
public sealed class LeagueReport
{
    public required int LeagueId { get; init; }
    public required string LeagueName { get; init; }
    /// <summary>First day of the reporting month.</summary>
    public required DateOnly ReportMonth { get; init; }

    public required IReadOnlyList<YearStats> MonthHeadlines { get; init; }
    public required IReadOnlyList<YearStats> SeasonHeadlines { get; init; }
    public required IReadOnlyList<DivisionStats> MonthDivisions { get; init; }
    public required IReadOnlyList<DivisionStats> SeasonDivisions { get; init; }
    public required IReadOnlyList<CancelledWatchEntry> CancelledWatchList { get; init; }
    public required IReadOnlyList<ConcededWatchEntry> ConcededWatchList { get; init; }
}

/// <summary>Headline stats for one year (current year vs. prior year comparison rows).</summary>
public sealed class YearStats
{
    public required int Year { get; init; }
    public required int Completed { get; init; }
    public required decimal PlayedPct { get; init; }
    public required decimal CancelledPct { get; init; }
    public required decimal AbandonedPct { get; init; }
    public required decimal ConcededPct { get; init; }
    public required decimal ShortSidedPct { get; init; }
}

public sealed class DivisionStats
{
    public required string Division { get; init; }
    public required int Completed { get; init; }
    public required decimal PlayedPct { get; init; }
    public required decimal CancelledPct { get; init; }
    public required decimal AbandonedPct { get; init; }
    public required decimal ConcededPct { get; init; }
    public required decimal ShortSidedPct { get; init; }
}

/// <summary>Watch list #1: clubs/teams with 2+ games cancelled in the month.</summary>
public sealed class CancelledWatchEntry
{
    public required string HomeClubName { get; init; }
    public required string HomeTeamName { get; init; }
    public required int Cancelled { get; init; }
    public required int Abandoned { get; init; }
    public int Total => Cancelled + Abandoned;
}

/// <summary>Watch list #2: clubs/teams with 2+ games conceded or short-sided in the month.</summary>
public sealed class ConcededWatchEntry
{
    public required string ClubName { get; init; }
    public required string Team { get; init; }
    public required int Conceded { get; init; }
    public required int ShortSided { get; init; }
    public int Total => Conceded + ShortSided;
}
