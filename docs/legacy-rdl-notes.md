# Legacy SSRS report (Leagues.rdl) — extracted logic

Reference notes from the `PlayCricket_SSRS/Leagues` RDL, which the new
generator reproduces. Parameters: `@LeagueName`, `@ClubId` (always 4).

## Where the logic lives

The RDL dataset queries are thin SELECTs — all real calculation logic is in
**`SSRS.*` views in the database** (now on Azure SQL), so the new pipeline
queries the same views:

| Report section | View |
|---|---|
| Headlines, reporting month (current vs prior year) | `SSRS.League_SeasonCurrLast` (+ `_PIVOT` for the chart) |
| Headlines, season to date | `SSRS.League_SeasonCurrLast_STD` (+ `_STD_PIVOT`) |
| Current-year month row (single league) | `SSRS.Leagues_ThisMonth` |
| Season-to-date row (single league) | `SSRS.Leagues_SeasonTD` |
| Divisional headlines, month | `SSRS.Leagues_Divisions_ThisMonth` |
| Divisional headlines, season to date | `SSRS.Leagues_Divisions_SeasonTD` |
| Watch list #1 (cancelled/abandoned) | `SSRS.Leagues_CanxAbandonened_Last5Games` |
| Watch list #2 (conceded/short-sided) | `SSRS.Leagues_ConcShortTeams_Last5Games` |
| League list | `SSRS.League_Sites` filtered by `SSRS.Leagues_ThisMonth` |
| MVP top-20 (dataset present but unused by fixture report pages) | `SSRS.PlayerMVP_ClubTop20_vw` |

All views expose the same count columns:
`[No Result], Abandoned, Cancelled, Conceded, InProgress, Win, ShortSided`.

## Percentage formulas (from RDL layout expressions)

```
Completed   = NoResult + Abandoned + Cancelled + Conceded + InProgress + Win
Played%     = (NoResult + InProgress + Win) / Completed
Cancelled%  = Cancelled / Completed
Abandoned%  = Abandoned / Completed
Conceded%   = Conceded  / Completed
ShortSided% = ShortSided / Completed        -- note: NOT part of the denominator
```

Year rows/series are split by tablix/chart filters
`Year_Of_Fixture = Year(Today)` and `Year(Today) - 1`.

The report title month is `MonthName(Month(DateAdd("m", -1, Now)))` — i.e. the
report always describes the **previous calendar month** relative to run time,
and the "ThisMonth"/"Last5Games" views are similarly self-relative to the run
date.

## Open items to verify against the database

1. **View definitions** — to confirm exactly how "this month" is derived and
   whether `League_SeasonCurrLast` is already restricted to the reporting
   month. Dump them with:

   ```sql
   SELECT s.name + '.' + v.name AS view_name, m.definition
   FROM sys.views v
   JOIN sys.schemas s ON s.schema_id = v.schema_id
   JOIN sys.sql_modules m ON m.object_id = v.object_id
   WHERE s.name = 'SSRS';
   ```

2. The "2 or more games" watch-list threshold is not applied in the RDL, so it
   must be inside the `*_Last5Games` views — verify from the definitions.
3. Long term: parameterising the views by month (table-valued functions or
   month-parameter procs) would allow re-running any historical month; today
   the pipeline must run within the month after the reporting month, exactly
   like the old process.
