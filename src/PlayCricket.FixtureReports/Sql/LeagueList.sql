-- Leagues receiving a report: any league with a completed fixture in the
-- reporting month. Name sanitisation and the Northumberland rename replicate
-- the legacy SSRS.RefreshFixtureData behaviour; league 8205 is folded into
-- 15363 as that proc did.
WITH month_fixtures AS (
    SELECT DISTINCT CASE League_ID WHEN 8205 THEN 15363 ELSE League_ID END AS LeagueId
    FROM fact.Fixture
    WHERE match_date BETWEEN @MonthStart AND @MonthEnd
      AND Result IS NOT NULL
      AND League_ID > 0
      AND (Home_Club_Id <> 9195 OR Away_Club_id <> 9195)
)
SELECT mf.LeagueId,
       CASE s.CleanName
            WHEN 'Northumberland and Tyneside Senior Cricket League'
            THEN 'Northumberland and Tyneside Cricket League'
            ELSE s.CleanName
       END AS LeagueName
FROM month_fixtures mf
JOIN DIM.League_Sites ls ON ls.ID_League = mf.LeagueId
CROSS APPLY (
    SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ls.Name,
           '&', 'and'), '/', ' '), ':', ' '), '+', ' plus'), 'u0026', 'and') AS CleanName
) s
ORDER BY LeagueName;
