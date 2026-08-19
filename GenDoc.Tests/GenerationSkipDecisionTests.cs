using System.IO;
using GenDoc.Services.Generation;

namespace GenDoc.Tests;

// Раніше пропуск залежав лише від наявності запису в архіві БД. Якщо користувач
// обирав іншу теку (або переносив/видаляв файли), запуск завершувався з «усе
// пропущено» і порожньою текою. Тепер пропуск вимагає ще й файлу на місці.
public class GenerationSkipDecisionTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "gendoc-skip-" + Guid.NewGuid().ToString("N"));

    public GenerationSkipDecisionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ExistsInOutputFolder_FileIsThere_IsTrue()
    {
        File.WriteAllText(Path.Combine(_folder, "Залік Додаток 8 06.08.2026.xlsx"), "x");

        Assert.True(GenerationService.ExistsInOutputFolder(_folder, "Залік Додаток 8 06.08.2026.xlsx"));
    }

    // Ключовий випадок: в архіві запис є, у теці файлу нема - генерувати треба.
    [Fact]
    public void ExistsInOutputFolder_ArchivedButMissingFromFolder_IsFalse()
        => Assert.False(GenerationService.ExistsInOutputFolder(_folder, "Залік Додаток 8 06.08.2026.xlsx"));

    [Fact]
    public void ExistsInOutputFolder_EmptyOrMissingName_IsFalse()
    {
        Assert.False(GenerationService.ExistsInOutputFolder(_folder, null));
        Assert.False(GenerationService.ExistsInOutputFolder(_folder, string.Empty));
        Assert.False(GenerationService.ExistsInOutputFolder(_folder, "   "));
    }

    // Інша тека - той самий сенс: файлу тут нема, отже не пропускаємо.
    [Fact]
    public void ExistsInOutputFolder_FileInDifferentFolder_IsFalse()
    {
        var other = Path.Combine(Path.GetTempPath(), "gendoc-skip-other-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other);
        try
        {
            File.WriteAllText(Path.Combine(other, "Звіт.xlsx"), "x");
            Assert.False(GenerationService.ExistsInOutputFolder(_folder, "Звіт.xlsx"));
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }
}
