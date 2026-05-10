namespace eForm_dotnetCore.Configuration
{
    public static class EmailTemplateConfiguration
    {
        public const string SREmailContent = "SREmailContent";
        public const string POEmailContent = "POEmailContent";

        public static List<string> srRequireParamForEmail = new List<string>() { "{vendor}", "{url}" };
        public static List<string> poRequireParamForEmail = new List<string>() { "{poNo}"};

        public static Dictionary<string, (string,List<string>)> GetEmailTemplates = new Dictionary<string,(string,List<string>)>()
        {
            {SREmailContent,("{vendor} is key word which will then will replace by vendor name.{url} will be the world for RFQ sharing link."
                ,srRequireParamForEmail) },
            {POEmailContent,("PO Number: {poNo}.",poRequireParamForEmail) },
        };

        public static (string, List<string>)? GetEmailTemplateByType(string type)
        {
            if (GetEmailTemplates.ContainsKey(type))
            {
                return GetEmailTemplates[type];
            }

            return null;
        }
    }
}
