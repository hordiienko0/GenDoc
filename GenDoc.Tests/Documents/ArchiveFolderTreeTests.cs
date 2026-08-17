using GenDoc.Services.Documents;

namespace GenDoc.Tests.Documents;

// Дерево в «Архіві» будується РОЗБОРОМ збереженого відносного шляху, а не
// повторним обчисленням із полів. Той шлях уже є результатом
// DocumentFolderLayout — саме його записали на диск. Читаючи його, екран
// фізично не може розійтися з тим, що лежить у папках; друге обчислення з
// часом розійшлося б, як це вже двічі ставалося в цьому проєкті.
public class ArchiveFolderTreeTests
{
    [Fact]
    public void Build_NestsFoldersAndCountsDocumentsInEachLeaf()
    {
        var tree = ArchiveFolderTree.Build(new[]
        {
            @"Набір №15\Акти приймання-передавання зброї\2026-08-17\КОВАЛЬЧУК В. Б..docx",
            @"Набір №15\Акти приймання-передавання зброї\2026-08-17\ТКАЧЕНКО О. Ю..docx"
        });

        var intake = Assert.Single(tree);
        Assert.Equal("Набір №15", intake.Name);
        Assert.Equal(2, intake.DocumentCount);

        var type = Assert.Single(intake.Children);
        Assert.Equal("Акти приймання-передавання зброї", type.Name);

        var run = Assert.Single(type.Children);
        Assert.Equal("2026-08-17", run.Name);
        Assert.Equal(2, run.DocumentCount);
        Assert.Empty(run.Children);
    }

    // Лічильник вузла — це всі документи ПІД ним, а не лише в ньому самому.
    // Інакше згорнута гілка показувала б нуль і виглядала б порожньою.
    [Fact]
    public void Build_CountsRollUpThroughEveryLevel()
    {
        var tree = ArchiveFolderTree.Build(new[]
        {
            @"Набір №15\Акти\2026-08-17\ОДИН.docx",
            @"Набір №15\Довідки\2026-08-15\ДВА.docx",
            @"Набір №15\Довідки\2026-08-15\ТРИ.docx"
        });

        var intake = Assert.Single(tree);
        Assert.Equal(3, intake.DocumentCount);

        var acts = intake.Children.Single(c => c.Name == "Акти");
        var notes = intake.Children.Single(c => c.Name == "Довідки");
        Assert.Equal(1, acts.DocumentCount);
        Assert.Equal(2, notes.DocumentCount);
    }

    // Записи, зроблені до переходу на папки, тримають саме лише ім'я файлу.
    // Вигадувати їм гілку заднім числом означало б показати те, чого на диску
    // немає, — тож вони збираються в окремому чесно названому вузлі.
    [Fact]
    public void Build_PutsPathlessLegacyRecordsInTheirOwnNode()
    {
        var tree = ArchiveFolderTree.Build(new[]
        {
            @"Набір №15\Акти\2026-08-17\НОВИЙ.docx",
            "Старий документ.docx"
        });

        var legacy = tree.Single(n => n.Name == ArchiveFolderTree.UnsortedFolder);
        Assert.Equal(1, legacy.DocumentCount);
        Assert.Empty(legacy.Children);
    }

    // Шлях вузла — це префікс, за яким список фільтрується. Він мусить бути
    // повним від кореня, інакше дві однойменні гілки в різних наборах
    // («Довідки») показували б документи одна одної.
    [Fact]
    public void Build_GivesEachNodeItsFullPathFromTheRoot()
    {
        var tree = ArchiveFolderTree.Build(new[]
        {
            @"Набір №15\Довідки\2026-08-15\ОДИН.docx",
            @"Набір №14\Довідки\2026-08-15\ДВА.docx"
        });

        var fifteen = tree.Single(n => n.Name == "Набір №15");
        var notes = Assert.Single(fifteen.Children);

        Assert.Equal("Набір №15", fifteen.Path);
        Assert.Equal(@"Набір №15\Довідки", notes.Path);
    }
}
