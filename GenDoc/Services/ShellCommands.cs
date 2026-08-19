namespace GenDoc.Services
{
    public static class ShellCommands
    {
        // explorer.exe /select,"шлях" - відкриває теку з виділеним файлом.
        public static string ExplorerSelectArguments(string path) => $"/select,\"{path}\"";
    }
}
