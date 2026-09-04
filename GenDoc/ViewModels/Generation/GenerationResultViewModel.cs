using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenDoc.Services.Generation;
using GenDoc.Services.Navigation;

namespace GenDoc.ViewModels.Generation;

public partial class GenerationResultViewModel : ObservableObject
{
    public GenerationResultViewModel(RunResult result, string outputFolder)
    {
        RunId = result.RunId;
        OutputFolder = outputFolder;
        FolderToOpen = ResolveFolderToOpen(outputFolder, result.FirstGeneratedPath);
        Issues = result.Issues ?? Array.Empty<RunIssue>();
        var generated = result.Generated + result.GroupGenerated + result.DocxGroupGenerated;
        var skipped = result.Skipped + result.GroupSkipped + result.DocxGroupSkipped;
        var errors = result.Errors + result.GroupErrors + result.DocxGroupErrors;
        SummaryText = $"Згенеровано {generated} · Пропущено {skipped} · Помилок {errors}";
        HasErrors = errors > 0;
    }

    public int RunId { get; }
    public string OutputFolder { get; }
    public string FolderToOpen { get; }
    public string SummaryText { get; }
    public bool HasErrors { get; }
    public IReadOnlyList<RunIssue> Issues { get; }
    public bool HasIssues => Issues.Count > 0;
    public bool CanShowInArchive => RunId > 0;

    internal static string ResolveFolderToOpen(string outputFolder, string? firstGeneratedPath)
    {
        if (string.IsNullOrWhiteSpace(firstGeneratedPath)) return outputFolder;
        var folder = Path.GetDirectoryName(firstGeneratedPath);
        return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : outputFolder;
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try { Process.Start("explorer.exe", $"\"{FolderToOpen}\""); } catch { }
    }

    [RelayCommand]
    private void ShowInArchive()
        => WeakReferenceMessenger.Default.Send(new NavigateToSectionMessage(Shell.MainViewModel.ArchiveSectionTitle, new ArchiveRunNavigationPayload(RunId)));
}
