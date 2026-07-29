using System.Security.Cryptography;
using System.Text;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;

namespace GenDoc.Services.Documents
{
    public interface IDocumentHashService
    {
        // Хеш значень усіх нерукописних міток шаблону для людини —
        // порівнюється з GeneratedDocument.SourceHash для стану «застарів».
        string ComputeSourceHash(
            List<TemplateFieldMapping> mappings, Recipient recipient, OrganizationSettings? orgSettings);
    }

    public class DocumentHashService : IDocumentHashService
    {
        public string ComputeSourceHash(
            List<TemplateFieldMapping> mappings, Recipient recipient, OrganizationSettings? orgSettings)
        {
            var autoMappings = mappings.Where(m => m.SourceType != MappingSourceType.Manual).ToList();
            var values = GenerationService.BuildValues(
                autoMappings, recipient, orgSettings, new Dictionary<string, string>());

            var joined = string.Join("", values
                .OrderBy(v => v.Key, StringComparer.Ordinal)
                .Select(v => $"{v.Key}={v.Value}"));

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
        }
    }
}
