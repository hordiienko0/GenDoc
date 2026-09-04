using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services.Documents;

namespace GenDoc.ViewModels.Archive
{
    public partial class RunGroupViewModel : ObservableObject
    {
        private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

        public RunGroupViewModel(RunDto dto)
        {
            Dto = dto;
        }

        public RunDto Dto { get; }
        public int Id => Dto.Id;

        public string DateText => Dto.RunAt.ToString("dd.MM.yyyy HH:mm", Uk);
        public string IntakeChip => Dto.IntakeLabel
            ?? (Dto.IntakeNumber is int n ? $"Набір №{n}" : "Без набору");
        public bool HasIntake => Dto.IntakeNumber is not null;
        public string PackageText => $"пакет «{Dto.PackageName}»";
        public string BranchText => string.IsNullOrWhiteSpace(Dto.BranchName) ? "" : $"· {Dto.BranchName}";
        public string DocsText => $"{Dto.GeneratedCount} док.";
        public string ErrorsText => Dto.ErrorCount == 0 ? "без помилок" : $"{Dto.ErrorCount} помилок";
        public bool HasErrors => Dto.ErrorCount > 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Glyph))]
        private bool isExpanded;

        public string Glyph => IsExpanded ? "▾" : "▸";

        public bool ItemsLoaded { get; set; }
        public ObservableCollection<RunItemRowViewModel> Items { get; } = new();
    }

    public class RunItemRowViewModel
    {
        public RunItemRowViewModel(RunItemDto dto)
        {
            Dto = dto;
        }

        public RunItemDto Dto { get; }
        public string Person => Dto.Person;
        public string TemplateName => Dto.TemplateName;
        public string Status => Dto.Status;
        public bool IsError => Dto.IsError;
        public bool CanOpen => Dto.DocumentId is not null && Dto.HasContent;
        public string SizeText => Dto.SizeBytes > 0 ? $"{Dto.SizeBytes / 1024.0:0.#} КБ" : "-";
    }
}
