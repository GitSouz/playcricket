USE [PlaycricketV2]
GO
/****** Object:  StoredProcedure [SSRS].[sp_Leagues_ThisMonth]    Script Date: 7/20/2026 11:38:26 AM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER Proc [SSRS].[sp_Leagues_ThisMonth]

as

--Declare @StartDate date = '2023-04-01'--DATEADD(month, DATEDIFF(month, 0, Getdate()), 0)
--Declare @EndDate date =  '2023-04-30'--Getdate()

Declare @StartDate date = (select DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE())-1, 0)) --First day of previous month
Declare @EndDate date = (select DATEADD(MONTH, DATEDIFF(MONTH, -1, GETDATE())-1, -1)) --Last day of previous month

Truncate Table SSRS.Leagues_ThisMonth;

Insert into SSRS.Leagues_ThisMonth
Select D.Name, a.* ,b.ShortSided 
FROM 
(
Select 
	League_ID, 
	
		case isnull(result, 'No Result')
		when 'Drawn' then 'Win'
		When 'Tie' then 'Win'
		Else isnull(result, 'No Result')
	End
Result,
	Count(*) ScheduledFixtures
FROM fact.Fixture
where match_date between @StartDate and @EndDate
and (Home_Club_Id <> 9195 or Away_Club_id <> 9195)
--and League_Of_Fixture <> 'CUP FIXTURE'
		and Result is not null
		and League_ID > 0 
Group by League_ID, 
	Result
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
Select League_ID, Count(*) ShortSided
FROM fact.Fixture
where match_date between @StartDate and @EndDate
and ShortSidedFixtureRef in (1)
and (Home_Club_Id <> 9195 or Away_Club_id <> 9195)
--and League_Of_Fixture <> 'CUP FIXTURE'
		and Result is not null
		and League_ID > 0 
Group by League_ID
)B on a.League_ID = b.League_ID
-------------------------------------------------------------------------

join SSRS.League_Sites D on A.League_ID = D.ID_League

Order by League_ID