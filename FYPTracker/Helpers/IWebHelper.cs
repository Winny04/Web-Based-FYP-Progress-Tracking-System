namespace FYPTracker.Helpers
{
    public partial interface IWebHelper
    {
        string GetCurrentIpAddress();
        string GetThisPageUrl(bool includeQueryString, bool? useSsl = null, bool lowercaseUrl = false);
        string GetLocation(bool? useSsl = null);
        string GetHost(bool useSsl);
        bool IsCurrentConnectionSecured();
        string GetUrlReferrer();
    }
}
