import os

# ----------------- Index.cshtml -----------------
index_path = r"c:\Users\winny\source\repos\Web-Based-FYP-Progress-Tracking-System\FYPTracker\Views\Student\Index.cshtml"
index_content = """@using FYPTracker.Models
@model FYPProject?
@{
    ViewData["Title"] = "Student Dashboard";
    var currentUser = ViewBag.CurrentUser as ApplicationUser;
    var name = currentUser?.FullName ?? "Student";
    var studentId = currentUser?.EmployeeId ?? "S12345678";
    var supervisorName = Model?.Supervisor?.FullName ?? "Not Assigned";
    var session = !string.IsNullOrEmpty(currentUser?.AcademicSession) ? currentUser.AcademicSession : (!string.IsNullOrEmpty(Model?.AcademicSession) ? Model.AcademicSession : "—");
    
    var milestones = Model?.Milestones?.ToList() ?? new List<FYPMilestone>();
    
    // --- Submission Progress Calculation ---
    bool IsSubmitted(FYPMilestone m) {
        if (m == null) return false;
        if (m.SubmittedDate.HasValue) return true;
        if (m.Status == "Approved" || m.Status == "Completed" || m.Status == "AdminApproved" || m.Status == "SupervisorSigned") return true;
        return false;
    }

    var stage1Required = milestones.Where(m => m.Stage == 1 && m.IsRequired).ToList();
    var stage2Required = milestones.Where(m => m.Stage == 2 && m.IsRequired).ToList();

    int stage1Completed = stage1Required.Count(m => IsSubmitted(m));
    int stage2Completed = stage2Required.Count(m => IsSubmitted(m));

    double stage1Contrib = stage1Required.Count > 0 ? ((double)stage1Completed / stage1Required.Count) * 50.0 : 0;
    double stage2Contrib = stage2Required.Count > 0 ? ((double)stage2Completed / stage2Required.Count) * 50.0 : 0;
    
    int overallProgress = (int)Math.Round(stage1Contrib + stage2Contrib);
    string pbColor = overallProgress > 70 ? "bg-success" : (overallProgress > 30 ? "bg-primary" : "bg-warning");

    // --- Next Required Action ---
    FYPMilestone? nextAction = null;
    string actionTitle = "You're all caught up!";
    string actionDesc = "No pending actions are currently required.";
    string actionStatus = "Completed";
    string actionBtnText = "View Milestones";
    string actionLink = "/Student/ProjectDetails";
    string actionBadge = "bg-success";
    int? actionDaysLeft = null;

    var rejectedMilestone = milestones.FirstOrDefault(m => m.Status == "Rejected" || m.Status == "Revision" || m.Status == "ReturnedForChanges" || m.Status == "ReturnedForEdits");
    if (rejectedMilestone != null) {
        nextAction = rejectedMilestone;
        actionTitle = nextAction.Title;
        actionDesc = "Revision is required based on recent feedback. Please update your work and resubmit.";
        actionStatus = "Revision Required";
        actionBtnText = "Revise Submission";
        actionLink = "/Student/Forms"; // Or Stage2 based on Stage
        actionBadge = "bg-danger";
    }
    else {
        var unsubmitted = milestones.Where(m => m.IsRequired && !IsSubmitted(m) && m.Status != "Locked" && m.Status != "DefenseScheduled")
                                    .OrderBy(m => m.DueDate ?? DateTime.MaxValue).FirstOrDefault();
        if (unsubmitted != null) {
            nextAction = unsubmitted;
            actionTitle = nextAction.Title;
            actionDesc = $"Your next required milestone is due soon. Please prepare your submission.";
            actionStatus = "Not Submitted";
            actionBtnText = "Submit Now";
            actionLink = nextAction.Stage == 2 ? "/Student/Stage2" : "/Student/Forms";
            actionBadge = "bg-warning text-dark";
        }
        else {
            var pendingReview = milestones.Where(m => IsSubmitted(m) && m.Status != "Approved" && m.Status != "Completed" && m.Status != "AdminApproved")
                                          .OrderByDescending(m => m.SubmittedDate).FirstOrDefault();
            if (pendingReview != null) {
                nextAction = pendingReview;
                actionTitle = nextAction.Title;
                actionDesc = "Your submission is currently under review by your supervisor or examiner.";
                actionStatus = "Pending Review";
                actionBtnText = "View Submission";
                actionLink = nextAction.Stage == 2 ? "/Student/Stage2" : "/Student/Forms";
                actionBadge = "bg-info text-dark";
            }
        }
    }

    if (nextAction != null && nextAction.DueDate.HasValue) {
        actionDaysLeft = (int)(nextAction.DueDate.Value.Date - DateTime.Today).TotalDays;
    }

    // --- Upcoming Deadlines ---
    var upcomingDeadlines = milestones.Where(m => m.IsRequired && !IsSubmitted(m) && m.Status != "Locked")
                                      .OrderBy(m => m.DueDate ?? DateTime.MaxValue)
                                      .Take(4)
                                      .ToList();

    // --- Latest Feedback ---
    var latestFeedbacks = milestones.SelectMany(m => m.Feedbacks ?? new List<FYPFeedback>())
                                    .OrderByDescending(f => f.CreatedDate)
                                    .Take(3)
                                    .ToList();
}

<div class="container-xxl py-4">
    <!-- Welcome Header -->
    <div class="card border-0 shadow-sm rounded-4 mb-4" style="background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%); color: white;">
        <div class="card-body p-4 p-md-5 d-flex flex-wrap justify-content-between align-items-center gap-4">
            <div>
                <h2 class="fw-bolder mb-2">Welcome, @name 👋</h2>
                <div class="d-flex flex-wrap gap-3 align-items-center text-white-50 fs-7 fw-bold">
                    <span><i class="fas fa-id-card me-1"></i> @studentId</span>
                    <span>&bull;</span>
                    <span><i class="fas fa-calendar-alt me-1"></i> @session</span>
                    @if(Model != null) {
                        <span>&bull;</span>
                        <span><i class="fas fa-user-tie me-1"></i> Supervisor: @supervisorName</span>
                    }
                </div>
            </div>
            @if(Model == null) {
                <div>
                    <a href="/Student/SelectSupervisor" class="btn btn-primary fw-bold px-4 rounded-pill shadow-sm">Find a Supervisor <i class="fas fa-arrow-right ms-2"></i></a>
                </div>
            }
            else {
                <div>
                    <a href="/Student/ProjectDetails" class="btn btn-light text-dark fw-bold px-4 rounded-pill shadow-sm">View Project Details <i class="fas fa-arrow-right ms-2"></i></a>
                </div>
            }
        </div>
    </div>

    @if(Model != null) {
        <div class="row g-4 mb-4">
            <!-- Next Required Action -->
            <div class="col-lg-8">
                <div class="card border-0 shadow-sm rounded-4 h-100">
                    <div class="card-header bg-white border-bottom-0 pt-4 pb-0">
                        <h5 class="fw-bolder text-dark mb-0"><i class="fas fa-bolt text-warning me-2"></i> Next Required Action</h5>
                    </div>
                    <div class="card-body p-4 d-flex flex-column justify-content-center">
                        <div class="d-flex align-items-start gap-4 p-4 rounded-4" style="background-color: #f8fafc; border: 1px solid #e2e8f0;">
                            <div class="flex-shrink-0 mt-1">
                                @if (actionStatus == "Revision Required") { <i class="fas fa-exclamation-triangle text-danger fs-1"></i> }
                                else if (actionStatus == "Not Submitted") { <i class="fas fa-file-upload text-warning fs-1"></i> }
                                else if (actionStatus == "Pending Review") { <i class="fas fa-hourglass-half text-info fs-1"></i> }
                                else { <i class="fas fa-check-circle text-success fs-1"></i> }
                            </div>
                            <div class="flex-grow-1">
                                <div class="d-flex justify-content-between align-items-center mb-1">
                                    <h4 class="fw-bolder text-dark mb-0">@actionTitle</h4>
                                    <span class="badge @actionBadge rounded-pill px-3 py-2 fw-bold">@actionStatus</span>
                                </div>
                                <p class="text-muted fs-6 mb-3">@actionDesc</p>
                                
                                @if (nextAction != null && nextAction.DueDate.HasValue) {
                                    <div class="d-flex align-items-center gap-3 mb-3">
                                        <div class="text-muted fs-7 fw-bold"><i class="far fa-calendar me-1"></i> Due: @nextAction.DueDate.Value.ToString("dd MMM yyyy")</div>
                                        @if (actionDaysLeft.HasValue) {
                                            if (actionDaysLeft.Value < 0) { <span class="badge bg-danger rounded-pill"><i class="fas fa-exclamation me-1"></i> Overdue by @Math.Abs(actionDaysLeft.Value) days</span> }
                                            else if (actionDaysLeft.Value <= 5) { <span class="badge bg-warning text-dark rounded-pill"><i class="fas fa-clock me-1"></i> @actionDaysLeft.Value days left</span> }
                                            else { <span class="badge bg-light text-muted border rounded-pill"><i class="fas fa-clock me-1"></i> @actionDaysLeft.Value days left</span> }
                                        }
                                    </div>
                                }
                                <div>
                                    <a href="@actionLink" class="btn btn-primary fw-bold px-4 rounded-pill">@actionBtnText</a>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Submission Progress -->
            <div class="col-lg-4">
                <div class="card border-0 shadow-sm rounded-4 h-100">
                    <div class="card-header bg-white border-bottom-0 pt-4 pb-0">
                        <h5 class="fw-bolder text-dark mb-0"><i class="fas fa-chart-pie text-primary me-2"></i> Submission Progress</h5>
                    </div>
                    <div class="card-body p-4 d-flex flex-column justify-content-center">
                        <div class="text-center mb-4">
                            <h1 class="display-3 fw-bolder text-dark mb-0">@overallProgress%</h1>
                            <div class="text-muted fw-bold">Overall Submission Progress</div>
                        </div>
                        <div class="progress mb-4" style="height: 12px; border-radius: 99px;">
                            <div class="progress-bar @pbColor progress-bar-striped progress-bar-animated" role="progressbar" style="width: @overallProgress%" aria-valuenow="@overallProgress" aria-valuemin="0" aria-valuemax="100"></div>
                        </div>
                        <div class="d-flex justify-content-between text-muted fs-7 fw-bold mb-2">
                            <span>Stage 1</span>
                            <span>@stage1Completed / @stage1Required.Count Completed</span>
                        </div>
                        <div class="d-flex justify-content-between text-muted fs-7 fw-bold">
                            <span>Stage 2</span>
                            <span>@stage2Completed / @stage2Required.Count Completed</span>
                        </div>
                        <div class="mt-4 text-center">
                            <span class="badge bg-light text-muted border fs-8 text-wrap px-3 py-2"><i class="fas fa-info-circle me-1"></i> Calculated based on required submissions (50% Stage 1, 50% Stage 2). Meeting assessments are graded separately.</span>
                        </div>
                    </div>
                </div>
            </div>
        </div>

        <div class="row g-4">
            <!-- Upcoming Deadlines -->
            <div class="col-lg-6">
                <div class="card border-0 shadow-sm rounded-4 h-100">
                    <div class="card-header bg-white border-bottom pt-4 pb-3 d-flex justify-content-between align-items-center">
                        <h5 class="fw-bolder text-dark mb-0"><i class="far fa-calendar-alt text-info me-2"></i> Upcoming Deadlines</h5>
                        <a href="/Student/ProjectDetails" class="text-primary fw-bold text-decoration-none fs-7">View All</a>
                    </div>
                    <div class="card-body p-0">
                        @if(upcomingDeadlines.Any()) {
                            <ul class="list-group list-group-flush rounded-4">
                                @foreach(var ms in upcomingDeadlines) {
                                    int days = ms.DueDate.HasValue ? (int)(ms.DueDate.Value.Date - DateTime.Today).TotalDays : 0;
                                    <li class="list-group-item p-4 border-bottom">
                                        <div class="d-flex justify-content-between align-items-start">
                                            <div>
                                                <h6 class="fw-bold text-dark mb-1">@ms.Title</h6>
                                                <div class="text-muted fs-7 fw-bold">Due: @(ms.DueDate?.ToString("dd MMM yyyy") ?? "-")</div>
                                            </div>
                                            @if (days < 0) { <span class="badge bg-danger rounded-pill px-3 py-2">Overdue</span> }
                                            else if (days <= 7) { <span class="badge bg-warning text-dark rounded-pill px-3 py-2">In @days days</span> }
                                            else { <span class="badge bg-light border text-muted rounded-pill px-3 py-2">In @days days</span> }
                                        </div>
                                    </li>
                                }
                            </ul>
                        }
                        else {
                            <div class="text-center p-5">
                                <i class="fas fa-calendar-check text-muted fs-1 opacity-25 mb-3"></i>
                                <h6 class="fw-bold text-dark mb-1">No upcoming deadlines</h6>
                                <p class="text-muted fs-7 mb-0">You have no pending required milestones.</p>
                            </div>
                        }
                    </div>
                </div>
            </div>

            <!-- Latest Feedback -->
            <div class="col-lg-6">
                <div class="card border-0 shadow-sm rounded-4 h-100">
                    <div class="card-header bg-white border-bottom pt-4 pb-3">
                        <h5 class="fw-bolder text-dark mb-0"><i class="fas fa-comment-dots text-primary me-2"></i> Latest Feedback</h5>
                    </div>
                    <div class="card-body p-0">
                        @if(latestFeedbacks.Any()) {
                            <ul class="list-group list-group-flush rounded-4">
                                @foreach(var fb in latestFeedbacks) {
                                    <li class="list-group-item p-4 border-bottom">
                                        <div class="d-flex align-items-start gap-3">
                                            <div class="bg-light-primary text-primary rounded-circle d-flex justify-content-center align-items-center" style="width: 40px; height: 40px; min-width: 40px;">
                                                <i class="fas fa-user-tie"></i>
                                            </div>
                                            <div>
                                                <h6 class="fw-bold text-dark mb-1">@fb.Milestone?.Title</h6>
                                                <p class="text-muted mb-2 fs-7" style="display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;">@fb.Comment</p>
                                                <div class="d-flex justify-content-between align-items-center">
                                                    <span class="text-muted fw-bold" style="font-size: 0.65rem;"><i class="far fa-clock me-1"></i> @fb.CreatedDate.ToString("dd MMM yyyy")</span>
                                                    <a href="/Student/ProjectDetails" class="text-primary fw-bold text-decoration-none" style="font-size: 0.75rem;">View Details</a>
                                                </div>
                                            </div>
                                        </div>
                                    </li>
                                }
                            </ul>
                        }
                        else {
                            <div class="text-center p-5">
                                <i class="fas fa-inbox text-muted fs-1 opacity-25 mb-3"></i>
                                <h6 class="fw-bold text-dark mb-1">No feedback yet</h6>
                                <p class="text-muted fs-7 mb-0">Your latest submission feedback will appear here.</p>
                            </div>
                        }
                    </div>
                </div>
            </div>
        </div>
    }
</div>

<style>
    .bg-light-primary { background-color: #f1faff !important; }
</style>
"""

with open(index_path, "w", encoding="utf-8") as f:
    f.write(index_content)


# ----------------- ProjectDetails.cshtml -----------------
project_path = r"c:\Users\winny\source\repos\Web-Based-FYP-Progress-Tracking-System\FYPTracker\Views\Student\ProjectDetails.cshtml"
project_content = """@using FYPTracker.Models
@model FYPProject
@{
    ViewData["Title"] = "Project Details";
    var currentUser = ViewBag.CurrentUser as ApplicationUser;
    
    var milestones = Model?.Milestones?.ToList() ?? new List<FYPMilestone>();
    var stage1 = milestones.Where(m => m.Stage == 1).OrderBy(m => m.DueDate ?? DateTime.MaxValue).ToList();
    var stage2 = milestones.Where(m => m.Stage == 2).OrderBy(m => m.DueDate ?? DateTime.MaxValue).ToList();

    bool IsSubmitted(FYPMilestone m) {
        if (m == null) return false;
        if (m.SubmittedDate.HasValue) return true;
        if (m.Status == "Approved" || m.Status == "Completed" || m.Status == "AdminApproved" || m.Status == "SupervisorSigned") return true;
        return false;
    }

    var stage1Required = stage1.Where(m => m.IsRequired).ToList();
    var stage2Required = stage2.Where(m => m.IsRequired).ToList();

    int stage1Completed = stage1Required.Count(m => IsSubmitted(m));
    int stage2Completed = stage2Required.Count(m => IsSubmitted(m));

    double stage1Contrib = stage1Required.Count > 0 ? ((double)stage1Completed / stage1Required.Count) * 50.0 : 0;
    double stage2Contrib = stage2Required.Count > 0 ? ((double)stage2Completed / stage2Required.Count) * 50.0 : 0;
    int overallProgress = (int)Math.Round(stage1Contrib + stage2Contrib);
    string pbColor = overallProgress > 70 ? "bg-success" : (overallProgress > 30 ? "bg-primary" : "bg-warning");
}

<div class="container-xxl py-4">
    <!-- Header -->
    <div class="card border-0 shadow-sm rounded-4 mb-4" style="background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%); color: white;">
        <div class="card-body p-4 p-md-5 d-flex flex-wrap justify-content-between align-items-center gap-4">
            <div>
                <h2 class="fw-bolder mb-2">Project Details</h2>
                <div class="text-white-50 fs-6 fw-bold">Overview and complete timeline for your FYP.</div>
            </div>
            <div>
                <a href="/Student/Index" class="btn btn-outline-light fw-bold px-4 rounded-pill shadow-sm"><i class="fas fa-arrow-left me-2"></i> Back to Dashboard</a>
            </div>
        </div>
    </div>

    @if(Model == null) {
        <div class="alert alert-warning border-0 shadow-sm rounded-4 p-4"><i class="fas fa-exclamation-triangle me-2"></i> No active project found.</div>
        return;
    }

    <div class="row g-4 mb-4">
        <!-- Project Info -->
        <div class="col-lg-8">
            <div class="card border-0 shadow-sm rounded-4 h-100">
                <div class="card-header bg-white border-bottom pt-4 pb-3">
                    <h5 class="fw-bolder text-dark mb-0"><i class="fas fa-laptop-code text-primary me-2"></i> Project Overview</h5>
                </div>
                <div class="card-body p-4">
                    <h4 class="fw-bold text-dark mb-3">@(Model.Title ?? "Untitled Project")</h4>
                    <p class="text-muted fs-6 mb-4">@(string.IsNullOrEmpty(Model.Description) ? "No description provided." : Model.Description)</p>
                    
                    <div class="row g-3">
                        <div class="col-md-6">
                            <div class="bg-light rounded p-3 h-100 border">
                                <div class="text-muted fs-7 fw-bold text-uppercase mb-1">Supervisor</div>
                                <div class="d-flex align-items-center gap-2">
                                    <div class="bg-primary text-white rounded-circle d-flex align-items-center justify-content-center" style="width: 32px; height: 32px;"><i class="fas fa-user-tie"></i></div>
                                    <span class="fw-bolder text-dark">@(Model.Supervisor?.FullName ?? "Not Assigned")</span>
                                </div>
                            </div>
                        </div>
                        <div class="col-md-6">
                            <div class="bg-light rounded p-3 h-100 border">
                                <div class="text-muted fs-7 fw-bold text-uppercase mb-1">Examiner</div>
                                <div class="d-flex align-items-center gap-2">
                                    <div class="bg-info text-white rounded-circle d-flex align-items-center justify-content-center" style="width: 32px; height: 32px;"><i class="fas fa-user-graduate"></i></div>
                                    <span class="fw-bolder text-dark">@(Model.AssignedExaminers?.Any() == true ? string.Join(", ", Model.AssignedExaminers.Select(e => e.Examiner?.FullName)) : "Not Assigned")</span>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>

        <!-- Progress Mini -->
        <div class="col-lg-4">
            <div class="card border-0 shadow-sm rounded-4 h-100">
                <div class="card-body p-4 d-flex flex-column justify-content-center text-center">
                    <h1 class="display-3 fw-bolder text-dark mb-0">@overallProgress%</h1>
                    <div class="text-muted fw-bold mb-4">Overall Submission Progress</div>
                    
                    <div class="progress mb-3" style="height: 10px; border-radius: 99px;">
                        <div class="progress-bar @pbColor progress-bar-striped progress-bar-animated" role="progressbar" style="width: @overallProgress%"></div>
                    </div>
                    
                    <div class="d-flex justify-content-between text-muted fs-7 fw-bold mb-1">
                        <span>Stage 1 Completed:</span>
                        <span>@stage1Completed / @stage1Required.Count</span>
                    </div>
                    <div class="d-flex justify-content-between text-muted fs-7 fw-bold">
                        <span>Stage 2 Completed:</span>
                        <span>@stage2Completed / @stage2Required.Count</span>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <!-- Milestone Timeline -->
    <div class="card border-0 shadow-sm rounded-4 mb-4">
        <div class="card-header bg-white border-bottom pt-4 pb-3">
            <h5 class="fw-bolder text-dark mb-0"><i class="fas fa-stream text-primary me-2"></i> Milestone Timeline</h5>
        </div>
        <div class="card-body p-0">
            <div class="accordion accordion-flush" id="timelineAccordion">
                
                <!-- Stage 1 -->
                <div class="accordion-item">
                    <h2 class="accordion-header" id="headingStage1">
                        <button class="accordion-button fw-bold fs-5 px-4 py-3" type="button" data-bs-toggle="collapse" data-bs-target="#collapseStage1" aria-expanded="true">
                            <i class="fas fa-tasks text-primary me-2"></i> Stage 1 Milestones
                            @if(Model.Stage1Grade == "Pass") { <span class="badge bg-success ms-3 fs-7">Passed</span> }
                            else if(Model.Stage1Grade == "Fail") { <span class="badge bg-danger ms-3 fs-7">Failed</span> }
                        </button>
                    </h2>
                    <div id="collapseStage1" class="accordion-collapse collapse show" data-bs-parent="#timelineAccordion">
                        <div class="accordion-body p-0">
                            <div class="table-responsive">
                                <table class="table table-hover align-middle mb-0">
                                    <thead class="bg-light">
                                        <tr>
                                            <th class="ps-4 text-muted fw-bold text-uppercase fs-8">Milestone</th>
                                            <th class="text-muted fw-bold text-uppercase fs-8">Due Date</th>
                                            <th class="text-muted fw-bold text-uppercase fs-8">Date Submitted</th>
                                            <th class="text-center text-muted fw-bold text-uppercase fs-8">Status</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        @foreach(var ms in stage1) {
                                            string statusClass = "bg-light text-muted border";
                                            string statusText = ms.Status ?? "Pending";
                                            if (!ms.SubmittedDate.HasValue && statusText == "Pending") { statusText = "Not Submitted"; statusClass = "bg-light text-muted border"; }
                                            else if (statusText == "Submitted" || statusText == "UnderReview" || statusText == "PendingSupervisorReview") { statusText = "Pending Review"; statusClass = "bg-warning text-dark border border-warning"; }
                                            else if (statusText == "Approved" || statusText == "Completed" || statusText == "AdminApproved" || statusText == "SupervisorSigned") { statusText = "Approved"; statusClass = "bg-success text-white"; }
                                            else if (statusText == "Rejected" || statusText == "Revision" || statusText == "ReturnedForChanges") { statusText = "Revision Required"; statusClass = "bg-danger text-white"; }
                                            else if (statusText == "Locked") { statusText = "Locked"; statusClass = "bg-secondary text-white"; }
                                            
                                            <tr>
                                                <td class="ps-4 py-3">
                                                    <div class="fw-bolder text-dark">@ms.Title</div>
                                                    <span class="badge bg-light text-muted border fs-8 mt-1">@(ms.IsRequired ? "Required" : "Optional")</span>
                                                </td>
                                                <td class="text-muted fw-bold fs-7">@(ms.DueDate?.ToString("dd MMM yyyy, hh:mm tt") ?? "-")</td>
                                                <td class="text-dark fw-bold fs-7">@(ms.SubmittedDate?.ToString("dd MMM yyyy, hh:mm tt") ?? "-")</td>
                                                <td class="text-center"><span class="badge @statusClass px-3 py-2 fw-bold">@statusText</span></td>
                                            </tr>
                                        }
                                        @if(!stage1.Any()) {
                                            <tr><td colspan="4" class="text-center py-4 text-muted">No milestones found for Stage 1.</td></tr>
                                        }
                                    </tbody>
                                </table>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- Stage 2 -->
                <div class="accordion-item border-bottom-0">
                    <h2 class="accordion-header" id="headingStage2">
                        <button class="accordion-button @(Model.Stage1Grade == "Pass" ? "" : "collapsed") fw-bold fs-5 px-4 py-3" type="button" data-bs-toggle="collapse" data-bs-target="#collapseStage2" aria-expanded="@(Model.Stage1Grade == "Pass" ? "true" : "false")">
                            <i class="fas fa-flag-checkered text-success me-2"></i> Stage 2 Milestones
                            @if(Model.Stage1Grade != "Pass") { <span class="badge bg-secondary ms-3 fs-7"><i class="fas fa-lock me-1"></i> Locked</span> }
                        </button>
                    </h2>
                    <div id="collapseStage2" class="accordion-collapse collapse @(Model.Stage1Grade == "Pass" ? "show" : "")" data-bs-parent="#timelineAccordion">
                        <div class="accordion-body p-0">
                            @if(Model.Stage1Grade != "Pass") {
                                <div class="text-center py-5 bg-light">
                                    <i class="fas fa-lock text-muted fs-1 opacity-25 mb-3"></i>
                                    <h6 class="fw-bold text-dark mb-1">Stage 2 is Locked</h6>
                                    <p class="text-muted fs-7 mb-0">You must pass Stage 1 before you can unlock and access Stage 2 milestones.</p>
                                </div>
                            }
                            else {
                                <div class="table-responsive">
                                    <table class="table table-hover align-middle mb-0">
                                        <thead class="bg-light">
                                            <tr>
                                                <th class="ps-4 text-muted fw-bold text-uppercase fs-8">Milestone</th>
                                                <th class="text-muted fw-bold text-uppercase fs-8">Due Date</th>
                                                <th class="text-muted fw-bold text-uppercase fs-8">Date Submitted</th>
                                                <th class="text-center text-muted fw-bold text-uppercase fs-8">Status</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            @foreach(var ms in stage2) {
                                                string statusClass = "bg-light text-muted border";
                                                string statusText = ms.Status ?? "Pending";
                                                if (!ms.SubmittedDate.HasValue && statusText == "Pending") { statusText = "Not Submitted"; statusClass = "bg-light text-muted border"; }
                                                else if (statusText == "Submitted" || statusText == "UnderReview" || statusText == "PendingSupervisorReview") { statusText = "Pending Review"; statusClass = "bg-warning text-dark border border-warning"; }
                                                else if (statusText == "Approved" || statusText == "Completed" || statusText == "AdminApproved" || statusText == "SupervisorSigned") { statusText = "Approved"; statusClass = "bg-success text-white"; }
                                                else if (statusText == "Rejected" || statusText == "Revision" || statusText == "ReturnedForChanges") { statusText = "Revision Required"; statusClass = "bg-danger text-white"; }
                                                else if (statusText == "Locked") { statusText = "Locked"; statusClass = "bg-secondary text-white"; }
                                                
                                                <tr>
                                                    <td class="ps-4 py-3">
                                                        <div class="fw-bolder text-dark">@ms.Title</div>
                                                        <span class="badge bg-light text-muted border fs-8 mt-1">@(ms.IsRequired ? "Required" : "Optional")</span>
                                                    </td>
                                                    <td class="text-muted fw-bold fs-7">@(ms.DueDate?.ToString("dd MMM yyyy, hh:mm tt") ?? "-")</td>
                                                    <td class="text-dark fw-bold fs-7">@(ms.SubmittedDate?.ToString("dd MMM yyyy, hh:mm tt") ?? "-")</td>
                                                    <td class="text-center"><span class="badge @statusClass px-3 py-2 fw-bold">@statusText</span></td>
                                                </tr>
                                            }
                                            @if(!stage2.Any()) {
                                                <tr><td colspan="4" class="text-center py-4 text-muted">No milestones found for Stage 2.</td></tr>
                                            }
                                        </tbody>
                                    </table>
                                </div>
                            }
                        </div>
                    </div>
                </div>

            </div>
        </div>
    </div>
</div>
"""

with open(project_path, "w", encoding="utf-8") as f:
    f.write(project_content)
