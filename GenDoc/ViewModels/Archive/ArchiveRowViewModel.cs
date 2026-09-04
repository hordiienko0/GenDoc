using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;

namespace GenDoc.ViewModels.Archive
{
    public partial class ArchiveRowViewModel : ObservableObject
    {
        private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

        public ArchiveRowViewModel(ArchiveRowDto dto)
        {
            Dto = dto;
        }

        public ArchiveRowDto Dto { get; private set; }

        public int Id => Dto.Id;
        public int RecipientId => Dto.RecipientId;
        public int TemplateId => Dto.TemplateId;
        public bool HasContent => Dto.HasContent;
        public bool TemplateAlive => Dto.TemplateAlive;
        public bool RecipientAlive => Dto.RecipientAlive;
        public DocumentSourceType SourceType => Dto.SourceType;

        public string ShortName
        {
            get
            {
                var initials = new[] { Dto.FirstName, Dto.MiddleName }
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => $"{char.ToUpperInvariant(p![0])}.");
                var suffix = string.Join("", initials);
                var last = Dto.LastName.ToUpper(Uk);
                return suffix.Length > 0 ? $"{last} {suffix}" : last;
            }
        }

        public string TemplateDisplay => Dto.HasContent ? Dto.TemplateName : $"{Dto.TemplateName} · файл не збережено";
        public bool IsDim => !Dto.HasContent;
        public string VersionText => $"в.{Dto.Version}";
        public bool VersionIsChip => Dto.Version > 1;
        public string IntakeText => Dto.IntakeNumber is int n ? $"№{n}" : "-";
        public string UnitLast => (Dto.OrgPathSnapshot ?? "-")
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? "-";
        public string UnitFull => Dto.OrgPathSnapshot ?? "-";
        public string DateText => Dto.CreatedAt.ToString("dd.MM.yyyy", Uk);
        public string Author => Dto.Author;
        public string AttachText => Dto.AttachmentCount > 0 ? $"▣{Dto.AttachmentCount}" : "-";

        public string SearchHaystack => string.Join(' ', new[]
        {
            Dto.LastName, Dto.FirstName, Dto.MiddleName, Dto.FileName
        }.Where(p => !string.IsNullOrWhiteSpace(p)));

        [ObservableProperty]
        private bool isChecked;

        public void UpdateFrom(ArchiveRowDto dto)
        {
            Dto = dto;
            OnPropertyChanged(string.Empty);
        }
    }
}
