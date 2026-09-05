using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services;
using GenDoc.Services.Documents;

namespace GenDoc.ViewModels.Archive
{
    public partial class GroupDocumentRowViewModel : ObservableObject
    {
        private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

        public GroupDocumentRowViewModel(GroupDocumentRowDto dto)
        {
            Dto = dto;
        }

        public GroupDocumentRowDto Dto { get; private set; }

        public int Id => Dto.Id;
        public int ExportTemplateId => Dto.ExportTemplateId;
        public bool HasContent => Dto.HasContent;
        public bool TemplateAlive => Dto.TemplateAlive;

        public string TemplateDisplay => string.Join(" · ", new[]
        {
            Dto.TemplateName,
            Dto.TemplateAlive ? null : "шаблон у кошику",
            Dto.HasContent ? null : "файл не збережено"
        }.Where(p => p is not null));
        public string? TemplateTooltip => Dto.TemplateAlive
            ? null
            : "Шаблон переміщено в кошик - відновіть його в «Шаблонах», щоб формувати нові версії";
        public string VersionText => $"в.{Dto.Version}";
        public int Version => Dto.Version;
        public int RecipientCount => Dto.RecipientCount;
        public DateTime GeneratedAt => Dto.GeneratedAt;
        public long SizeBytes => Dto.SizeBytes;
        public string PeopleText => $"{Dto.RecipientCount} {PluralHelper.Pluralize(Dto.RecipientCount, "особа", "особи", "осіб")}";
        public string DateText => Dto.GeneratedAt.ToString("dd.MM.yyyy", Uk);
        public string Author => Dto.Author;
        public string SizeText => Dto.SizeBytes > 0 ? $"{Dto.SizeBytes / 1024.0:0.#} КБ" : "-";

        [ObservableProperty]
        private bool isChecked;

        public void UpdateFrom(GroupDocumentRowDto dto)
        {
            Dto = dto;
            OnPropertyChanged(string.Empty);
        }
    }
}
