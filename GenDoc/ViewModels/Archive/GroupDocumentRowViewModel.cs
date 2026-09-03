using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
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

        public string TemplateDisplay => Dto.HasContent ? Dto.TemplateName : $"{Dto.TemplateName} · файл не збережено";
        public string VersionText => $"в.{Dto.Version}";
        public string PeopleText => $"{Dto.RecipientCount} осіб";
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
