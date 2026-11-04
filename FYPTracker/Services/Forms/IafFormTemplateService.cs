using System.Text.Json;
using FYPTracker.ViewModels;

namespace FYPTracker.Services.Forms
{
    public class IafFormTemplateService
    {
        private readonly IWebHostEnvironment _environment;

        public IafFormTemplateService(
            IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public async Task<MeetingRecordTemplateVM> LoadAsync()
        {
            string path = Path.Combine(
                _environment.ContentRootPath,
                "FormTemplates",
                "InitialProjectAllocationForm.default.json"
            );

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Admin IAF Template not found.",
                    path
                );
            }

            string json = await File.ReadAllTextAsync(path);

            var template =
                JsonSerializer.Deserialize<MeetingRecordTemplateVM>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                );

            if (template == null ||
                template.Fields == null ||
                template.Fields.Count == 0)
            {
                throw new InvalidOperationException(
                    "Invalid Admin IAF Template."
                );
            }

            return template;
        }
    }
}