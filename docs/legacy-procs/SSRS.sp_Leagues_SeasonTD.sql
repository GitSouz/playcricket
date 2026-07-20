USE [PlaycricketV2]
GO
/****** Object:  StoredProcedure [SSRS].[sp_Leagues_SeasonTD]    Script Date: 7/20/2026 11:38:15 AM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
ALTER Proc [SSRS].[sp_Leagues_SeasonTD]
as


Declare @EndDate date = (SELECT EndDate
		   FROM lkp.Season
		   WHERE CurrLast = 'C'
		  )

Declare @StartDate date =  (SELECT StartDate
			 FROM lkp.Season
			 WHERE CurrLast = 'C'
			 )

IF OBJECT_ID('SSRS.Leagues_SeasonTD_2025') Is not NULL
Begin
	Drop Table [SSRS].[Leagues_SeasonTD_2026]
END


SELECT   a.League_ID,  D.[name], [No Result],a.Year_Of_Fixture, a.MonthofFixture, Abandoned, Cancelled,Conceded, InProgress, Win ,b.ShortSided
into [SSRS].[Leagues_SeasonTD_2026]
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
	 YEAR_of_Fixture, 
	 MONTH(Match_Date) As MonthofFixture,

	Count(*) ScheduledFixtures
FROM fact.Fixture
WHERE match_date between @StartDate and @EndDate
and (Home_Club_Id not in (9195,11477,8141,12251) or Away_Club_id not in (9195,11477,8141,12251) )
--and League_Of_Fixture <> 'CUP FIXTURE'
		and Result is not null
		and League_ID > 0 
		and league_ID <> 9039
GROUP BY League_ID,  result,
YEAR_of_Fixture, 
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
	 MONTH(Match_Date) as MonthofFixture,
	Count(*) ShortSided
FROM 
	fact.Fixture

WHERE 
	match_date between @StartDate and @EndDate
	and ShortSidedFixtureRef in (1)--,2)
	and (Home_Club_Id not in (9195,11477,8141,12251) or Away_Club_id not in (9195,11477,8141,12251))
	--and League_Of_Fixture <> 'CUP FIXTURE'
			and Result is not null
			and League_ID > 0 
GROUP BY  
	League_ID,
	YEAR_of_Fixture, 
	 MONTH(Match_Date)
)B on a.League_ID = b.League_ID and A.Year_Of_Fixture = B.Year_Of_Fixture and A.MonthofFixture = B.MonthofFixture
-------------------------------------------------------------------------

join SSRS.League_Sites D on A.League_ID = D.ID_League

Order by League_ID
