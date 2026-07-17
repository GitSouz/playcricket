using PlayCricket.FixtureReports.Models;

namespace PlayCricket.FixtureReports.Data;

public interface IReportDataSource
{
    /// <summary>Returns one fully-populated report per league due a report for the given month.</summary>
    Task<IReadOnlyList<LeagueReport>> GetReportsAsync(DateOnly reportMonth, CancellationToken ct = default);
}
