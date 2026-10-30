using System.Windows;
using GenDoc.Services;

namespace GenDoc.ViewModels.Common;

/// <summary>Перевірка місця, яке обрали в діалозі збереження: в OneDrive не пишемо.</summary>
public static class CloudFolderGuard
{
    /// <summary>true - шлях в OneDrive: попередження показано, зберігати не треба.</summary>
    public static bool Blocks(string path)
    {
        if (!CloudFolders.IsInOneDrive(path)) return false;

        MessageBox.Show(CloudFolders.Warning(path), "Тека в OneDrive", MessageBoxButton.OK, MessageBoxImage.Warning);
        return true;
    }
}
