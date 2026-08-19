using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

public class OutputFolderResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_Empty_FallsBackToDocumentsGenDoc(string? configured)
        => Assert.Equal(Path.Combine(@"C:\Users\x\Documents", "GenDoc"), OutputFolderResolver.Resolve(configured, @"C:\Users\x\Documents"));

    [Fact]
    public void Resolve_Configured_ReturnsItTrimmed()
        => Assert.Equal(@"D:\Док", OutputFolderResolver.Resolve(@"  D:\Док  ", @"C:\Users\x\Documents"));
}
