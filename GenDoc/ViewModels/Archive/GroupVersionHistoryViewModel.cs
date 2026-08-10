using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services.Documents;
using GenDoc.ViewModels.Personnel;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Archive
{
    public partial class GroupVersionRowViewModel : ObservableObject
    {
        private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

        public GroupVersionRowViewModel(GroupVersionDto dto)
        {
            Dto = dto;
        }

        public GroupVersionDto Dto { get; }

        public string VersionText => $"в.{Dto.Version}";
        public string DateText => Dto.CreatedAt.ToString("dd.MM.yyyy HH:mm", Uk);
        public string Author => Dto.Author;
        public string SizeText => Dto.SizeBytes > 0 ? $"{Dto.SizeBytes / 1024.0:0.#} КБ" : "—";
        public string PeopleText => $"{Dto.RecipientCount} осіб";
        public bool IsCurrent => Dto.IsCurrent;
        public bool CanOpen => Dto.HasContent;
        public bool CanMakeCurrent => !Dto.IsCurrent;
    }

    public partial class GroupVersionHistoryViewModel : DialogViewModelBase
    {
        private readonly IDocumentArchiveService _archiveService;
        private readonly int? _exportTemplateId;
        private readonly int? _docxTemplateId;

        // Групова серія ключується парою (ExportTemplateId, DocxTemplateId): XLSX-відомість
        // задає лише перше, груповий DOCX — лише друге; другий — завжди null для XLSX.
        public GroupVersionHistoryViewModel(
            IDocumentArchiveService archiveService, int? exportTemplateId, int? docxTemplateId, string templateName)
        {
            _archiveService = archiveService;
            _exportTemplateId = exportTemplateId;
            _docxTemplateId = docxTemplateId;
            HeaderText = templateName;
        }

        public string HeaderText { get; }
        public bool HasChanges { get; private set; }

        public ObservableCollection<GroupVersionRowViewModel> Versions { get; } = new();

        public async Task InitializeAsync()
        {
            Versions.Clear();
            foreach (var version in await _archiveService.GetGroupVersionsAsync(_exportTemplateId, _docxTemplateId))
                Versions.Add(new GroupVersionRowViewModel(version));
        }

        [RelayCommand]
        private async Task OpenVersionAsync(GroupVersionRowViewModel? row)
        {
            if (row is null || !row.CanOpen) return;
            try
            {
                var result = await _archiveService.OpenGroupAsync(row.Dto.Id);
                if (!result.Success)
                    MessageBox.Show(result.ErrorMessage, "Відкриття документа",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(
                    "Не вдалося відкрити: немає програми для .xlsx. Скористайтесь «Зберегти як…».",
                    "Відкриття документа", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task SaveVersionAsAsync(GroupVersionRowViewModel? row)
        {
            if (row is null || !row.CanOpen) return;

            var dialog = new SaveFileDialog { FileName = row.Dto.FileName };
            if (dialog.ShowDialog() != true) return;

            var result = await _archiveService.SaveGroupAsAsync(row.Dto.Id, dialog.FileName);
            if (!result.Success)
                MessageBox.Show(result.ErrorMessage, "Зберегти як",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        [RelayCommand]
        private async Task MakeCurrentAsync(GroupVersionRowViewModel? row)
        {
            if (row is null || !row.CanMakeCurrent) return;

            var confirm = MessageBox.Show(
                $"Зробити версію {row.VersionText} актуальною?",
                "Зміна актуальної версії", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            await _archiveService.MakeGroupCurrentAsync(row.Dto.Id);
            HasChanges = true;
            await InitializeAsync();
        }

        [RelayCommand]
        private void Close() => CloseDialog(HasChanges);
    }
}
