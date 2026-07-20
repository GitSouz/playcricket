-- Reporting-month result counts per league, for the report year and the same
-- month a year earlier. Ported from SSRS.sp_Leagues_ThisMonth (current year)
-- and the Leagues_{prior}Season snapshot the League_SeasonCurrLast view
-- unioned in (prior year), computed live from fact.Fixture instead.
SELECT
    CASE f.League_ID WHEN 8205 THEN 15363 ELSE f.League_ID END AS LeagueId,
    YEAR(f.match_date) AS YearOfFixture,
    SUM(CASE WHEN m.MappedResult = 'No Result'  THEN 1 ELSE 0 END) AS NoResult,
    SUM(CASE WHEN m.MappedResult = 'Abandoned'  THEN 1 ELSE 0 END) AS Abandoned,
    SUM(CASE WHEN m.MappedResult = 'Cancelled'  THEN 1 ELSE 0 END) AS Cancelled,
    SUM(CASE WHEN m.MappedResult = 'Conceded'   THEN 1 ELSE 0 END) AS Conceded,
    SUM(CASE WHEN m.MappedResult = 'InProgress' THEN 1 ELSE 0 END) AS InProgress,
    SUM(CASE WHEN m.MappedResult = 'Win'        THEN 1 ELSE 0 END) AS Win,
    SUM(CASE WHEN f.ShortSidedFixtureRef = 1    THEN 1 ELSE 0 END) AS ShortSided
FROM fact.Fixture f
CROSS APPLY (
    SELECT CASE ISNULL(f.Result, 'No Result')
                WHEN 'Drawn' THEN 'Win'
                WHEN 'Tie'   THEN 'Win'
                ELSE ISNULL(f.Result, 'No Result')
           END AS MappedResult
) m
WHERE (f.match_date BETWEEN @MonthStart AND @MonthEnd
       OR f.match_date BETWEEN @PrevYearMonthStart AND @PrevYearMonthEnd)
  AND f.Result IS NOT NULL
  AND f.League_ID > 0
  AND (f.Home_Club_Id <> 9195 OR f.Away_Club_id <> 9195)
GROUP BY CASE f.League_ID WHEN 8205 THEN 15363 ELSE f.League_ID END,
         YEAR(f.match_date);
