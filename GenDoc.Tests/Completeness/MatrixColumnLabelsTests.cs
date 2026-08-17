using GenDoc.Services.Completeness;

namespace GenDoc.Tests.Completeness;

// Заголовки колонок матриці комплектності обрізалися з ПОЧАТКУ назви, тож
// «Рапорт котлове ІНДИВІДУАЛЬНИЙ» і «Рапорт котлове ГРУПОВИЙ (3)» ставали
// однаковим «Рапорт котло…». На екрані дві різні колонки були нерозрізненні,
// а це різні документи з різною поведінкою.
//
// Правило: коли назви збігаються на початку, показуємо ту частину, якою вони
// РІЗНЯТЬСЯ. Повна назва лишається в підказці, тож нічого не втрачається.
public class MatrixColumnLabelsTests
{
    [Fact]
    public void Build_NamesSharingAPrefix_AreLabelledByWhatDiffers()
    {
        var labels = MatrixColumnLabels.Build(new[]
        {
            "Рапорт котлове ІНДИВІДУАЛЬНИЙ",
            "Рапорт котлове ГРУПОВИЙ (3)"
        });

        Assert.NotEqual(labels[0], labels[1]);
        Assert.StartsWith("ІНДИВІДУАЛЬН", labels[0], StringComparison.Ordinal);
        Assert.StartsWith("ГРУПОВИЙ", labels[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Build_UnrelatedNames_KeepTheirOwnBeginning()
    {
        var labels = MatrixColumnLabels.Build(new[]
        {
            "Допуск Додаток 5",
            "Залік Додаток 8"
        });

        Assert.Equal("Допуск Додаток 5", labels[0]);
        Assert.Equal("Залік Додаток 8", labels[1]);
    }

    // Довга назва без сусідів-двійників віддається ЦІЛОЮ: багатокрапку
    // домалює TextTrimming заголовка. Різати ще й у коді означало б дві
    // багатокрапки поспіль.
    [Fact]
    public void Build_SingleLongName_IsLeftWholeForTheViewToTrim()
    {
        const string name = "Роздавально-здавальна відомість майна";

        var labels = MatrixColumnLabels.Build(new[] { name });

        Assert.Equal(name, labels[0]);
    }

    // Три назви зі спільним початком мусять лишитись розрізненними всі три.
    [Fact]
    public void Build_ThreeWayCollision_StaysDistinct()
    {
        var labels = MatrixColumnLabels.Build(new[]
        {
            "Акт приймання зброї автомат",
            "Акт приймання зброї пістолет",
            "Акт приймання зброї кулемет"
        });

        Assert.Equal(3, labels.Distinct(StringComparer.Ordinal).Count());
    }

    // Спільний початок відрізається ПО МЕЖІ СЛОВА: «Рапорт котлове Г…» краще
    // за «ове ГРУПОВИЙ», інакше підпис починається з уламка слова.
    [Fact]
    public void Build_CutsTheSharedPartOnAWordBoundary()
    {
        var labels = MatrixColumnLabels.Build(new[]
        {
            "Відомість видачі майна",
            "Відомість видачі пального"
        });

        Assert.Equal("майна", labels[0]);
        Assert.Equal("пального", labels[1]);
    }

    // Порожній перелік і порожні назви не мають валити побудову колонок.
    [Fact]
    public void Build_HandlesEmptyInput()
    {
        Assert.Empty(MatrixColumnLabels.Build(Array.Empty<string>()));
    }
}
