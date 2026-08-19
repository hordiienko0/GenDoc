using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

// Набір прогону виводиться з людей, яким реально генерували: найчастіший
// не-null; усі null (лише постійний склад) - null; нічия - менший Id.
public class RunIntakeResolverTests
{
    [Fact]
    public void Resolve_AllNull_ReturnsNull()
        => Assert.Null(RunIntakeResolver.Resolve(new int?[] { null, null }));

    [Fact]
    public void Resolve_Empty_ReturnsNull()
        => Assert.Null(RunIntakeResolver.Resolve(Array.Empty<int?>()));

    [Fact]
    public void Resolve_MostFrequentWins()
        => Assert.Equal(4, RunIntakeResolver.Resolve(new int?[] { 4, 4, 5, null }));

    [Fact]
    public void Resolve_Tie_PicksSmallerId()
        => Assert.Equal(3, RunIntakeResolver.Resolve(new int?[] { 7, 3 }));
}
