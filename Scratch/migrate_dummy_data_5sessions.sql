-- ==============================================================================
-- Migration Script: Organize Dummy Data into 5 Academic Sessions (Intake Scheme)
-- Target Database: FYPTracker_Development ONLY
-- Verified Backup: FYPTracker_Development_Pre5Sessions_20261016_000140.bak
-- ==============================================================================

USE FYPTracker_Development;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

BEGIN TRY
    PRINT 'Starting Transactional Academic Session Migration...';

    -- 1. Ensure 4 New Academic Sessions Exist with exact date ranges matching Session 1
    DECLARE @StartDate DATETIME2 = '2026-03-01 00:00:00.0000000';
    DECLARE @EndDate DATETIME2 = '2027-02-28 00:00:00.0000000';

    IF NOT EXISTS (SELECT 1 FROM AcademicSession WHERE SessionName = 'Session 2026/2027 - Intake A')
    BEGIN
        INSERT INTO AcademicSession (SessionName, IsActive, StartDate, EndDate, IsCurrent, RequiredMeetingsStage1, RequiredMeetingsStage2)
        VALUES ('Session 2026/2027 - Intake A', 1, @StartDate, @EndDate, 0, 10, 10);
    END

    IF NOT EXISTS (SELECT 1 FROM AcademicSession WHERE SessionName = 'Session 2026/2027 - Intake B')
    BEGIN
        INSERT INTO AcademicSession (SessionName, IsActive, StartDate, EndDate, IsCurrent, RequiredMeetingsStage1, RequiredMeetingsStage2)
        VALUES ('Session 2026/2027 - Intake B', 1, @StartDate, @EndDate, 0, 10, 10);
    END

    IF NOT EXISTS (SELECT 1 FROM AcademicSession WHERE SessionName = 'Session 2026/2027 - Intake C')
    BEGIN
        INSERT INTO AcademicSession (SessionName, IsActive, StartDate, EndDate, IsCurrent, RequiredMeetingsStage1, RequiredMeetingsStage2)
        VALUES ('Session 2026/2027 - Intake C', 1, @StartDate, @EndDate, 0, 10, 10);
    END

    IF NOT EXISTS (SELECT 1 FROM AcademicSession WHERE SessionName = 'Session 2026/2027 - Intake D')
    BEGIN
        INSERT INTO AcademicSession (SessionName, IsActive, StartDate, EndDate, IsCurrent, RequiredMeetingsStage1, RequiredMeetingsStage2)
        VALUES ('Session 2026/2027 - Intake D', 1, @StartDate, @EndDate, 0, 10, 10);
    END

    -- Retrieve generated IDs
    DECLARE @IdSession1 INT, @IdSession2 INT, @IdSession3 INT, @IdSession4 INT, @IdSession5 INT;
    SELECT @IdSession1 = Id FROM AcademicSession WHERE SessionName = 'Session 2026/2027 - Intake A';
    SELECT @IdSession2 = Id FROM AcademicSession WHERE SessionName = 'Session 2026/2027 - Intake B';
    SELECT @IdSession3 = Id FROM AcademicSession WHERE SessionName = 'Session 2026/2027 - Intake C';
    SELECT @IdSession4 = Id FROM AcademicSession WHERE SessionName = 'Session 2026/2027 - Intake D';
    SELECT @IdSession5 = 1; -- Preserved Session 2026/2027

    PRINT 'Session IDs: Session 1=' + CAST(@IdSession1 AS VARCHAR(10)) + 
          ', Session 2=' + CAST(@IdSession2 AS VARCHAR(10)) + 
          ', Session 3=' + CAST(@IdSession3 AS VARCHAR(10)) + 
          ', Session 4=' + CAST(@IdSession4 AS VARCHAR(10)) + 
          ', Session 5=' + CAST(@IdSession5 AS VARCHAR(10));

    -- 2. Update FYPProject AcademicSession column
    PRINT 'Updating FYPProject AcademicSession...';
    UPDATE FYPProject SET AcademicSession = 'Session 2026/2027 - Intake A' WHERE Id BETWEEN 3 AND 12;
    UPDATE FYPProject SET AcademicSession = 'Session 2026/2027 - Intake B' WHERE Id BETWEEN 13 AND 22;
    UPDATE FYPProject SET AcademicSession = 'Session 2026/2027 - Intake C' WHERE Id BETWEEN 23 AND 32;
    UPDATE FYPProject SET AcademicSession = 'Session 2026/2027 - Intake D' WHERE Id BETWEEN 33 AND 42;
    UPDATE FYPProject SET AcademicSession = 'Session 2026/2027' WHERE Id BETWEEN 43 AND 52;

    -- 3. Update ApplicationUser AcademicSession column for Students
    PRINT 'Updating ApplicationUser AcademicSession...';
    UPDATE ApplicationUser SET AcademicSession = 'Session 2026/2027 - Intake A' WHERE Id BETWEEN 19 AND 28;
    UPDATE ApplicationUser SET AcademicSession = 'Session 2026/2027 - Intake B' WHERE Id BETWEEN 29 AND 38;
    UPDATE ApplicationUser SET AcademicSession = 'Session 2026/2027 - Intake C' WHERE Id BETWEEN 39 AND 48;
    UPDATE ApplicationUser SET AcademicSession = 'Session 2026/2027 - Intake D' WHERE Id BETWEEN 49 AND 58;
    UPDATE ApplicationUser SET AcademicSession = 'Session 2026/2027' WHERE Id BETWEEN 59 AND 68;

    -- 4. Update MeetingBooking AcademicSessionId
    PRINT 'Updating MeetingBooking AcademicSessionId...';
    UPDATE MeetingBooking SET AcademicSessionId = @IdSession1 WHERE FYPProjectId BETWEEN 3 AND 12;
    UPDATE MeetingBooking SET AcademicSessionId = @IdSession2 WHERE FYPProjectId BETWEEN 13 AND 22;
    UPDATE MeetingBooking SET AcademicSessionId = @IdSession3 WHERE FYPProjectId BETWEEN 23 AND 32;
    UPDATE MeetingBooking SET AcademicSessionId = @IdSession4 WHERE FYPProjectId BETWEEN 33 AND 42;
    UPDATE MeetingBooking SET AcademicSessionId = @IdSession5 WHERE FYPProjectId BETWEEN 43 AND 52;

    -- 5. Update SupervisorTimetableEntry AcademicSessionId
    PRINT 'Updating SupervisorTimetableEntry AcademicSessionId...';
    UPDATE SupervisorTimetableEntry SET AcademicSessionId = @IdSession1 WHERE SupervisorId IN (9, 10);
    UPDATE SupervisorTimetableEntry SET AcademicSessionId = @IdSession2 WHERE SupervisorId IN (11, 12);
    UPDATE SupervisorTimetableEntry SET AcademicSessionId = @IdSession3 WHERE SupervisorId IN (13, 14);
    UPDATE SupervisorTimetableEntry SET AcademicSessionId = @IdSession4 WHERE SupervisorId IN (15, 16);
    UPDATE SupervisorTimetableEntry SET AcademicSessionId = @IdSession5 WHERE SupervisorId IN (17, 18);

    -- 6. Update IAFDefenseBooking & IAFDefenseHistory AcademicSessionId
    -- All 9 IAF defense bookings belong to Projects 14-22 (Session 2 / Intake B).
    PRINT 'Updating IAFDefenseBooking & IAFDefenseHistory AcademicSessionId to Session 2...';
    UPDATE b
    SET b.AcademicSessionId = @IdSession2
    FROM IAFDefenseBooking b
    JOIN FYPMilestone m ON m.Id = b.MilestoneId
    WHERE m.FYPProjectId BETWEEN 13 AND 22;

    UPDATE h
    SET h.AcademicSessionId = @IdSession2
    FROM IAFDefenseHistory h
    JOIN FYPMilestone m ON m.Id = h.MilestoneId
    WHERE m.FYPProjectId BETWEEN 13 AND 22;

    COMMIT TRANSACTION;
    PRINT 'Academic Session Migration Completed Successfully!';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    
    DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
    DECLARE @ErrorState INT = ERROR_STATE();
    PRINT 'MIGRATION FAILED: ' + @ErrorMessage;
    RAISERROR (@ErrorMessage, @ErrorSeverity, @ErrorState);
END CATCH
GO
