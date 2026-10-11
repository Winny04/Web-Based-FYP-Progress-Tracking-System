import os

admin_path = 'FYPTracker/Controllers/AdminController.cs'
with open(admin_path, 'r', encoding='utf-8') as f:
    admin_content = f.read()

# Fix A5: AdminFailDefense clears DefenseDate, DefenseTime, DefenseVenue. Comment it out.
a5_search = '''                milestone.DefenseDate = null;
                milestone.DefenseTime = null;
                milestone.DefenseVenue = null;'''

a5_insert = '''                // BLOCKED: Do not clear DefenseDate, Time, Venue to preserve history.
                // milestone.DefenseDate = null;
                // milestone.DefenseTime = null;
                // milestone.DefenseVenue = null;'''

admin_content = admin_content.replace(a5_search, a5_insert)

# Fix A6: ManageDefenses uses DefenseDate != null && AdminApproved to classify as completed defense.
a6_search = '''                            ((m.Status == "AdminApproved" || m.Status == "ChangeOfTopicRequired") && m.DefenseDate != null))'''

a6_insert = '''                            ((m.Status == "AdminApproved" || m.Status == "ChangeOfTopicRequired") && m.DefenseDate != null && m.Remarks != null && m.Remarks.Contains("JSON: ")))'''

admin_content = admin_content.replace(a6_search, a6_insert)

with open(admin_path, 'w', encoding='utf-8') as f:
    f.write(admin_content)

print('Done fixing AdminFailDefense and ManageDefenses')
