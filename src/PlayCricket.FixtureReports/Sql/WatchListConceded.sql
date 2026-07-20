-- Watch list #2: clubs/teams with 2+ games conceded or short-sided in the
-- reporting month. Ported from SSRS.sp_Leagues_CanxConcShortTeams_Last5Games.
WITH month_games AS (
    SELECT Match_ID,
           CASE League_ID WHEN 8205 THEN 15363 ELSE League_ID END AS LeagueId,
           Home_Club_Id  AS Club_Id,   Home_Club_Name AS Club_Name,
           Home_Team_Id  AS Team_Id,   Home_Team_Name AS Team_Name,
           Conceded, Conceded_Home, Conceded_Away,
           ShortSidedFixtureRef, ShortSidedTeamName
    FROM fact.Fixture
    WHERE match_date BETWEEN @MonthStart AND @MonthEnd
      AND Result IS NOT NULL AND League_ID > 0
      AND (Home_Club_Id NOT IN (9195, 11477, 8141) OR Away_Club_id NOT IN (9195, 11477, 8141))
    UNION
    SELECT Match_ID,
           CASE League_ID WHEN 8205 THEN 15363 ELSE League_ID END,
           Away_Club_Id, Away_Club_Name,
           Away_Team_Id, Away_Team_Name,
           Conceded, Conceded_Home, Conceded_Away,
           ShortSidedFixtureRef, ShortSidedTeamName
    FROM fact.Fixture
    WHERE match_date BETWEEN @MonthStart AND @MonthEnd
      AND Result IS NOT NULL AND League_ID > 0
      AND (Home_Club_Id NOT IN (9195, 11477, 8141) OR Away_Club_id NOT IN (9195, 11477, 8141))
),
conceded AS (
    SELECT LeagueId, Club_Id, Club_Name, Team_Id, Team_Name, ISNULL(SUM(Conceded), 0) AS Conceded
    FROM month_games
    WHERE Conceded >= 1 AND Club_Name = ISNULL(Conceded_Home, Conceded_Away)
    GROUP BY LeagueId, Club_Id, Club_Name, Team_Id, Team_Name
),
short_sided AS (
    SELECT LeagueId, Club_Id, Club_Name, Team_Id, Team_Name, COUNT(ShortSidedFixtureRef) AS ShortSided
    FROM month_games
    WHERE ShortSidedFixtureRef = 1 AND Club_Name = ShortSidedTeamName
    GROUP BY LeagueId, Club_Id, Club_Name, Team_Id, Team_Name
)
SELECT ISNULL(c.LeagueId,  s.LeagueId)  AS LeagueId,
       ISNULL(c.Club_Name, s.Club_Name) AS Club_Name,
       ISNULL(c.Team_Name, s.Team_Name) AS Team_Name,
       ISNULL(c.Conceded, 0)            AS Conceded,
       ISNULL(s.ShortSided, 0)          AS ShortSided
FROM conceded c
FULL OUTER JOIN short_sided s ON s.Team_Id = c.Team_Id
WHERE ISNULL(c.Conceded, 0) + ISNULL(s.ShortSided, 0) > 1
ORDER BY ISNULL(c.Club_Name, s.Club_Name), ISNULL(c.Team_Name, s.Team_Name);
