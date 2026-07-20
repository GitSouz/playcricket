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

## Confirmed from the SSRS view definitions (docs/legacy-views/)

1. **Month handling confirmed.** `League_SeasonCurrLast` filters
   `MonthofFixture = DATEPART(month, GETDATE()) - 1` (the previous calendar
   month) and `_STD` uses `<=` for season-to-date, so the SQL data source's
   per-year aggregation matches the report exactly. ⚠️ Note the **January
   bug** in the legacy views: `DATEPART(month, GETDATE()) - 1 = 0` in January,
   which matches no rows — the old report could never describe December. Fine
   for a summer season, but the new pipeline should compute the month
   boundary properly.
2. **The chart/percentage maths is confirmed identical** —
   `League_SeasonCurrLast_Pivot` computes `category / Completed` with
   `Completed = NoResult + Win + Abandoned + Cancelled + Conceded + InProgress`
   (ShortSided excluded), categories labelled `1. Played` … `5. ShortSided`.
3. **The year-view chain bottoms out in refreshed tables, not live data.**
   `League_SeasonCurrLast` unions two *year-specific tables* —
   `SSRS.Leagues_SeasonTD_2026` and `SSRS.Leagues_2025Season` — and the
   divisions view reads `SSRS.Leagues_Division_STD_2026`. These (plus
   `Leagues_ThisMonth`, `Leagues_SeasonTD`, `Leagues_Divisions_ThisMonth`,
   `Leagues_Divisions_SeasonTD`, `Leagues_CanxAbandonened_Last5Games`,
   `Leagues_ConcShortTeams_Last5Games`, `League_Sites`) are **tables populated
   by the SQL Agent refresh job**, not views — they did not appear in the
   sys.views export. The "2+ games" watch-list threshold therefore lives in
   whatever populates the `*_Last5Games` tables.
4. The league ID remap (8205 ↔ 15363, Northumberland rename) appears inside
   `League_SeasonCurrLast_STD` as well as in the refresh proc.

## Resolved: refresh procs captured and ported

The five `PlaycricketV2_SSRS_Refresh` procs are archived in
`docs/legacy-procs/` and their logic ported to month-parameterised queries in
`src/PlayCricket.FixtureReports/Sql/`, computed live from `fact.Fixture` — no
refresh step, no snapshot tables, no yearly table churn. Key behaviours
carried over:

- Result mapping `Drawn`/`Tie` → `Win`, `NULL` → `No Result`; only fixtures
  with a non-null `Result` are counted (so `No Result` is always 0).
- Short-sided = `ShortSidedFixtureRef = 1`.
- Watch lists: despite the legacy "Last5Games" naming, the window was the
  previous calendar month (the per-team ROW_NUMBER was computed but never
  used). Thresholds: cancelled `> 1`; conceded + short-sided `> 1`; games are
  attributed via `Cancelled_Club` / `Abandoned_Club` /
  `ISNULL(Conceded_Home, Conceded_Away)` / `ShortSidedTeamName`.
- Club exclusions preserved per query as the procs had them (9195 for month
  queries; 9195/11477/8141 for watch lists and division season; plus 12251
  and league 9039 for league season) — including their `OR` quirk, which in
  practice only excludes fixtures where *both* sides are excluded clubs.
- Season windows from `lkp.Season` (`CurrLast` = `C`/`L`) — still relative to
  run time; the reporting month itself is a real parameter, fixing the legacy
  January bug and making historical month re-runs possible within a season.
- League 8205 folded into 15363 and the Northumberland league rename applied
  inline; league names sanitised from `DIM.League_Sites` exactly as
  `SSRS.RefreshFixtureData` did (`&`→and, `/`→space, `:`→space, `+`→plus,
  `u0026`→and).
