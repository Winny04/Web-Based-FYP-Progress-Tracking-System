using System.Text.Json;
using FYPTracker.ViewModels;

namespace FYPTracker.Services.Forms
{
    public class MeetingLogTemplateService
    {
        private readonly IWebHostEnvironment _environment;

        public MeetingLogTemplateService(
            IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public async Task<MeetingRecordTemplateVM> LoadAsync(int stage)
        {
            string fileName = stage switch
            {
                1 => "Stage1RecordOfSupervisorMeeting6000CEM6002CMD.default.json",
                2 => "Stage2RecordOfSupervisorMeeting6001CEM303COM.default.json",
                _ => throw new ArgumentException(
                    "Unsupported meeting stage.")
            };

            string filePath = Path.Combine(
                _environment.ContentRootPath,
                "FormTemplates",
                fileName
            );

            if (!File.Exists(filePath))
                throw new FileNotFoundException(
                    "Meeting template not found.", filePath);

            string json = await File.ReadAllTextAsync(filePath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var template =
                JsonSerializer.Deserialize<MeetingRecordTemplateVM>(
                    json, options);

            if (template == null ||
                template.Fields == null ||
                template.Fields.Count == 0)
            {
                throw new InvalidDataException(
                    "Meeting template is invalid.");
            }

            return template;
        }
    }
}