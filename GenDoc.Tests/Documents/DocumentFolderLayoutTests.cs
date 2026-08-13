using GenDoc.Services.Documents;

namespace GenDoc.Tests.Documents;

/// <summary>Розкладка документів по папках. Ця функція годує і генерацію на
/// диск, і дерево в «Архіві документів», тож розходження тут означає, що на
/// екрані показано не те, що лежить у теці.</summary>
public class DocumentFolderLayoutTests
{
    [Fact]
    public void Person_document_goes_into_intake_then_type_then_person()
    {
        var placement = DocumentFolderLayout.ForPerson(
            "Набір №15", "Акт приймання-передавання зброї", "КОВАЛЬЧУК В.Б.");

        Assert.Equal(new[] { "Набір №15", "Акт приймання-передавання зброї" }, placement.Folders);
        Assert.Equal("КОВАЛЬЧУК В.Б.", placement.FileName);
        Assert.Equal(
            Path.Combine("Набір №15", "Акт приймання-передавання зброї", "КОВАЛЬЧУК В.Б..docx"),
            placement.RelativePath(".docx"));
    }

    // Груповий документ — на весь список, а не на людину, тож рівень «особа»
    // заміняє дата.
    [Fact]
    public void Group_document_uses_the_date_instead_of_a_person()
    {
        var placement = DocumentFolderLayout.ForGroup(
            "Набір №15", "Відомість видачі майна", new DateTime(2026, 8, 13));

        Assert.Equal(new[] { "Набір №15", "Відомість видачі майна" }, placement.Folders);
        Assert.Equal("2026-08-13", placement.FileName);
    }

    // Дата саме в такому вигляді, щоб теки сортувалися хронологічно самі собою.
    [Fact]
    public void Group_date_sorts_chronologically_as_text()
    {
        var january = DocumentFolderLayout.ForGroup("Н", "Т", new DateTime(2026, 1, 5)).FileName;
        var october = DocumentFolderLayout.ForGroup("Н", "Т", new DateTime(2026, 10, 5)).FileName;

        Assert.True(string.CompareOrdinal(january, october) < 0);
    }

    // Постійний склад не належить жодному наборові; без власної папки його
    // документи лягали б у корінь упереміш із папками наборів.
    [Fact]
    public void People_outside_an_intake_get_their_own_top_level_folder()
    {
        var placement = DocumentFolderLayout.ForPerson(null, "Довідка", "ТКАЧУК В.І.");

        Assert.Equal(DocumentFolderLayout.PermanentStaffFolder, placement.Folders[0]);
    }

    [Theory]
    [InlineData("Акт / приймання", "Акт приймання")]
    [InlineData("Звіт: підсумки", "Звіт підсумки")]
    [InlineData("Наказ \"про склад\"", "Наказ про склад")]
    [InlineData("Акт*?<>|", "Акт")]
    public void Characters_windows_forbids_are_stripped_from_folder_names(string raw, string expected)
    {
        var placement = DocumentFolderLayout.ForPerson("Набір №1", raw, "ПІБ");

        Assert.Equal(expected, placement.Folders[1]);
    }

    // Windows мовчки відкидає крапку й пробіл у кінці імені, тож «Акт.» і «Акт»
    // стали б однією текою — прибираємо їх самі, щоб це було видно в коді.
    [Fact]
    public void Trailing_dots_and_spaces_are_removed()
    {
        Assert.Equal("Акт", DocumentFolderLayout.Sanitize("Акт.  "));
        Assert.Equal("Акт", DocumentFolderLayout.Sanitize("  Акт "));
    }

    [Fact]
    public void Empty_name_falls_back_instead_of_producing_an_empty_folder()
    {
        var placement = DocumentFolderLayout.ForPerson("Набір №1", "   ", "ПІБ");

        Assert.False(string.IsNullOrWhiteSpace(placement.Folders[1]));
    }

    // Двоє однофамільців з однаковими ініціалами в одному наборі інакше дали б
    // той самий шлях, і другий файл тихо затер би перший.
    [Fact]
    public void Service_number_separates_people_with_the_same_short_name()
    {
        var first = DocumentFolderLayout.ForPerson("Набір №1", "Акт", "КОВАЛЬЧУК В.Б.", "ХН-001");
        var second = DocumentFolderLayout.ForPerson("Набір №1", "Акт", "КОВАЛЬЧУК В.Б.", "ХН-002");

        Assert.NotEqual(first.FileName, second.FileName);
        Assert.Contains("ХН-001", first.FileName);
    }

    [Fact]
    public void Without_a_service_number_the_name_stays_clean()
    {
        var placement = DocumentFolderLayout.ForPerson("Набір №1", "Акт", "КОВАЛЬЧУК В.Б.");

        Assert.Equal("КОВАЛЬЧУК В.Б.", placement.FileName);
    }
}
