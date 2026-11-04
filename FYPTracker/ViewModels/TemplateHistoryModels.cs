using System;
using System.Collections.Generic;

namespace FYPTracker.ViewModels
{
    public class TemplateAuditEvent
    {
        public string EventId { get; set; } = Guid.NewGuid().ToString();
        public string TemplateKey { get; set; }
        public string TemplateVersion { get; set; }
        public string PreviousVersion { get; set; }
        public string ActionType { get; set; } // "Created", "Updated", "Restored"
        public string ModifiedBy { get; set; } // Admin Name/ID
        public DateTime DateModifiedUtc { get; set; } = DateTime.UtcNow;
        public List<TemplateFieldChange> FieldChanges { get; set; } = new List<TemplateFieldChange>();
        public string RestoredFromVersion { get; set; }
    }

    public class TemplateFieldChange
    {
        public string FieldKey { get; set; }
        public string PropertyName { get; set; }
        public string PreviousValue { get; set; }
        public string NewValue { get; set; }
    }

    public class FormTemplateSettingsViewModel
    {
        public MeetingRecordTemplateVM CurrentTemplate { get; set; }
        public List<TemplateAuditEvent> History { get; set; } = new List<TemplateAuditEvent>();
    }
}
