using GenDoc.Services.Completeness;

namespace GenDoc.Tests.Completeness;

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

    [Fact]
    public void Build_SingleLongName_IsLeftWholeForTheViewToTrim()
    {
        const string name = "Роздавально-здавальна відомість майна";

        var labels = MatrixColumnLabels.Build(new[] { name });

        Assert.Equal(name, labels[0]);
    }

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

    [Fact]
    public void Build_HandlesEmptyInput()
    {
        Assert.Empty(MatrixColumnLabels.Build(Array.Empty<string>()));
    }
}
