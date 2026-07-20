-- Watch list #1: clubs/teams with 2+ games cancelled in the reporting month,
-- with their abandoned count alongside. Ported from
-- SSRS.sp_Leagues_CanxConcShortTeams_Last5Games (whose window was, despite
-- the name, the previous calendar month; its per-team ROW_NUMBER was unused).
WITH month_games AS (
    SELECT Match_ID,
           CASE League_ID WHEN 8205 THEN 15363 ELSE League_ID END AS LeagueId,
           Home_Club_Id  AS Club_Id,   Home_Club_Name AS Club_Name,
           Home_Team_Id  AS Team_Id,   Home_Team_Name AS Team_Name,
           Cancelled, Cancelled_Club, Abandoned, Abandoned_Club
    FROM fact.Fixture
    WHERE match_date BETWEEN @MonthStart AND @MonthEnd
      AND Result IS NOT NULL AND League_ID > 0
      AND (Home_Club_Id NOT IN (9195, 11477, 8141) OR Away_Club_id NOT IN (9195, 11477, 8141))
    UNION
    SELECT Match_ID,
           CASE League_ID WHEN 8205 THEN 15363 ELSE League_ID END,
           Away_Club_Id, Away_Club_Name,
           Away_Team_Id, Away_Team_Name,
           Cancelled, Cancelled_Club, Abandoned, Abandoned_Club
    FROM fact.Fixture
    WHERE match_date BETWEEN @MonthStart AND @MonthEnd
      AND Result IS NOT NULL AND League_ID > 0
      AND (Home_Club_Id NOT IN (9195, 11477, 8141) OR Away_Club_id NOT IN (9195, 11477, 8141))
),
cancelled AS (
    SELECT LeagueId, Club_Id, Club_Name, Team_Id, Team_Name, SUM(Cancelled) AS Cancelled
    FROM month_games
    WHERE Cancelled >= 1 AND Club_Name = Cancelled_Club
    GROUP BY LeagueId, Club_Id, Club_Name, Team_Id, Team_Name
),
abandoned AS (
    SELECT Team_Id, SUM(Abandoned) AS Abandoned
    FROM month_games
    WHERE Abandoned >= 1 AND Club_Name = Abandoned_Club
    GROUP BY Team_Id
)
SELECT c.LeagueId, c.Club_Name, c.Team_Name, c.Cancelled, ISNULL(a.Abandoned, 0) AS Abandoned
FROM cancelled c
LEFT JOIN abandoned a ON a.Team_Id = c.Team_Id
WHERE c.Cancelled > 1
ORDER BY c.Club_Name, c.Team_Name;
