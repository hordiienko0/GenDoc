using System.Security.Cryptography;
using System.Text;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Documents
{
    public static class RecipientHashSources
    {
        public static IQueryable<Recipient> WithHashSources(this IQueryable<Recipient> query) => query
            .Include(r => r.Unit)
            .Include(r => r.Room)
            .Include(r => r.OrgNode)
            .Include(r => r.Weapons);
    }

    public interface IDocumentHashService
    {
        string ComputeSourceHash(
            List<TemplateFieldMapping> mappings, Recipient recipient, OrganizationSettings? orgSettings);

        string ComputeSourceHash(
            List<TemplateFieldMapping> mappings, Recipient recipient, OrganizationSettings? orgSettings,
            Dictionary<string, string> manualValues, string? courseOfficerSignature);

        string ComputeRosterHash(
            int exportTemplateId, IReadOnlyList<(int RecipientId, string SourceHash)> roster,
            string? manualFingerprint = null);
    }

    public class DocumentHashService : IDocumentHashService
    {
        private const char ManualSeparator = '+';

        public string ComputeSourceHash(
            List<TemplateFieldMapping> mappings, Recipient recipient, OrganizationSettings? orgSettings)
        {
            var autoMappings = mappings.Where(m => m.SourceType != MappingSourceType.Manual).ToList();
            var values = GenerationService.BuildValues(
                autoMappings, recipient, orgSettings, new Dictionary<string, string>());

            return Hash(values);
        }

        public string ComputeSourceHash(
            List<TemplateFieldMapping> mappings, Recipient recipient, OrganizationSettings? orgSettings,
            Dictionary<string, string> manualValues, string? courseOfficerSignature)
        {
            var auto = ComputeSourceHash(mappings, recipient, orgSettings);

            var manualMappings = mappings.Where(IsManualDriven).ToList();
            if (manualMappings.Count == 0) return auto;

            var values = GenerationService.BuildValues(
                manualMappings, recipient, orgSettings, manualValues, courseOfficerSignature);

            return auto + ManualSeparator + Hash(values);
        }

        public string ComputeRosterHash(
            int exportTemplateId, IReadOnlyList<(int RecipientId, string SourceHash)> roster,
            string? manualFingerprint = null)
        {
            var joined = $"template={exportTemplateId};" + string.Join(
                ";", roster.Select(r => $"{r.RecipientId}={r.SourceHash}"));
            var auto = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));

            return string.IsNullOrEmpty(manualFingerprint) ? auto : auto + ManualSeparator + manualFingerprint;
        }

        public static string AutoPart(string? sourceHash)
        {
            if (string.IsNullOrEmpty(sourceHash)) return string.Empty;
            var separator = sourceHash.IndexOf(ManualSeparator);
            return separator < 0 ? sourceHash : sourceHash[..separator];
        }

        public static bool IsManualDriven(TemplateFieldMapping mapping) => mapping.SourceType switch
        {
            MappingSourceType.Manual => true,
            MappingSourceType.Recipient => mapping.FieldName
                is nameof(ExportFieldKey.CourseOfficerSignature)
                or nameof(ExportFieldKey.RowNumber),
            _ => false
        };

        public static bool IsManualDriven(ExportTemplateColumnMapping mapping)
            => mapping.SourceType == MappingSourceType.Manual
               || mapping.FieldKey == nameof(ExportFieldKey.CourseOfficerSignature);

        public static string ManualFingerprint(IEnumerable<KeyValuePair<string, string>> values) => Hash(values);

        private static string Hash(IEnumerable<KeyValuePair<string, string>> values)
        {
            var joined = string.Join("", values
                .OrderBy(v => v.Key, StringComparer.Ordinal)
                .Select(v => $"{v.Key}={v.Value}"));

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
        }
    }
}
