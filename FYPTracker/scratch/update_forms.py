import re

with open('Views/Student/Forms.cshtml', 'r', encoding='utf-8') as f:
    c = f.read()

old_buttons = """                                                <div class="d-flex gap-2">
                                                    <button type="submit" name="isDraft" value="true" class="btn btn-light fw-bold px-4 border shadow-sm" formnovalidate><i class="fas fa-save me-2 text-muted"></i>Save Draft</button>
                                                    <button type="submit" name="isDraft" value="false" class="btn btn-primary btn-submit px-4 shadow-sm" onclick="return confirm('Are you sure you want to submit your IAF? You cannot edit this once submitted.')"><i class="fas fa-paper-plane me-2"></i>Submit IAF Form</button>
                                                </div>"""

new_buttons = """                                                <div class="d-flex gap-2">
                                                    @if (iafMilestone?.Status == "Draft" || iafMilestone?.Status == "ReturnedForEdits" || string.IsNullOrEmpty(iafMilestone?.Status))
                                                    {
                                                        <button type="submit" name="iafAction" value="draft" class="btn btn-light fw-bold px-4 border shadow-sm" formnovalidate><i class="fas fa-save me-2 text-muted"></i>Save Draft</button>
                                                        <button type="submit" name="iafAction" value="supervisor_review" class="btn btn-info px-4 shadow-sm text-white fw-bold"><i class="fas fa-user-tie me-2"></i>Send for Supervisor Review</button>
                                                    }
                                                    else if (iafMilestone?.Status == "SupervisorSigned")
                                                    {
                                                        bool isChangeOfTopic = false;
                                                        if (!string.IsNullOrEmpty(iafMilestone.Remarks) && iafMilestone.Remarks.Contains("JSON: ")) {
                                                            var parts = iafMilestone.Remarks.Split(new[] { "JSON: " }, StringSplitOptions.None);
                                                            var history = FYPTracker.Models.IafAttemptHistory.ParseOrMigrate(parts[1]);
                                                            if (history.Attempts.Any(a => a.Status == "ChangeOfTopicRequired" || (a.PanelComments != null && a.PanelComments.Contains("Change of Topic"))) || history.CurrentAttempt > 1) {
                                                                isChangeOfTopic = true;
                                                            }
                                                        }
                                                        
                                                        if (isChangeOfTopic) {
                                                            <button type="submit" name="iafAction" value="submit_new" class="btn btn-success btn-submit px-4 shadow-sm" onclick="return confirm('Are you sure you want to officially submit your NEW IAF? It will be sent directly for final Admin Approval.')"><i class="fas fa-paper-plane me-2"></i>Submit New IAF</button>
                                                        } else {
                                                            <button type="submit" name="iafAction" value="submit" class="btn btn-primary btn-submit px-4 shadow-sm" onclick="return confirm('Are you sure you want to officially submit your IAF? It will be sent to the Panel.')"><i class="fas fa-paper-plane me-2"></i>Submit IAF</button>
                                                        }
                                                    }
                                                    else if (iafMilestone?.Status == "PanelRevisionRequired")
                                                    {
                                                        <button type="submit" name="iafAction" value="draft" class="btn btn-light fw-bold px-4 border shadow-sm" formnovalidate><i class="fas fa-save me-2 text-muted"></i>Save Draft</button>
                                                        <button type="submit" name="iafAction" value="ready_defense" class="btn btn-warning text-dark px-4 shadow-sm fw-bold"><i class="fas fa-calendar-check me-2"></i>Ready for Defence</button>
                                                    }
                                                    else if (iafMilestone?.Status == "ChangeOfTopicRequired")
                                                    {
                                                        <button type="submit" name="iafAction" value="draft" class="btn btn-light fw-bold px-4 border shadow-sm" formnovalidate><i class="fas fa-save me-2 text-muted"></i>Save Draft</button>
                                                        <button type="submit" name="iafAction" value="supervisor_review" class="btn btn-info px-4 shadow-sm text-white fw-bold"><i class="fas fa-user-tie me-2"></i>Send for Supervisor Review</button>
                                                    }
                                                </div>"""

old_lock = 'var isIafLocked = (iafMilestone?.Status == "Submitted" || iafMilestone?.Status == "PendingPanelReview" || iafMilestone?.Status == "AdminApproved" || iafMilestone?.Status == "Approved" || iafMilestone?.Status == "DefenseScheduled");'
new_lock = 'var isIafLocked = (iafMilestone?.Status == "Submitted" || iafMilestone?.Status == "PendingPanelReview" || iafMilestone?.Status == "AdminApproved" || iafMilestone?.Status == "Approved" || iafMilestone?.Status == "DefenseScheduled" || iafMilestone?.Status == "PendingSupervisorReview" || iafMilestone?.Status == "SupervisorSigned" || iafMilestone?.Status == "DefenseRequired");'

c = c.replace(old_buttons, new_buttons)
c = c.replace(old_lock, new_lock)

with open('Views/Student/Forms.cshtml', 'w', encoding='utf-8') as f:
    f.write(c)
