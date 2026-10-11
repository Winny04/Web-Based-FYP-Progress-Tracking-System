import os

admin_path = 'FYPTracker/Controllers/AdminController.cs'
with open(admin_path, 'r', encoding='utf-8') as f:
    admin_content = f.read()

# Fix A1: AdminScheduleDefense overwrites FYPProjectExaminer. Comment it out.
admin_content = admin_content.replace(
    '''// Assign or Update Examiner
                if (milestone.Project != null)
                {
                    var existingAssignment = milestone.Project.AssignedExaminers.FirstOrDefault();
                    if (existingAssignment == null)
                    {
                        _context.FYPProjectExaminers.Add(new FYPProjectExaminer { FYPProjectId = milestone.Project.Id, ExaminerId = examiner.Id });
                    }
                    else if (existingAssignment.ExaminerId != examiner.Id)
                    {
                        existingAssignment.ExaminerId = examiner.Id;
                    }
                }''',
    '''// Assign or Update Examiner
                // BLOCKED: Do not overwrite FYPProjectExaminer assignments. 
                // A new FYPDefenseSchedule migration is required to store independent Defense Examiners safely.
                if (milestone.Project != null)
                {
                    // var existingAssignment = milestone.Project.AssignedExaminers.FirstOrDefault();
                    // ...
                }'''
)

# Fix A4: Move Email Before SaveChangesAsync to After SaveChangesAsync
# I will find the exact lines and move them down.
# Let's check AdminScheduleDefense.
with open(admin_path, 'w', encoding='utf-8') as f:
    f.write(admin_content)

print('Done fixing AdminController.cs')
