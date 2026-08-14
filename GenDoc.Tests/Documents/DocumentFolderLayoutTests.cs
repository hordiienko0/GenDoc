using GenDoc.Services.Documents;

namespace GenDoc.Tests.Documents;

/// <summary>Розкладка документів по папках. Ця функція годує і генерацію на
/// диск, і дерево в «Архіві документів», тож розходження тут означає, що на
/// екрані показано не те, що лежить у теці.</summary>
public class DocumentFolderLayoutTests
{
    private const string Stamp = "2026-08-14";

    [Fact]
    public void Person_document_goes_into_intake_then_type_then_run_then_person()
    {
        var placement = DocumentFolderLayout.ForPerson(
            "Набір №15", "Акт приймання-передавання зброї", Stamp, "КОВАЛЬЧУК В.Б.");

        Assert.Equal(
            new[] { "Набір №15", "Акт приймання-передавання зброї", Stamp },
            placement.Folders);
        Assert.Equal("КОВАЛЬЧУК В.Б.", placement.FileName);
    }

    // Груповий документ — на весь список, а не на людину, тож рівень «особа»
    // заміняє позначка прогону, і вона ж стає іменем файлу.
    [Fact]
    public void Group_document_uses_the_run_stamp_as_its_file_name()
    {
        var placement = DocumentFolderLayout.ForGroup(
            new[] { "Набір №15" }, "Відомість видачі майна", Stamp);

        Assert.Equal(new[] { "Набір №15", "Відомість видачі майна" }, placement.Folders);
        Assert.Equal(Stamp, placement.FileName);
    }

    // ─── Позначка прогону ────────────────────────────────────────────────────

    [Fact]
    public void First_run_of_the_day_gets_a_plain_date()
    {
        var stamp = DocumentFolderLayout.RunStamp(
            new DateTime(2026, 8, 14, 14, 30, 0), dateFolderAlreadyUsed: false);

        Assert.Equal("2026-08-14", stamp);
    }

    // Другий прогін за той самий день без часу затер би перший — саме від цього
    // рівень дати й мав захистити.
    [Fact]
    public void Second_run_of_the_same_day_gets_the_time_too()
    {
        var stamp = DocumentFolderLayout.RunStamp(
            new DateTime(2026, 8, 14, 14, 30, 0), dateFolderAlreadyUsed: true);

        Assert.Equal("2026-08-14 14-30", stamp);
        Assert.DoesNotContain(":", stamp);
    }

    [Fact]
    public void Run_stamps_sort_chronologically_as_text()
    {
        var january = DocumentFolderLayout.RunStamp(new DateTime(2026, 1, 5), false);
        var october = DocumentFolderLayout.RunStamp(new DateTime(2026, 10, 5), false);

        Assert.True(string.CompareOrdinal(january, october) < 0);
    }

    // ─── Набір ───────────────────────────────────────────────────────────────

    [Fact]
    public void People_outside_an_intake_get_their_own_top_level_folder()
    {
        var placement = DocumentFolderLayout.ForPerson(null, "Довідка", Stamp, "ТКАЧУК В.І.");

        Assert.Equal(DocumentFolderLayout.PermanentStaffFolder, placement.Folders[0]);
    }

    [Fact]
    public void Group_takes_the_intake_of_its_members_when_they_all_share_one()
    {
        var placement = DocumentFolderLayout.ForGroup(
            new[] { "Набір №15", "Набір №15" }, "Відомість", Stamp);

        Assert.Equal("Набір №15", placement.Folders[0]);
    }

    // Покласти змішану відомість в один із наборів означало б збрехати про її
    // склад, тому для неї окрема папка.
    [Fact]
    public void Group_of_mixed_intakes_goes_to_the_shared_folder()
    {
        var placement = DocumentFolderLayout.ForGroup(
            new[] { "Набір №15", "Набір №16" }, "Відомість", Stamp);

        Assert.Equal(DocumentFolderLayout.SharedFolder, placement.Folders[0]);
    }

    // Один із людей поза набором — це вже не «весь набір», отже теж «Спільні».
    [Fact]
    public void One_member_outside_the_intake_makes_the_group_shared()
    {
        var placement = DocumentFolderLayout.ForGroup(
            new[] { "Набір №15", null }, "Відомість", Stamp);

        Assert.Equal(DocumentFolderLayout.SharedFolder, placement.Folders[0]);
    }

    [Fact]
    public void Empty_group_goes_to_the_shared_folder()
    {
        var placement = DocumentFolderLayout.ForGroup(Array.Empty<string?>(), "Відомість", Stamp);

        Assert.Equal(DocumentFolderLayout.SharedFolder, placement.Folders[0]);
    }

    // ─── Назви ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Акт / приймання", "Акт приймання")]
    [InlineData("Звіт: підсумки", "Звіт підсумки")]
    [InlineData("Наказ \"про склад\"", "Наказ про склад")]
    [InlineData("Акт*?<>|", "Акт")]
    public void Characters_windows_forbids_are_stripped_from_folder_names(string raw, string expected)
    {
        var placement = DocumentFolderLayout.ForPerson("Набір №1", raw, Stamp, "ПІБ");

        Assert.Equal(expected, placement.Folders[1]);
    }

    // Windows мовчки відкидає крапку й пробіл у кінці імені, тож «Акт.» і «Акт»
    // стали б однією текою — прибираємо їх самі, щоб це було видно в коді.
    [Fact]
    public void Trailing_dots_and_spaces_are_removed_from_folders()
    {
        Assert.Equal("Акт", DocumentFolderLayout.Sanitize("Акт.  "));
        Assert.Equal("Акт", DocumentFolderLayout.Sanitize("  Акт "));
    }

    // А в імені файлу кінцева крапка лишається: у «КОВАЛЬЧУК В.Б.» це ініціал.
    [Fact]
    public void Trailing_dot_survives_in_a_file_name()
    {
        var placement = DocumentFolderLayout.ForPerson("Набір №1", "Акт", Stamp, "КОВАЛЬЧУК В.Б.");

        Assert.Equal("КОВАЛЬЧУК В.Б.", placement.FileName);
    }

    [Fact]
    public void Empty_name_falls_back_instead_of_producing_an_empty_folder()
    {
        var placement = DocumentFolderLayout.ForPerson("Набір №1", "   ", Stamp, "ПІБ");

        Assert.False(string.IsNullOrWhiteSpace(placement.Folders[1]));
    }

    // Двоє однофамільців з однаковими ініціалами в одному наборі інакше дали б
    // той самий шлях, і другий файл тихо затер би перший.
    [Fact]
    public void Service_number_separates_people_with_the_same_short_name()
    {
        var first = DocumentFolderLayout.ForPerson("Набір №1", "Акт", Stamp, "КОВАЛЬЧУК В.Б.", "ХН-001");
        var second = DocumentFolderLayout.ForPerson("Набір №1", "Акт", Stamp, "КОВАЛЬЧУК В.Б.", "ХН-002");

        Assert.NotEqual(first.FileName, second.FileName);
        Assert.Contains("ХН-001", first.FileName);
    }

    [Fact]
    public void Relative_path_joins_folders_and_extension()
    {
        var placement = DocumentFolderLayout.ForPerson("Набір №1", "Акт", Stamp, "ПІБ");

        Assert.Equal(
            Path.Combine("Набір №1", "Акт", Stamp, "ПІБ.docx"),
            placement.RelativePath(".docx"));
    }
}
