using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

public class PackageFolderOriginDisplayTests
{
    [Fact]
    public void ShortDisplay_FolderDescription_ShowsOnlyTheFolderName()
    {
        var description = PackageFolderOrigin.Describe(@"D:\GenDoc\шаблони\комплекти\Відрядження");

        Assert.Equal("Зібрано з теки «Відрядження»", PackageFolderOrigin.ShortDisplay(description));
    }

    [Fact]
    public void ShortDisplay_TrailingSlash_IsIgnored()
    {
        var description = PackageFolderOrigin.Describe(@"D:\комплекти\Стрільби\");

        Assert.Equal("Зібрано з теки «Стрільби»", PackageFolderOrigin.ShortDisplay(description));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Власний опис пакета")]
    [InlineData("Зібрано з теки «Відрядження»")]
    public void ShortDisplay_OtherDescriptions_StayAsIs(string? description)
    {
        Assert.Equal(description, PackageFolderOrigin.ShortDisplay(description));
    }
}
