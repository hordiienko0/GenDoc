namespace GenDoc.Data
{
    public static class SqlCipherBootstrapper
    {
        private static bool _initialized;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlcipher());
            _initialized = true;
        }
    }
}
