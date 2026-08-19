using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    /// <summary>Особа для селектора «Тестова особа» в попередньому перегляді.</summary>
    public record BuilderTestPerson(int Id, string Display);

    /// <summary>Посадова особа з постійного складу - кандидат у підписанти.</summary>
    public record BuilderSignatory(int Id, string Display, string Rank, string ShortName);

    public record BuilderTemplateSource(int Id, string Name, TemplateBuilderDocument Document);

    public interface ITemplateBuilderService
    {
        /// <summary>Особи активного набору; якщо активного набору нема - постійний склад.</summary>
        IReadOnlyList<BuilderTestPerson> GetTestPeople();

        /// <summary>Постійний склад: підписант обирається зі списку, а не вписується руками.</summary>
        IReadOnlyList<BuilderSignatory> GetSignatories();

        /// <summary>Значення тегів для попереднього перегляду. Manual-теги сюди не
        /// потрапляють - їх показує плашка «вводиться при генерації».</summary>
        IReadOnlyDictionary<string, string> ResolveValues(int recipientId, IReadOnlyList<string> tags);

        byte[] BuildDocx(TemplateBuilderDocument document);

        XlsxBuildResult BuildXlsx(TemplateBuilderDocument document);

        /// <summary>Зберігає як звичайний шаблон - Word у Template, відомість в
        /// ExportTemplate, - з байтами файлу і джерелом блоків у BuilderJson.
        /// Повертає Id у відповідній таблиці.</summary>
        int Save(int? templateId, string name, TemplateBuilderDocument document);

        /// <summary>null - шаблон завантажений файлом, конструктор його не відкриває.</summary>
        BuilderTemplateSource? Load(int templateId, TemplateBuilderMode mode);
    }
}
