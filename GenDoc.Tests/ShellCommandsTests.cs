using GenDoc.Services;

namespace GenDoc.Tests;

public class ShellCommandsTests
{
    [Fact]
    public void ExplorerSelectArguments_QuotesPath()
        => Assert.Equal(@"/select,""D:\Док\Набір 4\файл.docx""", ShellCommands.ExplorerSelectArguments(@"D:\Док\Набір 4\файл.docx"));
}
