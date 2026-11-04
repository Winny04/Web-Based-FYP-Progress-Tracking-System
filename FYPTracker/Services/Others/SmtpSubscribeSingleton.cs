using MailKit;
using MailKit.Net.Smtp;
using MimeKit;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace FYPTracker.Services.Others
{
    public class SmtpSubscribeSingleton : SmtpClient
    {
        protected override void OnSenderNotAccepted(MimeMessage message, MailboxAddress mailbox, SmtpResponse response)
        {
            base.OnSenderNotAccepted(message, mailbox, response);
        }

        public override string Send(FormatOptions options, MimeMessage message, CancellationToken cancellationToken = default, ITransferProgress progress = null)
        {
            return base.Send(options, message, cancellationToken, progress);
        }

    }
}
