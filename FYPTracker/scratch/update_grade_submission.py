import re

with open('Views/Supervisor/GradeSubmission.cshtml', 'r', encoding='utf-8') as f:
    c = f.read()

c = c.replace(
    '"Approved" or "AdminApproved" => "badge-light-success",',
    '"Approved" or "AdminApproved" or "SupervisorSigned" => "badge-light-success",'
)

c = c.replace(
    '"Rejected" or "PanelRevisionRequired" or "ChangeOfTopicRequired" => "badge-light-danger",',
    '"Rejected" or "PanelRevisionRequired" or "ChangeOfTopicRequired" or "ReturnedForEdits" => "badge-light-danger",'
)

c = c.replace(
    'bool isLocked = Model.Status == "Approved" || Model.Status == "Rejected";',
    'bool isLocked = Model.Status == "Approved" || Model.Status == "Rejected" || Model.Status == "SupervisorSigned" || Model.Status == "ReturnedForEdits";'
)

with open('Views/Supervisor/GradeSubmission.cshtml', 'w', encoding='utf-8') as f:
    f.write(c)
