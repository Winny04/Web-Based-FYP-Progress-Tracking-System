using FYPTracker.Configuration;
using FYPTracker.Services.Others.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace FYPTracker.Services.Others
{
    public class EmailService : IEmailService
    {
        private readonly SmtpSettings smtpSettings;
        private readonly IWebHostEnvironment _env;

        public EmailService(SmtpSettings smtpSettings, IWebHostEnvironment env)
        {
            this.smtpSettings = smtpSettings;
            _env = env;
        }

        /// <summary>
        /// Validates that in Development environment, a valid DevRedirectEmail is configured.
        /// Throws InvalidOperationException (fails closed) if running in Development and the redirect email is missing or invalid.
        /// Returns null if not running in Development (Production environment).
        /// </summary>
        private string? GetValidatedDevRedirectEmail()
        {
            if (!_env.IsDevelopment())
            {
                return null;
            }

            var configured = smtpSettings.DevRedirectEmail?.Trim();

            if (string.IsNullOrWhiteSpace(configured) || !configured.Contains('@') || configured.Length < 5)
            {
                Console.WriteLine("[EmailService] ⚠ EMAIL SAFETY BLOCK: Development email redirect is missing or invalid in configuration (SmtpSettings:DevRedirectEmail). Email delivery has been blocked to prevent sending to real addresses.");
                throw new InvalidOperationException(
                    "EMAIL SAFETY BLOCK: Development email redirect is missing or invalid in configuration (SmtpSettings:DevRedirectEmail). " +
                    "Email delivery has been blocked to prevent sending to real addresses. Please configure a valid test recipient email.");
            }

            return configured;
        }

        /// <summary>
        /// Masks an email address for safe display in logs and development subjects (e.g., "j***e@domain.com").
        /// </summary>
        private static string MaskEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "N/A";
            email = email.Trim();
            int atIndex = email.IndexOf('@');
            if (atIndex <= 0 || atIndex == email.Length - 1)
            {
                return email.Length <= 3 ? "***" : $"{email[0]}***{email[^1]}";
            }

            string user = email.Substring(0, atIndex);
            string domain = email.Substring(atIndex); // includes '@'

            if (user.Length <= 2)
            {
                return $"{user[0]}***{domain}";
            }

            return $"{user[0]}***{user[^1]}{domain}";
        }

        /// <summary>
        /// Masks multiple comma/semicolon-separated email addresses.
        /// </summary>
        private static string MaskEmailList(string? emails)
        {
            if (string.IsNullOrWhiteSpace(emails)) return "N/A";
            var parts = emails.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join("; ", parts.Select(p => MaskEmail(p.Trim())));
        }

        public void Send(string to, string subject, string html, string? filePath = null, string? from = null, string? cc = null)
        {
            if (smtpSettings.Offline)
            {
                Console.WriteLine($"[EmailService] ⏸ Offline mode active (SmtpSettings:Offline = true). Email delivery to \"{(_env.IsDevelopment() ? MaskEmailList(to) : to)}\" skipped.");
                throw new InvalidOperationException("Email delivery skipped: SMTP service is configured in Offline mode (SmtpSettings:Offline = true).");
            }

            try
            {
                var email = new MimeMessage();

                email.From.Add(MailboxAddress.Parse(from ?? smtpSettings.EmailFrom));

                var multiPart = new Multipart("mixed");

                // ── Development Email Safety & Redirection ───────────────────
                string actualRecipient;

                if (_env.IsDevelopment())
                {
                    // Fail-closed check: throws InvalidOperationException if DevRedirectEmail is missing or invalid
                    var devRedirectEmail = GetValidatedDevRedirectEmail()!;

                    actualRecipient = devRedirectEmail;

                    // Exclusively send to the authorized test mailbox
                    email.To.Add(MailboxAddress.Parse(devRedirectEmail));

                    // Do NOT add CC or BCC recipients to email headers in development.
                    // Annotate subject with MASKED original intended recipient(s) for test traceability
                    string maskedTo = MaskEmailList(to);
                    string maskedDetails = string.IsNullOrWhiteSpace(cc) ? maskedTo : $"{maskedTo} (CC: {MaskEmailList(cc)})";
                    subject = $"[DEV → {maskedDetails}] {subject}";

                    Console.WriteLine($"[EmailService] 📧 DEV REDIRECT: Original=\"{maskedDetails}\", Redirected=\"{devRedirectEmail}\"");
                }
                else
                {
                    // Preserve existing production email behaviour
                    actualRecipient = to;

                    foreach (var addr in to.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        email.To.Add(MailboxAddress.Parse(addr.Trim()));
                    }

                    if (!string.IsNullOrEmpty(cc))
                    {
                        email.Cc.Add(MailboxAddress.Parse(cc.Trim()));
                    }
                }
                // ── End Development Email Safety ─────────────────────────────

                email.Subject = subject;
                var bodyPart = new TextPart(TextFormat.Html) { Text = html };
                multiPart.Add(bodyPart);

                if(!string.IsNullOrEmpty(filePath))
                {
                    var attachment = new MimePart("application", "pdf")
                    {
                        Content = new MimeContent(File.OpenRead(filePath)),
                        ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                        ContentTransferEncoding = ContentEncoding.Base64,
                        FileName = Path.GetFileName(filePath)
                    };
                    multiPart.Add(attachment);
                }

                email.Body = multiPart;

                // send email (using standard platform certificate validation)
                using (var smtp = new SmtpSubscribeSingleton())
                {
                    smtp.Connect(smtpSettings.SmtpHost, smtpSettings.SmtpPort, SecureSocketOptions.SslOnConnect);
                    smtp.Authenticate(smtpSettings.SmtpUser, smtpSettings.SmtpPass);
                    smtp.Send(email);
                    smtp.Disconnect(true);
                    smtp.Dispose();
                }

                if (_env.IsDevelopment())
                    Console.WriteLine($"[EmailService] ✅ Email sent successfully to \"{actualRecipient}\".");
            }
            catch (Exception ex)
            {
                string safeTarget = _env.IsDevelopment() ? MaskEmailList(to) : to;
                Console.WriteLine($"[EmailService] ❌ Email sending failed to \"{safeTarget}\": {ex.Message}");
                throw;
            }
        }

        public async Task Send(List<string> to, string subject, string html, string? filePath = null, string? from = null)
        {
            // create message
            if (smtpSettings.Offline)
            {
                string safeTarget = _env.IsDevelopment() ? string.Join("; ", to.Select(MaskEmail)) : string.Join("; ", to);
                Console.WriteLine($"[EmailService] ⏸ Offline mode active (SmtpSettings:Offline = true). Email delivery to \"{safeTarget}\" skipped.");
                throw new InvalidOperationException("Email delivery skipped: SMTP service is configured in Offline mode (SmtpSettings:Offline = true).");
            }

            try
            {
                var email = new MimeMessage();

                email.From.Add(MailboxAddress.Parse(from ?? smtpSettings.EmailFrom));

                // ── Development Email Safety & Redirection ───────────────────
                string actualRecipient;
                string maskedRecipients = string.Join("; ", to.Select(MaskEmail));

                if (_env.IsDevelopment())
                {
                    // Fail-closed check: throws InvalidOperationException if DevRedirectEmail is missing or invalid
                    var devRedirectEmail = GetValidatedDevRedirectEmail()!;

                    actualRecipient = devRedirectEmail;

                    // Exclusively send to the authorized test mailbox
                    email.To.Add(MailboxAddress.Parse(devRedirectEmail));

                    // Annotate subject with MASKED original intended recipient(s) for test traceability
                    subject = $"[DEV → {maskedRecipients}] {subject}";

                    Console.WriteLine($"[EmailService] 📧 DEV REDIRECT: Original=\"{maskedRecipients}\", Redirected=\"{devRedirectEmail}\", Subject=\"{subject}\"");
                }
                else
                {
                    // Preserve existing production email behaviour
                    actualRecipient = string.Join("; ", to);
                    foreach (var address in to)
                    {
                        email.To.Add(MailboxAddress.Parse(address));
                    }
                }
                // ── End Development Email Safety ─────────────────────────────

                var multiPart = new Multipart("mixed");

                email.Subject = subject;
                var bodyPart = new TextPart(TextFormat.Html) { Text = html };
                multiPart.Add(bodyPart);
                if (!string.IsNullOrEmpty(filePath))
                {
                    var attachment = new MimePart("application", "pdf")
                    {
                        Content = new MimeContent(File.OpenRead(filePath)),
                        ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                        ContentTransferEncoding = ContentEncoding.Base64,
                        FileName = Path.GetFileName(filePath)
                    };
                    multiPart.Add(attachment);
                }

                email.Body = multiPart;

                // send email (using standard platform certificate validation)
                using (var smtp = new SmtpClient())
                {
                    try
                    {
                        await smtp.ConnectAsync(smtpSettings.SmtpHost, smtpSettings.SmtpPort, SecureSocketOptions.SslOnConnect);
                        await smtp.AuthenticateAsync(smtpSettings.SmtpUser, smtpSettings.SmtpPass);
                        await smtp.SendAsync(email);
                        await smtp.DisconnectAsync(true);
                        smtp.Dispose();
                    }
                    catch (SmtpCommandException ex)
                    {
                        Console.WriteLine("[EmailService] ❌ SMTP error: {0} (StatusCode: {1})", ex.Message, ex.StatusCode);
                        throw;
                    }
                    catch (SmtpProtocolException ex)
                    {
                        Console.WriteLine("[EmailService] ❌ Protocol error: {0}", ex.Message);
                        throw;
                    }
                }

                if (_env.IsDevelopment())
                    Console.WriteLine($"[EmailService] ✅ Email sent successfully to \"{actualRecipient}\".");
            }
            catch (Exception ex)
            {
                string safeTarget = _env.IsDevelopment() ? string.Join("; ", to.Select(MaskEmail)) : string.Join("; ", to);
                Console.WriteLine($"[EmailService] ❌ Email sending failed to \"{safeTarget}\": {ex.Message}");
                throw;
            }
        }
    }

}
