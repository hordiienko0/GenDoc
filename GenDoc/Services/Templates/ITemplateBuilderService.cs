using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    public record BuilderTestPerson(int Id, string Display);

    public record BuilderSignatory(int Id, string Display, string Rank, string ShortName);

    public record BuilderTemplateSource(int Id, string Name, TemplateBuilderDocument Document);

    public interface ITemplateBuilderService
    {
        IReadOnlyList<BuilderTestPerson> GetTestPeople();

        IReadOnlyList<BuilderSignatory> GetSignatories();

        IReadOnlyDictionary<string, string> ResolveValues(int recipientId, IReadOnlyList<string> tags);

        byte[] BuildDocx(TemplateBuilderDocument document);

        XlsxBuildResult BuildXlsx(TemplateBuilderDocument document);

        int Save(int? templateId, string name, TemplateBuilderDocument document);

        BuilderTemplateSource? Load(int templateId, TemplateBuilderMode mode);
    }
}
