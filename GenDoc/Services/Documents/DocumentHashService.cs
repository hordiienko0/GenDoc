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

        string ComputeRosterHash(int exportTemplateId, IReadOnlyList<(int RecipientId, string SourceHash)> roster);
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

        public string ComputeRosterHash(int exportTemplateId, IReadOnlyList<(int RecipientId, string SourceHash)> roster)
        {
            var joined = $"template={exportTemplateId};" + string.Join(
                ";", roster.Select(r => $"{r.RecipientId}={r.SourceHash}"));

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
        }
    }
}
