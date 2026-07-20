# Legacy SSIS package — extracted logic

Reference notes from `DMSSRS_FixtureReports.dtsx` (the package being retired),
captured so the Phase 2 SQL data source reproduces its behaviour.

## What the package did

1. **Execute SQL Task** — league list for the current month:

   ```sql
   SELECT 4 AS ClubID,
          ID_League AS LeagueId,
          Name      AS LeagueName
   FROM [SSRS].League_Sites
   WHERE ID_League IN (SELECT League_ID FROM [SSRS].[Leagues_ThisMonth])
   ```

2. **Foreach loop** over that result set; for each league a **Script Task**
   downloaded the rendered SSRS report as PDF:

   - URL: `http://vm-uks-d-sql-02/ReportServer_SSRS?/PlayCricket_SSRS/Leagues`
     with parameters `LeagueName`, `ClubId` (always `4`), `rs:Format=PDF`
   - Saved to: `{Folder_Destination}\FixtureReport_{LeagueName}_{LeagueId}_{yyyyMMdd}.pdf`
   - `Folder_Destination` was the hand-edited monthly path
     (`...\SSRS\Reports\FixtureReports{yyyy}\{Month}\`) — replaced by a
     computed path in the new pipeline.

## Implications for the new pipeline

- **League selection** comes from `SSRS.League_Sites` filtered by
  `SSRS.Leagues_ThisMonth` (definition still to be captured).
- **`ClubId` is a constant 4** — the SSRS report took it as a parameter but the
  package never varied it.
- The report itself was parameterised by **league name** (not ID) — the ID only
  appears in the file name.
- All stat calculations (played/cancelled/abandoned/conceded/short-sided
  percentages, divisional splits, watch lists) live in the **`Leagues` RDL
  dataset queries** on the report server — the RDL must be exported to port
  them exactly.
- The script task swallowed download errors silently (empty `Catch`) — failed
  reports simply produced truncated/empty files, which is why the manual
  "check smallest/largest file size" validation step existed. The new
  pipeline's per-league error handling + count/size validation replaces this.
