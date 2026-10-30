using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

public class OutputFolderResolverTests
{
    private static readonly string[] OneDrive = { @"C:\Users\x\OneDrive" };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_Empty_FallsBackToDocumentsNextToDatabase(string? configured)
        => Assert.Equal(Path.Combine(@"D:\ДокОблік", "Документи"), OutputFolderResolver.Resolve(configured, @"D:\ДокОблік", OneDrive));

    [Fact]
    public void Resolve_Configured_ReturnsItTrimmed()
        => Assert.Equal(@"D:\Док", OutputFolderResolver.Resolve(@"  D:\Док  ", @"D:\ДокОблік", OneDrive));

    [Theory]
    [InlineData(@"C:\Users\x\OneDrive\Рабочий стол\ДокОблік")]
    [InlineData(@"C:\Users\x\OneDrive")]
    [InlineData(@"c:\users\x\onedrive\Documents\GenDoc\")]
    public void Resolve_ConfiguredInOneDrive_IsIgnored(string configured)
        => Assert.Equal(Path.Combine(@"D:\ДокОблік", "Документи"), OutputFolderResolver.Resolve(configured, @"D:\ДокОблік", OneDrive));
}
