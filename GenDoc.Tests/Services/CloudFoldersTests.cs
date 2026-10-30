using GenDoc.Services;

namespace GenDoc.Tests.Services;

public class CloudFoldersTests
{
    private static readonly string[] Roots = { @"C:\Users\x\OneDrive", @"C:\Users\x\OneDrive - Курси" };

    [Theory]
    [InlineData(@"C:\Users\x\OneDrive")]
    [InlineData(@"C:\Users\x\OneDrive\")]
    [InlineData(@"C:\Users\x\OneDrive\Рабочий стол\ДокОблік\gendoc.db")]
    [InlineData(@"c:\USERS\x\onedrive\Documents")]
    [InlineData(@"C:\Users\x\OneDrive - Курси\Спільні")]
    public void InsideOneDrive(string path) => Assert.True(CloudFolders.IsInOneDrive(path, Roots));

    [Theory]
    [InlineData(@"D:\ДокОблік")]
    [InlineData(@"C:\Users\x\OneDriveBackup\Док")]
    [InlineData(@"C:\Users\x\Desktop")]
    [InlineData("")]
    [InlineData(null)]
    public void OutsideOneDrive(string? path) => Assert.False(CloudFolders.IsInOneDrive(path, Roots));

    [Fact]
    public void NoOneDriveInstalled_NothingIsInIt()
        => Assert.False(CloudFolders.IsInOneDrive(@"C:\Users\x\OneDrive\Док", Array.Empty<string>()));
}
