BEGIN TRANSACTION;

-- =========================================================================
-- 1. Student p26010033 (UserId: 51, ProjectId: 35, SupervisorId: 15 - Ms. Grace Teo)
-- Add 5 Stage 1 meetings (Weeks 6 to 10) with 5 signed logbooks
-- =========================================================================

-- Meeting W6: 2026-09-10
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    51, 15, 35, 1, 6, 1,
    '2026-09-10', 'Thursday', '10:00', 'Stage 1 Week 6 supervision: Methodology refinement and system flow diagram review', 'Approved', 'Lecturer Office / Online Teams',
    '2026-09-10 10:00:00.000', '2026-09-10 11:00:00.000', '2026-09-08 14:00:00.000', 0
);
DECLARE @b33_6 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b33_6,
    'Review of detailed system flowchart, entity relational modeling, and API contract specifications.',
    'Completed draft schema diagrams and updated Chapter 3 methodology write-up.',
    'Finalize architectural block diagrams and start core module interface stubs.',
    'Methodology is logically sound. Ensure data dictionary tables are thoroughly documented.|||SIG|||Ms. Grace Teo',
    '2026-09-10 11:30:00.000', 'Signed', '2026-09-10 10:00:00.000'
);

-- Meeting W7: 2026-09-17
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    51, 15, 35, 1, 7, 1,
    '2026-09-17', 'Thursday', '10:00', 'Stage 1 Week 7 supervision: Core architectural design and prototype scaffolding', 'Approved', 'Lecturer Office / Online Teams',
    '2026-09-17 10:00:00.000', '2026-09-17 11:00:00.000', '2026-09-15 14:00:00.000', 0
);
DECLARE @b33_7 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b33_7,
    'Discussion on preliminary technical proof-of-concept and third-party library integration.',
    'Scaffolded project baseline repository and implemented database connection pooling.',
    'Conduct benchmarking on response latency and document preliminary findings in DPP draft.',
    'Prototype structure adheres to modular software architecture guidelines. Well executed.|||SIG|||Ms. Grace Teo',
    '2026-09-17 11:30:00.000', 'Signed', '2026-09-17 10:00:00.000'
);

-- Meeting W8: 2026-09-24
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    51, 15, 35, 1, 8, 1,
    '2026-09-24', 'Thursday', '10:00', 'Stage 1 Week 8 supervision: Draft Project Proposal (DPP) preliminary review', 'Approved', 'Lecturer Office / Online Teams',
    '2026-09-24 10:00:00.000', '2026-09-24 11:00:00.000', '2026-09-22 14:00:00.000', 0
);
DECLARE @b33_8 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b33_8,
    'Detailed chapter-by-chapter walkthrough of Chapters 1 to 3 in Draft Project Proposal.',
    'Consolidated problem statement, research questions, and project objectives into unified draft.',
    'Format citations according to Harvard referencing standard and enhance literature matrix.',
    'Clear alignment between objectives and methodology. Tighten the scope definition in Chapter 1.|||SIG|||Ms. Grace Teo',
    '2026-09-24 11:30:00.000', 'Signed', '2026-09-24 10:00:00.000'
);

-- Meeting W9: 2026-10-01
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    51, 15, 35, 1, 9, 1,
    '2026-10-01', 'Thursday', '10:00', 'Stage 1 Week 9 supervision: Turnitin similarity report analysis and revision', 'Approved', 'Lecturer Office / Online Teams',
    '2026-10-01 10:00:00.000', '2026-10-01 11:00:00.000', '2026-09-29 14:00:00.000', 0
);
DECLARE @b33_9 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b33_9,
    'Evaluation of Turnitin similarity breakdown, paraphrasing verification, and academic integrity check.',
    'Submitted preliminary draft to Turnitin draft inbox with an 11% similarity index.',
    'Paraphrase literature background excerpts and assemble final DPP submission package.',
    'Similarity index is well below the 20% institutional threshold. No plagiarism concerns.|||SIG|||Ms. Grace Teo',
    '2026-10-01 11:30:00.000', 'Signed', '2026-10-01 10:00:00.000'
);

-- Meeting W10: 2026-10-08
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    51, 15, 35, 1, 10, 1,
    '2026-10-08', 'Thursday', '10:00', 'Stage 1 Week 10 supervision: Stage 1 final wrap-up and oral presentation readiness', 'Approved', 'Lecturer Office / Online Teams',
    '2026-10-08 10:00:00.000', '2026-10-08 11:00:00.000', '2026-10-06 14:00:00.000', 0
);
DECLARE @b33_10 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b33_10,
    'Final readiness check for Stage 1 DPP submission and transition into Stage 2 implementation.',
    'Submitted Stage 1 Final Submission (DPP) to the tracking portal.',
    'Prepare sprint backlog and development environment setup for Stage 2 sprint 1.',
    'Stage 1 requirements fully satisfied. Commended on meeting consistency and diligence.|||SIG|||Ms. Grace Teo',
    '2026-10-08 11:30:00.000', 'Signed', '2026-10-08 10:00:00.000'
);

-- Update existing Stage 1 bookings (87 to 91) with explicit WeekNumber 1 to 5
UPDATE MeetingBooking SET WeekNumber = 1 WHERE Id = 87;
UPDATE MeetingBooking SET WeekNumber = 2 WHERE Id = 88;
UPDATE MeetingBooking SET WeekNumber = 3 WHERE Id = 89;
UPDATE MeetingBooking SET WeekNumber = 4 WHERE Id = 90;
UPDATE MeetingBooking SET WeekNumber = 5 WHERE Id = 91;

-- Update existing Stage 2 bookings (92 to 96) with explicit WeekNumber 1 to 5
UPDATE MeetingBooking SET WeekNumber = 1 WHERE Id = 92;
UPDATE MeetingBooking SET WeekNumber = 2 WHERE Id = 93;
UPDATE MeetingBooking SET WeekNumber = 3 WHERE Id = 94;
UPDATE MeetingBooking SET WeekNumber = 4 WHERE Id = 95;
UPDATE MeetingBooking SET WeekNumber = 5 WHERE Id = 96;


-- =========================================================================
-- 2. Student p26010050 (UserId: 68, ProjectId: 52, SupervisorId: 18 - Dr. Jeremy Ko)
-- Add 5 Stage 1 meetings (Weeks 6 to 10) with 5 signed logbooks
-- Add 5 Stage 2 meetings (Weeks 6 to 10) with 5 signed logbooks
-- =========================================================================

-- --- Stage 1 Meetings (Weeks 6 to 10) ---

-- S50 Stage 1 W6: 2026-09-10
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    68, 18, 52, 1, 6, 1,
    '2026-09-10', 'Thursday', '10:00', 'Stage 1 Week 6 supervision: Detailed system architecture and database design verification', 'Approved', 'Supervisor Consultation Room C-12',
    '2026-09-10 10:00:00.000', '2026-09-10 11:00:00.000', '2026-09-08 14:00:00.000', 0
);
DECLARE @b50_s1_6 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b50_s1_6,
    'Evaluation of relational database schema normalisation, foreign key constraints, and indexing strategy.',
    'Refined database schema to 3NF and documented entity relationship definitions.',
    'Draft system sequence diagrams and data flow diagrams (Level 0 and 1).',
    'Database model is robust and accounts for relational integrity. Proceed to sequence diagrams.|||SIG|||Dr. Jeremy Ko',
    '2026-09-10 11:30:00.000', 'Signed', '2026-09-10 10:00:00.000'
);

-- S50 Stage 1 W7: 2026-09-17
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    68, 18, 52, 1, 7, 1,
    '2026-09-17', 'Thursday', '10:00', 'Stage 1 Week 7 supervision: High-level architectural validation and sequence diagram audit', 'Approved', 'Supervisor Consultation Room C-12',
    '2026-09-17 10:00:00.000', '2026-09-17 11:00:00.000', '2026-09-15 14:00:00.000', 0
);
DECLARE @b50_s1_7 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b50_s1_7,
    'Review of end-to-end sequence diagrams, transaction boundaries, and exception handling paths.',
    'Completed sequence diagrams and documented API endpoint contracts.',
    'Assemble complete Draft Project Proposal (DPP) draft covering Chapters 1 through 3.',
    'Thorough architectural planning demonstrated. Ensure edge cases in authentication are addressed.|||SIG|||Dr. Jeremy Ko',
    '2026-09-17 11:30:00.000', 'Signed', '2026-09-17 10:00:00.000'
);

-- S50 Stage 1 W8: 2026-09-24
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    68, 18, 52, 1, 8, 1,
    '2026-09-24', 'Thursday', '10:00', 'Stage 1 Week 8 supervision: Draft Project Proposal (DPP) comprehensive review', 'Approved', 'Supervisor Consultation Room C-12',
    '2026-09-24 10:00:00.000', '2026-09-24 11:00:00.000', '2026-09-22 14:00:00.000', 0
);
DECLARE @b50_s1_8 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b50_s1_8,
    'Comprehensive feedback on DPP report structure, literature critique depth, and research justification.',
    'Compiled full 40-page DPP draft including literature synthesis matrix.',
    'Implement supervisor recommendations on literature critique and run preliminary Turnitin scan.',
    'Excellent depth of academic critique in literature review. Ready for Turnitin verification.|||SIG|||Dr. Jeremy Ko',
    '2026-09-24 11:30:00.000', 'Signed', '2026-09-24 10:00:00.000'
);

-- S50 Stage 1 W9: 2026-10-01
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    68, 18, 52, 1, 9, 1,
    '2026-10-01', 'Thursday', '10:00', 'Stage 1 Week 9 supervision: Turnitin report verification and final DPP sign-off', 'Approved', 'Supervisor Consultation Room C-12',
    '2026-10-01 10:00:00.000', '2026-10-01 11:00:00.000', '2026-09-29 14:00:00.000', 0
);
DECLARE @b50_s1_9 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b50_s1_9,
    'Verification of Turnitin similarity score (8%), reference list audit, and proposal sign-off.',
    'Executed similarity check; all flagged matches verified as standard bibliographic references.',
    'Package final DPP submission and upload to FYPTracker portal.',
    'Clean originality profile with 8% similarity. Formally approved for final Stage 1 submission.|||SIG|||Dr. Jeremy Ko',
    '2026-10-01 11:30:00.000', 'Signed', '2026-10-01 10:00:00.000'
);

-- S50 Stage 1 W10: 2026-10-08
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    68, 18, 52, 1, 10, 1,
    '2026-10-08', 'Thursday', '10:00', 'Stage 1 Week 10 supervision: Stage 1 completion debrief and Stage 2 milestone planning', 'Approved', 'Supervisor Consultation Room C-12',
    '2026-10-08 10:00:00.000', '2026-10-08 11:00:00.000', '2026-10-06 14:00:00.000', 0
);
DECLARE @b50_s1_10 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b50_s1_10,
    'Debrief on Stage 1 assessment marks, examiner feedback highlights, and Stage 2 timeline roadmapping.',
    'Successfully submitted DPP and archived Stage 1 milestone artifacts.',
    'Set up CI/CD pipeline and initialize core implementation repository for Stage 2 development.',
    'Commendable performance throughout Stage 1. Established solid foundation for development.|||SIG|||Dr. Jeremy Ko',
    '2026-10-08 11:30:00.000', 'Signed', '2026-10-08 10:00:00.000'
);

-- Update existing S50 Stage 1 bookings (257 to 261) with explicit WeekNumber 1 to 5
UPDATE MeetingBooking SET WeekNumber = 1 WHERE Id = 257;
UPDATE MeetingBooking SET WeekNumber = 2 WHERE Id = 258;
UPDATE MeetingBooking SET WeekNumber = 3 WHERE Id = 259;
UPDATE MeetingBooking SET WeekNumber = 4 WHERE Id = 260;
UPDATE MeetingBooking SET WeekNumber = 5 WHERE Id = 261;


-- --- Stage 2 Meetings (Weeks 6 to 10) ---

-- S50 Stage 2 W6: 2026-10-15
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    68, 18, 52, 2, 6, 1,
    '2026-10-15', 'Thursday', '10:00', 'Stage 2 Week 6 supervision: Mid-development progress audit and automated testing review', 'Approved', 'Supervisor Consultation Room C-12',
    '2026-10-15 10:00:00.000', '2026-10-15 11:00:00.000', '2026-10-13 14:00:00.000', 0
);
DECLARE @b50_s2_6 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b50_s2_6,
    'Audit of completed user stories, code test coverage metrics (84%), and integration testing results.',
    'Implemented backend services and completed automated unit test suite with 84% statement coverage.',
    'Initiate user interface integration and conduct end-to-end integration test runs.',
    'Test suite coverage is commendable. Ensure edge case exception handling is verified.|||SIG|||Dr. Jeremy Ko',
    '2026-10-15 11:30:00.000', 'Signed', '2026-10-15 10:00:00.000'
);

-- S50 Stage 2 W7: 2026-10-22
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    68, 18, 52, 2, 7, 1,
    '2026-10-22', 'Thursday', '10:00', 'Stage 2 Week 7 supervision: System integration, performance benchmarking and load test analysis', 'Approved', 'Supervisor Consultation Room C-12',
    '2026-10-22 10:00:00.000', '2026-10-22 11:00:00.000', '2026-10-20 14:00:00.000', 0
);
DECLARE @b50_s2_7 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b50_s2_7,
    'Analysis of JMeter load testing data, database query profiling, and API response optimization.',
    'Resolved database query bottlenecks and benchmarked sub-200ms latency under 100 concurrent users.',
    'Begin drafting Chapter 4 (Implementation) and Chapter 5 (Testing & Evaluation) of Final Report.',
    'Performance benchmarks validate scalability claims. Document test setups with clear graphs.|||SIG|||Dr. Jeremy Ko',
    '2026-10-22 11:30:00.000', 'Signed', '2026-10-22 10:00:00.000'
);

-- S50 Stage 2 W8: 2026-10-29
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    68, 18, 52, 2, 8, 1,
    '2026-10-29', 'Thursday', '10:00', 'Stage 2 Week 8 supervision: Final thesis report draft comprehensive audit', 'Approved', 'Supervisor Consultation Room C-12',
    '2026-10-29 10:00:00.000', '2026-10-29 11:00:00.000', '2026-10-27 14:00:00.000', 0
);
DECLARE @b50_s2_8 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b50_s2_8,
    'Thorough review of complete Final Thesis Report draft (Chapters 1 to 6), figures, and appendices.',
    'Consolidated all thesis chapters into unified document and verified appendix test evidence.',
    'Refine conclusion and future work recommendations; conduct Turnitin originality scan.',
    'Thesis draft is comprehensive and scholarly. Ensure discussion ties back directly to objectives.|||SIG|||Dr. Jeremy Ko',
    '2026-10-29 11:30:00.000', 'Signed', '2026-10-29 10:00:00.000'
);

-- S50 Stage 2 W9: 2026-11-05
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    68, 18, 52, 2, 9, 1,
    '2026-11-05', 'Thursday', '10:00', 'Stage 2 Week 9 supervision: Final submission verification, Turnitin clearance, and logbook sign-off', 'Approved', 'Supervisor Consultation Room C-12',
    '2026-11-05 10:00:00.000', '2026-11-05 11:00:00.000', '2026-11-03 14:00:00.000', 0
);
DECLARE @b50_s2_9 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b50_s2_9,
    'Final Turnitin clearance check (7% similarity), digital logbook verification, and submission sign-off.',
    'Completed Final Report submission and compiled Record of Supervisor Meeting documentation.',
    'Prepare viva presentation slides and rehearse system demonstration walkthrough.',
    'Full submission package verified. Turnitin similarity is exemplary. Approved for viva defense.|||SIG|||Dr. Jeremy Ko',
    '2026-11-05 11:30:00.000', 'Signed', '2026-11-05 10:00:00.000'
);

-- S50 Stage 2 W10: 2026-11-12
INSERT INTO MeetingBooking (
    StudentId, SupervisorId, FYPProjectId, Stage, WeekNumber, AcademicSessionId,
    MeetingDate, MeetingDay, MeetingTime, Reason, Status, Location,
    StartDateTime, EndDateTime, CreatedDate, ReminderSent
) VALUES (
    68, 18, 52, 2, 10, 1,
    '2026-11-12', 'Thursday', '10:00', 'Stage 2 Week 10 supervision: Final Viva rehearsal and presentation demonstration review', 'Approved', 'Supervisor Consultation Room C-12',
    '2026-11-12 10:00:00.000', '2026-11-12 11:00:00.000', '2026-11-10 14:00:00.000', 0
);
DECLARE @b50_s2_10 INT = SCOPE_IDENTITY();

INSERT INTO MeetingLog (
    MeetingBookingId, KeyTopicsDiscussed, CompletedActionsAndNotes, ActionPointsForNextMeeting,
    SupervisorComments, SignatureDate, LogStatus, CreatedDate
) VALUES (
    @b50_s2_10,
    'Dry-run rehearsal of 15-minute viva presentation, live system demonstration, and Q&A handling.',
    'Completed presentation slides and finalized demonstration sandbox environment.',
    'Deliver final oral defense presentation at scheduled Final Viva session.',
    'Confident presentation and polished demonstration. Ready for final evaluation panel.|||SIG|||Dr. Jeremy Ko',
    '2026-11-12 11:30:00.000', 'Signed', '2026-11-12 10:00:00.000'
);

-- Update existing S50 Stage 2 bookings (262 to 266) with explicit WeekNumber 1 to 5
UPDATE MeetingBooking SET WeekNumber = 1 WHERE Id = 262;
UPDATE MeetingBooking SET WeekNumber = 2 WHERE Id = 263;
UPDATE MeetingBooking SET WeekNumber = 3 WHERE Id = 264;
UPDATE MeetingBooking SET WeekNumber = 4 WHERE Id = 265;
UPDATE MeetingBooking SET WeekNumber = 5 WHERE Id = 266;

COMMIT TRANSACTION;
