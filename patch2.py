import os

admin_path = 'FYPTracker/Controllers/AdminController.cs'
with open(admin_path, 'r', encoding='utf-8') as f:
    admin_content = f.read()

import re

# Match AdminScheduleDefense
match = re.search(r'public async Task<IActionResult> AdminScheduleDefense\(int milestoneId, int examinerId, DateTime defenseDate, TimeSpan defenseTime, string venue\)\s*\{', admin_content)

if match:
    new_signature = '''public async Task<IActionResult> AdminScheduleDefense([FromServices] FYPTracker.Services.ILecturerAvailabilityService availabilityService, int milestoneId, int examinerId, DateTime defenseDate, TimeSpan defenseTime, string venue)
        {'''
    admin_content = admin_content[:match.start()] + new_signature + admin_content[match.end():]

# Now insert the A2 check right after checking supervisor independence:
search_str = '''if (milestone.Project != null && milestone.Project.SupervisorId == examiner.Id)
            {
                TempData["Error"] = "A supervisor cannot serve as the independent examiner for their own student's defense.";
                return RedirectToAction(nameof(SmartScheduleDefense), new { milestoneId });
            }'''

insert_str = search_str + '''

            // Missing POST Availability Revalidation (A2)
            var supervisorId = milestone.Project?.SupervisorId ?? 0;
            var lecturers = new List<int> { examinerId }; if (supervisorId > 0) lecturers.Add(supervisorId);
            var slots = await availabilityService.GetMutualAvailableSlotsAsync(lecturers, defenseDate, 45);
            var isValidSlot = slots.Any(s => s.StartTime <= defenseTime && s.EndTime >= defenseTime.Add(TimeSpan.FromMinutes(45)));
            
            if (!isValidSlot)
            {
                TempData["Error"] = "The selected time slot is no longer available.";
                return RedirectToAction(nameof(SmartScheduleDefense), new { milestoneId });
            }'''

admin_content = admin_content.replace(search_str, insert_str)

# Now move await _context.SaveChangesAsync() before emails.
# In the original, SaveChangesAsync is at the end:
#                 await _context.SaveChangesAsync();
#                 TempData["Success"] = "Successfully scheduled defense and sent invitations to the student, supervisor, and examiner.";

# And email sending is just above it. But I also need to save the notification before? No, EF saves everything.
# The user wants "Move Email Before SaveChangesAsync to After SaveChangesAsync" 
# Oh wait, the original code doesn't call SaveChangesAsync before emails. It just builds them. I need to insert SaveChangesAsync before the email block!

email_start = '''                var student = milestone.Project?.Student;
                var supervisor = milestone.Project?.Supervisor;'''

new_email_start = '''                await _context.SaveChangesAsync();
                
                var student = milestone.Project?.Student;
                var supervisor = milestone.Project?.Supervisor;'''

admin_content = admin_content.replace(email_start, new_email_start)

# Remove the final SaveChangesAsync since we moved it up. Or keep it? It's fine to keep it, but I can remove it.
final_save = '''                await _context.SaveChangesAsync();
                TempData["Success"] = "Successfully scheduled defense and sent invitations to the student, supervisor, and examiner.";'''

new_final_save = '''                TempData["Success"] = "Successfully scheduled defense and sent invitations to the student, supervisor, and examiner.";'''

# We must be careful to replace only the correct final save.
admin_content = admin_content.replace(final_save, new_final_save)

with open(admin_path, 'w', encoding='utf-8') as f:
    f.write(admin_content)

print('Done fixing AdminScheduleDefense')
