INSERT INTO FYPMilestone (FYPProjectId, Title, Description, Status, DueDate, CreatedDate)
SELECT Id, 'FYP Proposal', 'Detailed project proposal covering Problem Statement, Objectives, and Scope.', 'Pending', DATEADD(day, 14, GETDATE()), GETDATE()
FROM FYPProject
WHERE Id NOT IN (
    SELECT FYPProjectId FROM FYPMilestone WHERE Title LIKE '%Proposal%'
)
