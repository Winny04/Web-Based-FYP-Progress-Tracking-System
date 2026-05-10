-- ============================================================
-- FYP Progress Tracking System - Database Setup Script
-- Run this in SSMS connected to Boon\SQLEXPRESS
-- ============================================================

-- Step 1: Create the new database
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'FYPTracker')
BEGIN
    CREATE DATABASE [FYPTracker];
    PRINT 'Database FYPTracker created.';
END
ELSE
    PRINT 'Database FYPTracker already exists. Skipping creation.';
GO

USE [FYPTracker];
GO

-- ============================================================
-- SECTION A: TABLES KEPT FROM THE OLD SYSTEM
-- ============================================================

-- Department / Faculty table
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Department')
BEGIN
    CREATE TABLE [dbo].[Department] (
        [Id]             INT            IDENTITY(1,1) PRIMARY KEY,
        [DepartmentName] NVARCHAR(100)  NULL,
        [DepartmentCode] NVARCHAR(20)   NULL
    );
    PRINT 'Table Department created.';
END
GO

-- Users: Students, Supervisors, Admins
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployeeInfo')
BEGIN
    CREATE TABLE [dbo].[EmployeeInfo] (
        [Id]                 INT            IDENTITY(1,1) PRIMARY KEY,
        [FullName]           NVARCHAR(255)  NULL,
        [UserName]           NVARCHAR(255)  NULL,
        [ShortName]          NVARCHAR(50)   NULL,
        [PassCode]           NVARCHAR(255)  NULL,
        [Email]              NVARCHAR(255)  NULL,
        [AddedBy]            NVARCHAR(50)   NULL,
        [DateCreated]        DATETIME       NULL,
        [DateJoin]           DATETIME       NULL,
        [Image]              NVARCHAR(500)  NULL,
        -- UserType: 'Student', 'Supervisor', 'Admin'
        [UserType]           NVARCHAR(50)   NULL,
        [Department]         NVARCHAR(100)  NULL,
        [EmployeeId]         NVARCHAR(50)   NULL,
        [DepartmentId]       INT            NULL,
        [IsNotificationOn]   BIT            NOT NULL DEFAULT 1,
        [IsResign]           BIT            NOT NULL DEFAULT 0,
        CONSTRAINT FK_Employee_Department FOREIGN KEY ([DepartmentId]) REFERENCES [Department]([Id])
    );
    PRINT 'Table EmployeeInfo created.';
END
GO

-- Audit Trail
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AuditTrail')
BEGIN
    CREATE TABLE [dbo].[AuditTrail] (
        [Id] INT IDENTITY(1,1) PRIMARY KEY,
        [ShortMessage] NVARCHAR(MAX) NULL,
        [IpAddress] NVARCHAR(MAX) NULL,
        [EmpId] INT NULL,
        [LogLevelId] INT NOT NULL,
        [FullMessage] NVARCHAR(MAX) NULL,
        [ReferrerUrl] NVARCHAR(MAX) NULL,
        [PageUrl] NVARCHAR(MAX) NULL,
        [TableChange] NVARCHAR(MAX) NULL,
        [DataBefore] NVARCHAR(MAX) NULL,
        [DataAfter] NVARCHAR(MAX) NULL,
        [CreatedOn] DATETIME NOT NULL
    );
    PRINT 'Table AuditTrail created.';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AuditTag')
BEGIN
    CREATE TABLE [dbo].[AuditTag] (
        [Id] UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        [AuditId] INT NOT NULL,
        [EmployeeId] INT NOT NULL,
        [CreateAt] DATETIME NOT NULL,
        [IsRead] BIT NOT NULL
    );
    PRINT 'Table AuditTag created.';
END
GO

-- Email Templates
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmailTemplateConfig')
BEGIN
    CREATE TABLE [dbo].[EmailTemplateConfig] (
        [Id]             INT            IDENTITY(1,1) PRIMARY KEY,
        [ConfigKey]      NVARCHAR(MAX)  NOT NULL,
        [ConfigValue]    NVARCHAR(MAX)  NULL,
        [RequireParams]  NVARCHAR(MAX)  NULL
    );
    PRINT 'Table EmailTemplateConfig created.';
END
GO

-- ============================================================
-- SECTION B: NEW FYP-SPECIFIC TABLES
-- ============================================================

-- FYP Projects (proposals submitted by students)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FYPProject')
BEGIN
    CREATE TABLE [dbo].[FYPProject] (
        [Id]           INT            IDENTITY(1,1) PRIMARY KEY,
        [Title]        NVARCHAR(255)  NOT NULL,
        [Description]  NVARCHAR(MAX)  NULL,
        [StudentId]    INT            NOT NULL,
        [SupervisorId] INT            NULL,
        -- Status: 'Proposed', 'Approved', 'InProgress', 'Completed', 'Rejected'
        [Status]       NVARCHAR(50)   NOT NULL DEFAULT 'Proposed',
        [StartDate]    DATE           NULL,
        [EndDate]      DATE           NULL,
        [CreatedDate]  DATETIME       NOT NULL DEFAULT GETDATE(),
        [UpdatedDate]  DATETIME       NULL,
        CONSTRAINT FK_FYPProject_Student    FOREIGN KEY ([StudentId])    REFERENCES [EmployeeInfo]([Id]),
        CONSTRAINT FK_FYPProject_Supervisor FOREIGN KEY ([SupervisorId]) REFERENCES [EmployeeInfo]([Id])
    );
    PRINT 'Table FYPProject created.';
END
GO

-- Milestones / Progress Checkpoints
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FYPMilestone')
BEGIN
    CREATE TABLE [dbo].[FYPMilestone] (
        [Id]             INT            IDENTITY(1,1) PRIMARY KEY,
        [FYPProjectId]   INT            NOT NULL,
        [Title]          NVARCHAR(255)  NOT NULL,
        [Description]    NVARCHAR(MAX)  NULL,
        [DueDate]        DATE           NULL,
        [SubmittedDate]  DATE           NULL,
        -- Status: 'Pending', 'Submitted', 'UnderReview', 'Approved', 'Rejected'
        [Status]         NVARCHAR(50)   NOT NULL DEFAULT 'Pending',
        [Remarks]        NVARCHAR(MAX)  NULL,
        [CreatedDate]    DATETIME       NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_Milestone_Project FOREIGN KEY ([FYPProjectId]) REFERENCES [FYPProject]([Id]) ON DELETE CASCADE
    );
    PRINT 'Table FYPMilestone created.';
END
GO

-- Supervisor Feedback on Milestones
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FYPFeedback')
BEGIN
    CREATE TABLE [dbo].[FYPFeedback] (
        [Id]           INT            IDENTITY(1,1) PRIMARY KEY,
        [MilestoneId]  INT            NOT NULL,
        [SupervisorId] INT            NOT NULL,
        [Comment]      NVARCHAR(MAX)  NULL,
        -- Grade: e.g. 'A', 'B+', 'Pass', 'Fail', or a numeric score
        [Grade]        NVARCHAR(10)   NULL,
        [CreatedDate]  DATETIME       NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_Feedback_Milestone   FOREIGN KEY ([MilestoneId])  REFERENCES [FYPMilestone]([Id]) ON DELETE CASCADE,
        CONSTRAINT FK_Feedback_Supervisor  FOREIGN KEY ([SupervisorId]) REFERENCES [EmployeeInfo]([Id])
    );
    PRINT 'Table FYPFeedback created.';
END
GO

-- File Uploads per Milestone (reports, slides, etc.)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FYPSubmissionFile')
BEGIN
    CREATE TABLE [dbo].[FYPSubmissionFile] (
        [Id]            INT            IDENTITY(1,1) PRIMARY KEY,
        [MilestoneId]   INT            NOT NULL,
        [FileName]      NVARCHAR(255)  NOT NULL,
        [FilePath]      NVARCHAR(500)  NOT NULL,
        [FileSize]      BIGINT         NULL,
        [UploadedBy]    INT            NULL,
        [UploadedDate]  DATETIME       NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_File_Milestone  FOREIGN KEY ([MilestoneId])  REFERENCES [FYPMilestone]([Id]) ON DELETE CASCADE,
        CONSTRAINT FK_File_Uploader   FOREIGN KEY ([UploadedBy])   REFERENCES [EmployeeInfo]([Id])
    );
    PRINT 'Table FYPSubmissionFile created.';
END
GO

-- Supervisor Selection Requests (student → supervisor, 1-week auto-reject)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SupervisorRequest')
BEGIN
    CREATE TABLE [dbo].[SupervisorRequest] (
        [Id]              INT            IDENTITY(1,1) PRIMARY KEY,
        [StudentId]       INT            NOT NULL,
        [SupervisorId]    INT            NOT NULL,
        -- Status: 'Pending', 'Approved', 'Rejected'
        [Status]          NVARCHAR(50)   NOT NULL DEFAULT 'Pending',
        [RequestMessage]  NVARCHAR(MAX)  NULL,
        [RequestedDate]   DATETIME       NOT NULL DEFAULT GETDATE(),
        [ExpiryDate]      DATETIME       NOT NULL,
        [RespondedDate]   DATETIME       NULL,
        [ResponseNote]    NVARCHAR(MAX)  NULL,
        CONSTRAINT FK_SupReq_Student    FOREIGN KEY ([StudentId])    REFERENCES [EmployeeInfo]([Id]),
        CONSTRAINT FK_SupReq_Supervisor FOREIGN KEY ([SupervisorId]) REFERENCES [EmployeeInfo]([Id])
    );
    PRINT 'Table SupervisorRequest created.';
END
GO

-- Meeting Bookings (student books meeting with supervisor)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MeetingBooking')
BEGIN
    CREATE TABLE [dbo].[MeetingBooking] (
        [Id]               INT            IDENTITY(1,1) PRIMARY KEY,
        [StudentId]        INT            NOT NULL,
        [SupervisorId]     INT            NOT NULL,
        [MeetingDate]      DATE           NOT NULL,
        [MeetingDay]       NVARCHAR(20)   NULL,
        [MeetingTime]      NVARCHAR(10)   NULL,
        [Reason]           NVARCHAR(MAX)  NULL,
        -- Status: 'Pending', 'Approved', 'Rejected', 'Postponed'
        [Status]           NVARCHAR(50)   NOT NULL DEFAULT 'Pending',
        [SupervisorNote]   NVARCHAR(MAX)  NULL,
        [ProposedNewDate]  DATE           NULL,
        [CreatedDate]      DATETIME       NOT NULL DEFAULT GETDATE(),
        [RespondedDate]    DATETIME       NULL,
        CONSTRAINT FK_Meeting_Student    FOREIGN KEY ([StudentId])    REFERENCES [EmployeeInfo]([Id]),
        CONSTRAINT FK_Meeting_Supervisor FOREIGN KEY ([SupervisorId]) REFERENCES [EmployeeInfo]([Id])
    );
    PRINT 'Table MeetingBooking created.';
END
GO


-- Student Notifications
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FYPNotification')
BEGIN
    CREATE TABLE [dbo].[FYPNotification] (
        [Id]          INT            IDENTITY(1,1) PRIMARY KEY,
        [RecipientId] INT            NOT NULL,
        -- Type: 'SupervisorApproval' | 'MeetingUpdate' | 'DeadlineReminder' | 'SubmissionFeedback' | 'RevisionRequest' | 'General'
        [Type]        NVARCHAR(80)   NOT NULL DEFAULT 'General',
        [Title]       NVARCHAR(255)  NOT NULL,
        [Message]     NVARCHAR(MAX)  NULL,
        [IsRead]      BIT            NOT NULL DEFAULT 0,
        [CreatedDate] DATETIME       NOT NULL DEFAULT GETDATE(),
        [ReadDate]    DATETIME       NULL,
        CONSTRAINT FK_Notif_Recipient FOREIGN KEY ([RecipientId]) REFERENCES [EmployeeInfo]([Id]) ON DELETE CASCADE
    );
    PRINT 'Table FYPNotification created.';
END
GO

-- Student Activity Feed
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FYPActivity')
BEGIN
    CREATE TABLE [dbo].[FYPActivity] (
        [Id]          INT            IDENTITY(1,1) PRIMARY KEY,
        [StudentId]   INT            NOT NULL,
        [Type]        NVARCHAR(80)   NOT NULL DEFAULT 'General',
        [Title]       NVARCHAR(255)  NOT NULL,
        [Description] NVARCHAR(MAX)  NULL,
        [CreatedDate] DATETIME       NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_Activity_Student FOREIGN KEY ([StudentId]) REFERENCES [EmployeeInfo]([Id]) ON DELETE CASCADE
    );
    PRINT 'Table FYPActivity created.';
END
GO

-- ============================================================
-- SECTION C: SEED DATA  (safe — skips if data already exists)
-- ============================================================

-- ── Departments ────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM [Department])
BEGIN
    INSERT INTO [Department] ([DepartmentName], [DepartmentCode]) VALUES
    ('Faculty of Computer Science & Information Technology', 'FCSIT'),
    ('Faculty of Engineering',                              'FE'),
    ('Faculty of Business & Accountancy',                   'FBA'),
    ('Faculty of Science',                                  'FS');
    PRINT 'Seeded Department.';
END
GO

-- ── Admin user ─────────────────────────────────────────────
-- Password: Admin@123  (MD5 hash stored in PassCode)
IF NOT EXISTS (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'admin')
BEGIN
    INSERT INTO [EmployeeInfo]
        ([FullName],[UserName],[ShortName],[PassCode],[Email],[UserType],[DepartmentId],[DateCreated],[IsNotificationOn],[IsResign])
    VALUES
        ('Administrator','admin','Admin',
         '0192023a7bbd73250516f069df18b500',   -- MD5 of "Admin@123"
         'admin@fyp.edu.my','Admin',1,GETDATE(),1,0);
    PRINT 'Seeded Admin user.';
END
GO

-- ── Supervisor users (each checked individually) ──────────
-- Password for all demo accounts: Student@123  (MD5 Custom: 2056540123147169491231074529168190193222241)
IF NOT EXISTS (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'dr.lim')
BEGIN
    INSERT INTO [EmployeeInfo]
        ([FullName],[UserName],[ShortName],[PassCode],[Email],[UserType],[Department],[DepartmentId],[EmployeeId],[DateCreated],[IsNotificationOn],[IsResign])
    VALUES ('Dr. Lim Wei Qiang','dr.lim','Dr. Lim',
            '2056540123147169491231074529168190193222241',
            'lim.weiqiang@fyp.edu.my','Supervisor','Faculty of Computer Science & IT',1,'SUP-001',GETDATE(),1,0);
    PRINT 'Seeded dr.lim.';
END
GO

IF NOT EXISTS (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'dr.siti')
BEGIN
    INSERT INTO [EmployeeInfo]
        ([FullName],[UserName],[ShortName],[PassCode],[Email],[UserType],[Department],[DepartmentId],[EmployeeId],[DateCreated],[IsNotificationOn],[IsResign])
    VALUES ('Dr. Siti Noor Fadzilah','dr.siti','Dr. Siti',
            '2056540123147169491231074529168190193222241',
            'siti.noor@fyp.edu.my','Supervisor','Faculty of Engineering',2,'SUP-002',GETDATE(),1,0);
    PRINT 'Seeded dr.siti.';
END
GO

-- ── Student users (each checked individually) ──────────────
IF NOT EXISTS (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'student')
BEGIN
    INSERT INTO [EmployeeInfo]
        ([FullName],[UserName],[ShortName],[PassCode],[Email],[UserType],[Department],[DepartmentId],[EmployeeId],[DateCreated],[IsNotificationOn],[IsResign])
    VALUES ('Demo Student','student','Winny',
            '2056540123147169491231074529168190193222241',
            'student@fyp.edu.my','Student','Faculty of Computer Science & IT',1,'STU-001',GETDATE(),1,0);
    PRINT 'Seeded student.';
END
GO

IF NOT EXISTS (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'ali.hassan')
BEGIN
    INSERT INTO [EmployeeInfo]
        ([FullName],[UserName],[ShortName],[PassCode],[Email],[UserType],[Department],[DepartmentId],[EmployeeId],[DateCreated],[IsNotificationOn],[IsResign])
    VALUES ('Ali Bin Hassan','ali.hassan','Ali',
            '2056540123147169491231074529168190193222241',
            'ali.hassan@fyp.edu.my','Student','Faculty of Computer Science & IT',1,'STU-002',GETDATE(),1,0);
    PRINT 'Seeded ali.hassan.';
END
GO

-- ── FYP Projects (one insert per student, individually guarded) ──
IF NOT EXISTS (SELECT 1 FROM [FYPProject] WHERE [StudentId] = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'student'))
   AND EXISTS  (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'student')
   AND EXISTS  (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'dr.lim')
BEGIN
    INSERT INTO [FYPProject]
        ([Title],[Description],[StudentId],[SupervisorId],[Status],[StartDate],[EndDate],[CreatedDate])
    VALUES (
        'Web-Based FYP Progress Tracking System',
        'A web application built with ASP.NET Core MVC and SQL Server to help students, supervisors, and administrators manage Final Year Project activities including progress tracking, supervisor selection, form submissions, and meeting scheduling.',
        (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'student'),
        (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'dr.lim'),
        'InProgress','2026-01-15','2026-12-31',GETDATE()
    );
    PRINT 'Seeded project for student.';
END
GO

IF NOT EXISTS (SELECT 1 FROM [FYPProject] WHERE [StudentId] = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'ali.hassan'))
   AND EXISTS  (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'ali.hassan')
   AND EXISTS  (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'dr.siti')
BEGIN
    INSERT INTO [FYPProject]
        ([Title],[Description],[StudentId],[SupervisorId],[Status],[StartDate],[EndDate],[CreatedDate])
    VALUES (
        'Smart Campus IoT Monitoring System',
        'An IoT-based system that monitors campus facilities in real time, providing energy usage analytics and automated alerts via a mobile-friendly web dashboard.',
        (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'ali.hassan'),
        (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'dr.siti'),
        'Proposed','2026-02-01','2026-12-31',GETDATE()
    );
    PRINT 'Seeded project for ali.hassan.';
END
GO

-- ── FYP Milestones for student's project ──────────────────
IF EXISTS (SELECT 1 FROM [FYPProject] WHERE [StudentId] = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'student'))
   AND NOT EXISTS (SELECT 1 FROM [FYPMilestone] WHERE [FYPProjectId] =
       (SELECT Id FROM [FYPProject] WHERE [StudentId] = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'student')))
BEGIN
    DECLARE @P1 INT = (SELECT Id FROM [FYPProject] WHERE [StudentId] = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'student'));

    INSERT INTO [FYPMilestone]
        ([FYPProjectId],[Title],[Description],[DueDate],[SubmittedDate],[Status],[Remarks],[CreatedDate])
    VALUES
        (@P1,'Chapter 1: Introduction',
         'Background, problem statement, objectives, and scope of the FYP.',
         '2026-02-20','2026-02-18','Approved',
         'Well written. Objectives are clear and achievable.',GETDATE()),

        (@P1,'Chapter 2: Literature Review',
         'Review of existing systems and related academic papers.',
         '2026-03-15','2026-03-14','Rejected',
         'Please add more recent references (2022-2026) and expand Section 2.3.',GETDATE()),

        (@P1,'Chapter 3: Methodology',
         'System design, architecture diagram, and development approach.',
         '2026-05-14',NULL,'Pending',NULL,GETDATE()),

        (@P1,'Chapter 4: Implementation',
         'Detailed implementation of all modules with screenshots.',
         '2026-07-10',NULL,'Pending',NULL,GETDATE()),

        (@P1,'Chapter 5: Testing and Evaluation',
         'Unit testing, UAT results, and system evaluation.',
         '2026-09-20',NULL,'Pending',NULL,GETDATE()),

        (@P1,'Final Submission',
         'Complete thesis document, presentation slides, and demo video.',
         '2026-11-30',NULL,'Pending',NULL,GETDATE());

    PRINT 'Seeded milestones for student.';
END
GO

-- ── FYP Milestones for ali.hassan's project ────────────────
IF EXISTS (SELECT 1 FROM [FYPProject] WHERE [StudentId] = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'ali.hassan'))
   AND NOT EXISTS (SELECT 1 FROM [FYPMilestone] WHERE [FYPProjectId] =
       (SELECT Id FROM [FYPProject] WHERE [StudentId] = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'ali.hassan')))
BEGIN
    DECLARE @P2 INT = (SELECT Id FROM [FYPProject] WHERE [StudentId] = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'ali.hassan'));

    INSERT INTO [FYPMilestone]
        ([FYPProjectId],[Title],[Description],[DueDate],[SubmittedDate],[Status],[Remarks],[CreatedDate])
    VALUES
        (@P2,'Chapter 1: Introduction',
         'Introduction and problem background for IoT campus monitoring.',
         '2026-03-10',NULL,'Pending',NULL,GETDATE()),

        (@P2,'Chapter 2: Literature Review',
         'Related IoT frameworks and smart campus implementations.',
         '2026-04-15',NULL,'Pending',NULL,GETDATE());

    PRINT 'Seeded milestones for ali.hassan.';
END
GO

-- ── Supervisor Requests (individually guarded) ────────────
IF NOT EXISTS (SELECT 1 FROM [SupervisorRequest]
               WHERE [StudentId]    = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'student')
                 AND [SupervisorId] = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'dr.lim'))
   AND EXISTS (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'student')
   AND EXISTS (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'dr.lim')
BEGIN
    INSERT INTO [SupervisorRequest]
        ([StudentId],[SupervisorId],[Status],[RequestMessage],[RequestedDate],[ExpiryDate],[RespondedDate],[ResponseNote])
    VALUES (
        (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'student'),
        (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'dr.lim'),
        'Approved',
        'Dear Dr. Lim, I would like to request your supervision for my FYP on a web-based tracking system.',
        '2026-01-10','2026-01-17','2026-01-12',
        'Happy to supervise. Looking forward to working with you.'
    );
    PRINT 'Seeded SupervisorRequest for student.';
END
GO

IF NOT EXISTS (SELECT 1 FROM [SupervisorRequest]
               WHERE [StudentId]    = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'ali.hassan')
                 AND [SupervisorId] = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'dr.siti'))
   AND EXISTS (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'ali.hassan')
   AND EXISTS (SELECT 1 FROM [EmployeeInfo] WHERE [UserName] = 'dr.siti')
BEGIN
    INSERT INTO [SupervisorRequest]
        ([StudentId],[SupervisorId],[Status],[RequestMessage],[RequestedDate],[ExpiryDate],[RespondedDate],[ResponseNote])
    VALUES (
        (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'ali.hassan'),
        (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'dr.siti'),
        'Pending',
        'Dear Dr. Siti, I am interested in developing an IoT smart campus monitoring system.',
        DATEADD(DAY,-2,GETDATE()),DATEADD(DAY,5,GETDATE()),NULL,NULL
    );
    PRINT 'Seeded SupervisorRequest for ali.hassan.';
END
GO

-- ── Meeting Bookings ───────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM [MeetingBooking])
BEGIN
    DECLARE @MS1  INT = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'student');
    DECLARE @MSup INT = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'dr.lim');

    INSERT INTO [MeetingBooking]
        ([StudentId],[SupervisorId],[MeetingDate],[MeetingDay],[MeetingTime],[Reason],[Status],[SupervisorNote],[ProposedNewDate],[CreatedDate],[RespondedDate])
    VALUES
        -- Past approved meeting
        (@MS1,@MSup,'2026-04-18','Friday','10:00',
         'Chapter 1 review and feedback discussion.',
         'Approved','See you at my office, Room B204.',NULL,
         '2026-04-10','2026-04-11'),

        -- Meeting that was postponed
        (@MS1,@MSup,'2026-05-02','Saturday','14:00',
         'Chapter 2 revision walkthrough.',
         'Postponed','I have a conference that day. Can we reschedule?','2026-05-09',
         '2026-04-28','2026-04-29'),

        -- Pending upcoming meeting
        (@MS1,@MSup,DATEADD(DAY,5,GETDATE()),DATENAME(WEEKDAY,DATEADD(DAY,5,GETDATE())),'11:00',
         'Discuss Chapter 3 methodology and get feedback before submission.',
         'Pending',NULL,NULL,GETDATE(),NULL);

    PRINT 'Seeded MeetingBooking.';
END
GO

-- ── FYP Notifications ──────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM [FYPNotification])
BEGIN
    DECLARE @NR INT = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'student');

    INSERT INTO [FYPNotification]
        ([RecipientId],[Type],[Title],[Message],[IsRead],[CreatedDate],[ReadDate])
    VALUES
        (@NR,'SupervisorApproval','Supervisor Request Approved!',
         'Dr. Lim Wei Qiang has accepted your supervision request. You can now book meetings and submit your project documents.',
         1,DATEADD(DAY,-18,GETDATE()),DATEADD(DAY,-17,GETDATE())),

        (@NR,'SubmissionFeedback','Chapter 1 Approved',
         'Your Chapter 1: Introduction has been reviewed and approved by Dr. Lim. Great work! Proceed to Chapter 2.',
         1,DATEADD(DAY,-14,GETDATE()),DATEADD(DAY,-13,GETDATE())),

        (@NR,'RevisionRequest','Chapter 2 Needs Revision',
         'Dr. Lim has requested revisions on your Chapter 2: Literature Review. Please add more recent references (2022–2026) and expand Section 2.3.',
         1,DATEADD(DAY,-7,GETDATE()),DATEADD(DAY,-6,GETDATE())),

        (@NR,'MeetingUpdate','Meeting Postponed',
         'Your meeting on 02 May 2026 has been postponed by Dr. Lim. A new date of 09 May 2026 has been proposed.',
         1,DATEADD(DAY,-4,GETDATE()),DATEADD(DAY,-3,GETDATE())),

        (@NR,'DeadlineReminder','Chapter 3 Deadline Approaching',
         'Your Chapter 3: Methodology is due on 14 May 2026 — only a few days away. Ensure you submit before the deadline.',
         0,DATEADD(DAY,-1,GETDATE()),NULL),

        (@NR,'MeetingUpdate','Meeting Request Submitted',
         'Your meeting request has been sent to Dr. Lim. You will be notified once the supervisor responds.',
         0,GETDATE(),NULL);

    PRINT 'Seeded FYPNotification.';
END
GO

-- ── FYP Activities (Recent Activity Feed) ──────────────────
IF NOT EXISTS (SELECT 1 FROM [FYPActivity])
BEGIN
    DECLARE @AR INT = (SELECT Id FROM [EmployeeInfo] WHERE [UserName] = 'student');

    INSERT INTO [FYPActivity]
        ([StudentId],[Type],[Title],[Description],[CreatedDate])
    VALUES
        (@AR,'SupervisorAssigned','Supervisor Assigned',
         'Dr. Lim Wei Qiang accepted your supervision request.',
         DATEADD(DAY,-18,GETDATE())),

        (@AR,'MilestoneApproved','Chapter 1 Approved',
         'Chapter 1: Introduction was approved by Dr. Lim.',
         DATEADD(DAY,-14,GETDATE())),

        (@AR,'FeedbackReceived','Supervisor Feedback on Chapter 2',
         'Dr. Lim added revision comments to Chapter 2: Literature Review.',
         DATEADD(DAY,-7,GETDATE())),

        (@AR,'MeetingBooked','Meeting Booked',
         'Meeting on 02 May 2026 at 14:00 was booked.',
         DATEADD(DAY,-5,GETDATE())),

        (@AR,'MeetingBooked','Meeting Postponed',
         'Meeting on 02 May was postponed. New date suggested: 09 May 2026.',
         DATEADD(DAY,-4,GETDATE())),

        (@AR,'MeetingBooked','New Meeting Requested',
         CONCAT('Meeting on ',FORMAT(DATEADD(DAY,5,GETDATE()),'dd MMM yyyy'),' at 11:00 is pending supervisor approval.'),
         GETDATE());

    PRINT 'Seeded FYPActivity.';
END
GO

PRINT '============================================';
PRINT 'FYP-Tracker database setup complete!';
PRINT '============================================';
