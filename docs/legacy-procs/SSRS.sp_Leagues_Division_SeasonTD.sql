USE [PlaycricketV2]
GO
/****** Object:  StoredProcedure [SSRS].[sp_Leagues_Division_SeasonTD]    Script Date: 7/20/2026 11:37:51 AM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


ALTER Proc [SSRS].[sp_Leagues_Division_SeasonTD]
as

Declare @EndDate date = (SELECT EndDate
		   FROM lkp.Season
		   WHERE CurrLast = 'C'
		  )

Declare @StartDate date =  (SELECT StartDate
			 FROM lkp.Season
			 WHERE CurrLast = 'C'
			 )

Truncate Table SSRS.Leagues_Division_STD_2026;

Insert into SSRS.Leagues_Division_STD_2026
SELECT  a.League_ID, 
D.[Name],
a.Division_Of_Fixture, [No Result],a.Year_Of_Fixture, a.MonthofFixture, Abandoned, Cancelled,Conceded, InProgress, Win ,b.ShortSided

FROM 
(
SELECT 
	League_ID,
	
	case isnull(result, 'No Result')
		when 'Drawn' then 'Win'
		When 'Tie' then 'Win'
		Else isnull(result, 'No Result')
	End
	 Result,
	 Division_Of_Fixture, 
	 YEAR_of_Fixture, 
	 MONTH(Match_Date) As MonthofFixture,

	Count(*) ScheduledFixtures
FROM fact.Fixture
WHERE match_date between @StartDate and @EndDate
and (Home_Club_Id not in (9195,11477,8141) or Away_Club_id not in (9195,11477,8141))
--and League_Of_Fixture <> 'CUP FIXTURE'
		and Result is not null
		and League_ID > 0 
GROUP BY League_ID,  result,
YEAR_of_Fixture, 
Division_Of_Fixture,
	 MONTH(Match_Date)
)PVT
Pivot
(SUM(ScheduledFixtures) For Result in (
[No Result],
[Abandoned],
[Cancelled],
[Conceded],
[InProgress],
[Win]
))A

Left outer join 
-------------------------------------------------------------------------
(
SELECT 
	League_ID, 
	YEAR_of_Fixture, 
	Division_Of_Fixture, 
	 MONTH(Match_Date) as MonthofFixture,
	Count(*) ShortSided
FROM 
	fact.Fixture

WHERE 
	match_date between @StartDate and @EndDate
	and ShortSidedFixtureRef in (1)--,2)
	and (Home_Club_Id not in (9195,11477,8141) or Away_Club_id not in (9195,11477,8141))
	--and League_Of_Fixture <> 'CUP FIXTURE'
			and Result is not null
			and League_ID > 0 
GROUP BY  
	League_ID,
	YEAR_of_Fixture, 
	Division_Of_Fixture,
	 MONTH(Match_Date)
)B on a.League_ID = b.League_ID and A.Year_Of_Fixture = B.Year_Of_Fixture and A.MonthofFixture = B.MonthofFixture and A.Division_Of_Fixture = B.Division_Of_Fixture
-------------------------------------------------------------------------

join SSRS.League_Sites D on A.League_ID = D.ID_League

Order by League_ID


