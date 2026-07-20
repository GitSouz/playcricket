-- Season-to-date result counts per league per year, up to and including the
-- reporting month. Ported from SSRS.sp_Leagues_SeasonTD; season windows come
-- from lkp.Season exactly as before (C = current season, L = last season).
SELECT
    CASE f.League_ID WHEN 8205 THEN 15363 ELSE f.League_ID END AS LeagueId,
    f.Year_Of_Fixture AS YearOfFixture,
    SUM(CASE WHEN m.MappedResult = 'No Result'  THEN 1 ELSE 0 END) AS NoResult,
    SUM(CASE WHEN m.MappedResult = 'Abandoned'  THEN 1 ELSE 0 END) AS Abandoned,
    SUM(CASE WHEN m.MappedResult = 'Cancelled'  THEN 1 ELSE 0 END) AS Cancelled,
    SUM(CASE WHEN m.MappedResult = 'Conceded'   THEN 1 ELSE 0 END) AS Conceded,
    SUM(CASE WHEN m.MappedResult = 'InProgress' THEN 1 ELSE 0 END) AS InProgress,
    SUM(CASE WHEN m.MappedResult = 'Win'        THEN 1 ELSE 0 END) AS Win,
    SUM(CASE WHEN f.ShortSidedFixtureRef = 1    THEN 1 ELSE 0 END) AS ShortSided
FROM fact.Fixture f
JOIN lkp.Season s
  ON s.CurrLast IN ('C', 'L')
 AND f.match_date BETWEEN s.StartDate AND s.EndDate
CROSS APPLY (
    SELECT CASE ISNULL(f.Result, 'No Result')
                WHEN 'Drawn' THEN 'Win'
                WHEN 'Tie'   THEN 'Win'
                ELSE ISNULL(f.Result, 'No Result')
           END AS MappedResult
) m
WHERE MONTH(f.match_date) <= @ReportMonthNumber
  AND f.Result IS NOT NULL
  AND f.League_ID > 0
  AND f.League_ID <> 9039
  AND (f.Home_Club_Id NOT IN (9195, 11477, 8141, 12251)
       OR f.Away_Club_id NOT IN (9195, 11477, 8141, 12251))
GROUP BY CASE f.League_ID WHEN 8205 THEN 15363 ELSE f.League_ID END,
         f.Year_Of_Fixture;
