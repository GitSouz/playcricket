USE [PlaycricketV2]
GO
/****** Object:  StoredProcedure [SSRS].[sp_Leagues_Divisions_ThisMonth]    Script Date: 7/20/2026 11:38:04 AM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROC [SSRS].[sp_Leagues_Divisions_ThisMonth]
as

Declare @StartDate date = (select DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE())-1, 0)) --First day of previous month
Declare @EndDate date = (select DATEADD(MONTH, DATEDIFF(MONTH, -1, GETDATE())-1, -1)) --Last day of previous month

Truncate Table SSRS.Leagues_Divisions_ThisMonth;

Insert into SSRS.Leagues_Divisions_ThisMonth


SELECT  D.Name,  A.* , B.ShortSided


FROM 
(
		SELECT 
			League_ID,
			Division_Of_Fixture,
				case isnull(result, 'No Result')
		when 'Drawn' then 'Win'
		When 'Tie' then 'Win'
		Else isnull(result, 'No Result')
	End
Result,
			Count(*) ScheduledFixtures

		FROM fact.Fixture
		WHERE match_date between @StartDate and @EndDate
		and (Home_Club_Id <> 9195 or Away_Club_id <> 9195)
		And Division_Of_Fixture Not like '%Test%'
		--and League_Of_Fixture <> 'CUP FIXTURE'
				and Result is not null
				and League_ID > 0 
		GROUP BY 
			League_ID,
			Division_Of_Fixture,
			isnull(result, 'No Result')

)A
PIVOT
(
		SUM(ScheduledFixtures) For Result in (  
												[No Result],
												[Abandoned],
												[Cancelled],
												[Conceded],
												[InProgress],
												[Win]
											  )
)A


Left outer join 
-------------------------------------------------------------------------
(
SELECT
	League_ID, 
	Division_Of_Fixture, 
	Count(*) ShortSided
FROM fact.Fixture

WHERE
match_date between @StartDate and @EndDate
and ShortSidedFixtureRef in (1)--,2)
and (Home_Club_Id <> 9195 or Away_Club_id <> 9195)
And Division_Of_Fixture Not like '%Test%'
--and League_Of_Fixture <> 'CUP FIXTURE'
		and Result is not null
		and League_ID > 0 
GROUP BY 
		League_ID, 
	Division_Of_Fixture 
)B ON a.Division_Of_Fixture = b.Division_Of_Fixture AND A.League_ID = B.League_ID
-------------------------------------------------------------------------

join SSRS.League_Sites D on A.League_ID = D.ID_League


ORDER BY A.League_ID
