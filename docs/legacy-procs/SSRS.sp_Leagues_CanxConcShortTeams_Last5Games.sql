USE [PlaycricketV2]
GO
/****** Object:  StoredProcedure [SSRS].[sp_Leagues_CanxConcShortTeams_Last5Games]    Script Date: 7/20/2026 11:36:25 AM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROC [SSRS].[sp_Leagues_CanxConcShortTeams_Last5Games] AS

Declare @StartDate date = (select DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE())-1, 0)) --First day of previous month
Declare @EndDate date = (select DATEADD(MONTH, DATEDIFF(MONTH, -1, GETDATE())-1, -1)) --Last day of previous month

IF OBJECT_ID('tempdb..#Data') is not null
BEGIN
	DROP TABLE #Data
END


Select *, ROW_NUMBER() OVER (PARTITION BY Home_team_ID order by match_date DESC)RN
into #Data
FROM 
		(
		Select 
			Match_ID, 
			League_ID,
			b.Name,
			match_Date,
			Home_Club_Id, 
			Home_Club_Name, 
			Home_Team_Id, 
			Home_Team_Name,
			Home_County_ID, 
			Home_County_Name,
			Cancelled, 
			Cancelled_Club, 
			Abandoned, 
			Abandoned_Club, 
			Conceded, 
			Conceded_Home, 
			Conceded_Away, 
			HomePlayersCount, 
			AwayPlayersCount, 
			ShortSidedFixtureRef, 
			ShortSidedTeamName
		From Fact.Fixture a
		Join SSRS.League_Sites  b on a.League_ID = b.ID_League
		WHERE match_Date between @StartDate and @EndDate
		and  (Home_Club_Id not in (9195,11477,8141) or Away_Club_id not in (9195,11477,8141))
		And Result is not null
		and League_ID > 0 
		UNION

		Select 
			Match_ID, 
			League_ID,
			b.Name,
			match_Date,
			Away_Club_Id, 
			Away_Club_Name, 
			Away_Team_Id, 
			Away_Team_Name,
			Away_County_ID, 
			Away_County_Name,
			Cancelled, 
			Cancelled_Club, 
			Abandoned, 
			Abandoned_Club, 
			Conceded, 
			Conceded_Home, 
			Conceded_Away, 
			HomePlayersCount, 
			AwayPlayersCount, 
			ShortSidedFixtureRef, 
			ShortSidedTeamName

		From Fact.Fixture a
		Join SSRS.League_Sites  b on a.League_ID = b.ID_League
		WHERE match_Date between @StartDate and @EndDate
		and  (Home_Club_Id not in (9195,11477,8141) or Away_Club_id not in (9195,11477,8141))
		And Result is not null
		and League_ID > 0 
)A
--------------------------------------------------------------------------------------------
--------------------------------------***BREAK***-------------------------------------------
--------------------------------***Canx & Abandoned***--------------------------------------
--------------------------------------------------------------------------------------------
Truncate Table SSRS.Leagues_CanxAbandonened_Last5Games;
Insert into SSRS.Leagues_CanxAbandonened_Last5Games

Select A.*, B.Abandoned
FROM 
(
Select 
	Home_Club_Name, 
	Home_Club_Id,
	Home_Team_Name, 
	Home_Team_Id, 
	Name, 
	League_ID,
	SUM(cancelled) Cancelled
FROM #Data
where Cancelled >= 1
and Home_Club_Name = Cancelled_Club
Group by 
	Home_Club_Name,
	Home_Club_Id, 
	Home_Team_Name, 
	Home_Team_Id, 
	Name,
	League_ID
)A
Left outer Join
(

Select 
	Home_Club_Name, 
	Home_Club_Id,
	Home_Team_Name, 
	Home_Team_Id, 
	Name, 
	League_ID,
	SUM(Abandoned) Abandoned
FROM #Data
where Abandoned >= 1
and Home_Club_Name = Abandoned_Club
Group by 
	Home_Club_Name, 
	Home_Club_Id,
	Home_Team_Name, 
	Home_Team_Id, 
	Name,
	League_ID
)B on a.Home_Team_Id = B.Home_Team_Id

Where Cancelled > 1
Order by A.Name

--------------------------------------------------------------------------------------------
--------------------------------------***BREAK***-------------------------------------------
--------------------------------***Conceded & Short***--------------------------------------
--------------------------------------------------------------------------------------------


Truncate Table SSRS.Leagues_ConcShortTeams_Last5Games;

Insert into SSRS.Leagues_ConcShortTeams_Last5Games


Select 
		isnull(A.Home_Club_Name, 	 B.Home_Club_Name) Home_Club_Name,
		isnull(A.Home_Club_Id, 	 B.Home_Club_Id) Home_Club_Id,
		isnull(A.Home_Team_Name, 	 B.Home_Team_Name) 	  Home_Team_Name, 
		isnull(A.Home_Team_Id, 	 B.Home_Team_Id) 		  Home_Team_Id, 	 
		isnull(A.Name, 	 B.Name) 	  Name,
		isnull(A.League_ID, 	 B.League_ID) 	  League_ID,
		isnull(A.Conceded,0) Conceded,
		isnull(B.ShortSided,0) ShortSided



FROM 
(
Select 
	Home_Club_Name, 
	Home_Club_Id,
	Home_Team_Name, 
	Home_Team_Id, 
	Name,
	League_ID,
	isnull(SUM(Conceded),0) Conceded
FROM #Data
where Conceded >= 1
and Home_Club_Name = ISNULL(Conceded_Home, Conceded_Away)

Group by 
	Home_Club_Name, 
	Home_Club_Id,
	Home_Team_Name, 
	Home_Team_Id, 
	Name,
	League_ID

)A
Full outer Join
(

Select 
	Home_Club_Name, 
	Home_Club_Id,
	Home_Team_Name, 
	Home_Team_Id, 
	Name,
	League_ID,
	isnull(Count(ShortSidedFixtureRef),0) ShortSided
FROM #Data
where ShortSidedFixtureRef in (1)
and Home_Club_Name = ShortSidedTeamName

Group by 
	Home_Club_Name, 
	Home_Club_Id,
	Home_Team_Name, 
	Home_Team_Id, 
	Name,
	League_ID
)B on a.Home_Team_Id = B.Home_Team_Id

Where isnull(Conceded,0)+isnull(ShortSided,0) > 1
Order by A.Name
-----

