namespace FYPTracker.Configuration
{
    public class SmtpSettings
    {
        // refresh token time to live (in days), inactive tokens are
        // automatically deleted from the database after this time
        public int RefreshTokenTTL { get; set; }

        public string Name { get; set; }
        public string EmailFrom { get; set; }
        public string SmtpHost { get; set; }
        public int SmtpPort { get; set; }
        public string SmtpUser { get; set; }
        public string SmtpPass { get; set; }
        public bool UseSSL { get; set; }
        public bool Offline { get; set; }
    }
}
