using System.Printing;
using System.Windows.Controls;
using System.Windows.Documents;

namespace GenDoc.Services.Duty;

/// <summary>
/// Друк графіка через звичайне вікно друку Windows. Якщо принтер підключений - друкується на ньому;
/// якщо ні, одразу обрано «Microsoft Print to PDF», і Windows спитає, куди зберегти PDF.
/// </summary>
public static class DutyRosterPrinter
{
    public const string PdfPrinterName = "Microsoft Print to PDF";

    /// <summary>Принтери, що лише зберігають файл чи кудись пересилають, а не друкують на папері.</summary>
    private static readonly string[] VirtualPrinterMarks = { "PDF", "XPS", "OneNote", "Fax", "Факс" };

    /// <summary>Показує вікно друку; повертає назву принтера, на який пішов аркуш, або null, якщо скасовано.</summary>
    public static string? Print(DutyRosterData roster, string jobName)
        => Print(DutyRosterPrintLayout.Document(roster), jobName);

    /// <summary>Будь-який зверстаний під A4 альбомну документ (графік, список вечірньої перевірки) - у вікно друку.</summary>
    public static string? Print(FixedDocument document, string jobName)
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        if (PreferredQueue() is { } queue) dialog.PrintQueue = queue;
        Landscape(dialog);
        if (dialog.ShowDialog() != true) return null;

        // Аркуш зверстаний під A4 альбомну - навіть якщо в налаштуваннях принтера змінили орієнтацію.
        Landscape(dialog);
        dialog.PrintDocument(document.DocumentPaginator, jobName);
        return dialog.PrintQueue?.FullName;
    }

    private static void Landscape(PrintDialog dialog)
    {
        dialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
        dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
    }

    /// <summary>Принтер за замовчуванням, якщо він на зв'язку і друкує на папері; інакше - «Microsoft Print to PDF».</summary>
    private static PrintQueue? PreferredQueue()
    {
        try
        {
            var defaultQueue = LocalPrintServer.GetDefaultPrintQueue();
            if (defaultQueue is not null && IsPaperPrinterOnline(defaultQueue)) return defaultQueue;

            var pdf = new LocalPrintServer().GetPrintQueues()
                .FirstOrDefault(q => string.Equals(q.Name, PdfPrinterName, StringComparison.OrdinalIgnoreCase));
            return pdf ?? defaultQueue;
        }
        catch (PrintSystemException)
        {
            return null;
        }
    }

    internal static bool IsVirtual(string printerName)
        => VirtualPrinterMarks.Any(mark => printerName.Contains(mark, StringComparison.OrdinalIgnoreCase));

    private static bool IsPaperPrinterOnline(PrintQueue queue)
    {
        if (IsVirtual(queue.Name)) return false;
        queue.Refresh();
        return !queue.IsOffline && !queue.IsNotAvailable && !queue.IsInError && !queue.IsPaused;
    }
}
