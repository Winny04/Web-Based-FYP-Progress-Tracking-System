BEGIN TRANSACTION;

-- 1. Scenario 1: Project 13, Milestone 61 (Refer to Defense, awaiting scheduling)
DELETE FROM IAFDefenseHistory WHERE MilestoneId = 61;
DELETE FROM IAFDefenseBooking WHERE MilestoneId = 61;

UPDATE FYPMilestone
SET Status = 'DefenseRequired',
    DefenseDate = NULL,
    DefenseTime = NULL,
    DefenseVenue = NULL,
    Remarks = 'Form: Initial Project Allocation Form (IAF). Details: Initial Project Allocation Form (IAF). JSON: {"CurrentAttempt":1,"Attempts":[{"AttemptNumber":1,"FormData":{"title":"Design and Implementation of Synthetic FYP Artifact 11","scope":"Development","problem":"Automated tracking architecture requires validation under high load.","obj1":"1. To design tracking architecture.","obj2":"2. To implement tracking.","obj3":"3. To evaluate performance.","references":"","studentName":"Student Dummy 11","coventryId":"cov26010011","program":"Computer Science","agreedSupervisor":""},"SubmittedAt":"2026-09-15T09:00:00","SupervisorFeedback":"Proceed to panel review.","SupervisorSignature":"Ts. Catherine Wong","SupervisorReviewedAt":"2026-09-16T14:00:00","Status":"DefenseRequired","PanelComments":"Scope and methodology require oral defense verification."}]}'
WHERE Id = 61;

-- 2. Scenario 2: Project 14, Milestone 65 (Defense scheduled, awaiting outcome)
UPDATE IAFDefenseBooking
SET BookingStatus = 'Scheduled',
    AcademicOutcome = 'Pending',
    OutcomeComments = NULL,
    DefenseDate = '2026-10-22',
    StartTime = '10:00:00.0000000',
    EndTime = '10:30:00.0000000',
    DurationMinutes = 30,
    Venue = 'Room B-201',
    UpdatedAt = '2026-10-15 22:00:00',
    UpdatedByUserId = 3
WHERE Id = 3;

UPDATE IAFDefenseHistory
SET ActionType = 'InitialScheduled',
    BookingStatus = 'Scheduled',
    AcademicOutcome = 'Pending',
    OutcomeComments = NULL,
    DefenseDate = '2026-10-22',
    StartTime = '10:00:00.0000000',
    EndTime = '10:30:00.0000000',
    Venue = 'Room B-201',
    ReasonForChange = 'Initial defense session scheduled.',
    Timestamp = '2026-10-15 22:00:00',
    ActionByUserId = 3
WHERE Id = 4;

UPDATE FYPMilestone
SET Status = 'DefenseScheduled',
    DefenseDate = '2026-10-22',
    DefenseTime = '10:00 AM',
    DefenseVenue = 'Room B-201',
    Remarks = 'Form: Initial Project Allocation Form (IAF). Details: Initial Project Allocation Form (IAF). JSON: {"CurrentAttempt":1,"Attempts":[{"AttemptNumber":1,"FormData":{"title":"Design and Implementation of Synthetic FYP Artifact 12","scope":"Development","problem":"System scalability requires defense panel verification.","obj1":"1. To design framework.","obj2":"2. To implement framework.","obj3":"3. To test framework.","references":"","studentName":"Student Dummy 12","coventryId":"cov26010012","program":"Computer Science","agreedSupervisor":""},"SubmittedAt":"2026-09-15T09:00:00","SupervisorFeedback":"Proceed to panel review.","SupervisorSignature":"Ts. Catherine Wong","SupervisorReviewedAt":"2026-09-16T14:00:00","Status":"DefenseScheduled","PanelComments":"Defense session scheduled in Room B-201."}]}'
WHERE Id = 65;

-- 3. Scenario 3: Project 21, Milestone 93 (Defense completed with FAIL outcome)
UPDATE FYPMilestone
SET Status = 'ChangeOfTopicRequired',
    DefenseDate = '2026-09-30',
    DefenseTime = '10:00 AM',
    DefenseVenue = 'Meeting Room B-2',
    Remarks = 'Form: Initial Project Allocation Form (IAF). Details: Initial Project Allocation Form (IAF). JSON: {"CurrentAttempt":1,"Attempts":[{"AttemptNumber":1,"FormData":{"title":"Design and Implementation of Synthetic FYP Artifact 19","scope":"Development","problem":"Topic overlap with completed research.","obj1":"1. To design new model.","obj2":"2. To test model.","obj3":"3. To benchmark model.","references":"","studentName":"Student Dummy 19","coventryId":"cov26010019","program":"Computer Science","agreedSupervisor":""},"SubmittedAt":"2026-09-15T09:00:00","SupervisorFeedback":"Proceed to panel review.","SupervisorSignature":"Dr. Daniel Raj","SupervisorReviewedAt":"2026-09-16T14:00:00","Status":"ChangeOfTopicRequired","PanelComments":"Defense Failed. Scope insufficient. Please submit a new Initial Allocation Form with a change of topic."}]}'
WHERE Id = 93;

-- 4. Scenario 4: Project 15, Milestone 69 (Defense completed with PASS outcome)
UPDATE FYPMilestone
SET Status = 'AdminApproved',
    DefenseDate = '2026-09-30',
    DefenseTime = '10:00 AM',
    DefenseVenue = 'Meeting Room B-2',
    Remarks = 'Form: Initial Project Allocation Form (IAF). Details: Initial Project Allocation Form (IAF). JSON: {"CurrentAttempt":1,"Attempts":[{"AttemptNumber":1,"FormData":{"title":"Design and Implementation of Synthetic FYP Artifact 13","scope":"Development","problem":"Edge deployment benchmarking required.","obj1":"1. To design benchmark suite.","obj2":"2. To implement suite.","obj3":"3. To validate results.","references":"","studentName":"Student Dummy 13","coventryId":"cov26010013","program":"Computer Science","agreedSupervisor":""},"SubmittedAt":"2026-09-15T09:00:00","SupervisorFeedback":"Proceed to panel review.","SupervisorSignature":"Ts. Catherine Wong","SupervisorReviewedAt":"2026-09-16T14:00:00","Status":"AdminApproved","PanelComments":"Passed Defense Session. Title accepted."}]}'
WHERE Id = 69;

COMMIT TRANSACTION;
