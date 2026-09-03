using System.Data.Common;
using System.Text.RegularExpressions;
using GenDoc.Services;
using Microsoft.Data.Sqlite;

namespace GenDoc.Tests;

public class DatabaseSchemaInitializerTests
{
    [Fact]
    public void MigrateGeneratedGroupDocumentsForDocxSupport_PreservesDataAndChildContent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        CreateOldSchema(connection);
        InsertOldRow(connection, id: 1, exportTemplateId: 7);

        DatabaseSchemaInitializer.MigrateGeneratedGroupDocumentsForDocxSupport((DbConnection)connection);

        var columns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "GeneratedGroupDocuments");
        Assert.Contains("TemplateId", columns);
        Assert.Contains("ExportTemplateId", columns);

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """SELECT "Id", "ExportTemplateId", "TemplateId", "FileName" FROM "GeneratedGroupDocuments";""";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(7L, reader.GetInt64(1));
            Assert.True(reader.IsDBNull(2));
            Assert.Equal("test.xlsx", reader.GetString(3));
            Assert.False(reader.Read());
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """SELECT COUNT(*) FROM "GeneratedGroupDocumentContents" WHERE "GeneratedGroupDocumentId" = 1;""";
            var count = (long)cmd.ExecuteScalar()!;
            Assert.Equal(1, count);
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO "GeneratedGroupDocuments"
                    ("ExportTemplateId", "TemplateId", "GeneratedAt", "GeneratedByUserId", "FileName")
                VALUES (NULL, 42, '2026-01-01', 1, 'group.docx');
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """SELECT COUNT(*) FROM "GeneratedGroupDocuments" WHERE "TemplateId" = 42;""";
            var count = (long)cmd.ExecuteScalar()!;
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public void MigrateGeneratedGroupDocumentsForDocxSupport_AlreadyMigrated_IsNoOp()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        CreateOldSchema(connection);
        InsertOldRow(connection, id: 1, exportTemplateId: 7);

        DatabaseSchemaInitializer.MigrateGeneratedGroupDocumentsForDocxSupport((DbConnection)connection);
        DatabaseSchemaInitializer.MigrateGeneratedGroupDocumentsForDocxSupport((DbConnection)connection);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """SELECT COUNT(*) FROM "GeneratedGroupDocuments";""";
        var count = (long)cmd.ExecuteScalar()!;
        Assert.Equal(1, count);
    }

    [Fact]
    public void EnsureWeaponVehicleTables_ExistingTableWithoutRawText_AddsMissingColumns()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE "Weapons" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Weapons" PRIMARY KEY AUTOINCREMENT,
                    "RecipientId" INTEGER NOT NULL,
                    "Name" TEXT NOT NULL,
                    "SerialNumber" TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO "Weapons" ("Id", "RecipientId", "Name", "SerialNumber")
                VALUES (1, 5, 'АКС-74', '903530');
                """;
            cmd.ExecuteNonQuery();
        }

        DatabaseSchemaInitializer.EnsureWeaponVehicleTables((DbConnection)connection);

        var columns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "Weapons");
        Assert.Contains("RawText", columns);
        Assert.Contains("IssuedAt", columns);
        Assert.Contains("Note", columns);
        Assert.Contains("DeletedAt", columns);
        Assert.Contains("DeletedBy", columns);

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """SELECT "Name", "SerialNumber", "RawText" FROM "Weapons" WHERE "Id" = 1;""";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("АКС-74", reader.GetString(0));
            Assert.Equal("903530", reader.GetString(1));
            Assert.True(reader.IsDBNull(2));
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO "Weapons" ("RecipientId", "Name", "SerialNumber", "RawText")
                VALUES (5, 'ПМ', '1234', 'ПМ №1234');
                """;
            cmd.ExecuteNonQuery();
        }
    }

    [Fact]
    public void EnsureWeaponVehicleTables_FreshDatabase_CreatesTablesAndIsIdempotent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        DatabaseSchemaInitializer.EnsureWeaponVehicleTables((DbConnection)connection);
        DatabaseSchemaInitializer.EnsureWeaponVehicleTables((DbConnection)connection);

        var weaponColumns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "Weapons");
        Assert.Contains("RawText", weaponColumns);

        var vehicleColumns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "Vehicles");
        Assert.Contains("Model", vehicleColumns);
        Assert.Contains("PlateNumber", vehicleColumns);

        Assert.Equal(weaponColumns.Count, DatabaseSchemaInitializer
            .GetExistingColumns((DbConnection)connection, "Weapons").Count);
    }

    [Fact]
    public void TemplateColumnsV23_AddsAudienceAndKeepsExistingTemplatesInIntake()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE "Templates" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Templates" PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL,
                    "OriginalFileName" TEXT NOT NULL,
                    "Content" BLOB NOT NULL,
                    "UploadedAt" TEXT NOT NULL
                );
                INSERT INTO "Templates" ("Id", "Name", "OriginalFileName", "Content", "UploadedAt")
                VALUES (1, 'Рапорт', 'raport.docx', x'0102', '2026-01-01');
                """;
            cmd.ExecuteNonQuery();
        }

        DatabaseSchemaInitializer.AddMissingColumns(
            (DbConnection)connection, "Templates", DatabaseSchemaInitializer.TemplateColumnsV23);
        DatabaseSchemaInitializer.AddMissingColumns(
            (DbConnection)connection, "Templates", DatabaseSchemaInitializer.TemplateColumnsV23);

        var columns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "Templates");
        Assert.Contains("Audience", columns);

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """SELECT "Audience" FROM "Templates" WHERE "Id" = 1;""";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(0, reader.GetInt32(0));
        }
    }

    [Fact]
    public void TemplateColumnsV21_AddsBuilderJsonToExistingTemplates()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE "Templates" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Templates" PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL,
                    "OriginalFileName" TEXT NOT NULL,
                    "Content" BLOB NOT NULL,
                    "UploadedAt" TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO "Templates" ("Id", "Name", "OriginalFileName", "Content", "UploadedAt")
                VALUES (1, 'Залік Додаток 8', 'zalik.docx', x'0102', '2026-01-01');
                """;
            cmd.ExecuteNonQuery();
        }

        DatabaseSchemaInitializer.AddMissingColumns(
            (DbConnection)connection, "Templates", DatabaseSchemaInitializer.TemplateColumnsV21);
        DatabaseSchemaInitializer.AddMissingColumns(
            (DbConnection)connection, "Templates", DatabaseSchemaInitializer.TemplateColumnsV21);

        var columns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "Templates");
        Assert.Contains("BuilderJson", columns);

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """SELECT "Name", "BuilderJson" FROM "Templates" WHERE "Id" = 1;""";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("Залік Додаток 8", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO "Templates" ("Name", "OriginalFileName", "Content", "UploadedAt", "BuilderJson")
                VALUES ('Зібраний', 'zibranyi.docx', x'0304', '2026-01-02', '{"Blocks":[],"Version":1}');
                """;
            cmd.ExecuteNonQuery();
        }
    }

    [Fact]
    public void ExportTemplateColumnsV22_AddsBuilderJsonToExistingExportTemplates()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE "ExportTemplates" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ExportTemplates" PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL,
                    "OriginalFileName" TEXT NOT NULL,
                    "Content" BLOB NOT NULL,
                    "IsBuiltIn" INTEGER NOT NULL,
                    "UploadedAt" TEXT NOT NULL,
                    "TemplateRowIndex" INTEGER NOT NULL DEFAULT 2,
                    "UsesPlaceholders" INTEGER NOT NULL DEFAULT 0
                );
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO "ExportTemplates" ("Id", "Name", "OriginalFileName", "Content", "IsBuiltIn", "UploadedAt")
                VALUES (1, 'Допуск Додаток 5', 'dopusk.xlsx', x'0102', 0, '2026-01-01');
                """;
            cmd.ExecuteNonQuery();
        }

        DatabaseSchemaInitializer.AddMissingColumns(
            (DbConnection)connection, "ExportTemplates", DatabaseSchemaInitializer.ExportTemplateColumnsV22);
        DatabaseSchemaInitializer.AddMissingColumns(
            (DbConnection)connection, "ExportTemplates", DatabaseSchemaInitializer.ExportTemplateColumnsV22);

        Assert.Contains("BuilderJson",
            DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "ExportTemplates"));

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """SELECT "Name", "BuilderJson" FROM "ExportTemplates" WHERE "Id" = 1;""";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("Допуск Додаток 5", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
        }
    }

    [Fact]
    public void EnsureUserSettingsTable_FreshDatabase_CreatesTableAndIsIdempotent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Exec(connection, """
            CREATE TABLE "Users" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT, "FullName" TEXT NOT NULL);
            INSERT INTO "Users" ("Id", "FullName") VALUES (1, 'Тест');
            """);

        DatabaseSchemaInitializer.EnsureUserSettingsTable((DbConnection)connection);
        DatabaseSchemaInitializer.EnsureUserSettingsTable((DbConnection)connection);

        var columns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "UserSettings");
        Assert.Contains("UserProfileId", columns);
        Assert.Contains("ActiveIntakeId", columns);
        Assert.Contains("LastPackageId", columns);
        Assert.Contains("ArchiveMineOnly", columns);
        Assert.Contains("LastManualValuesJson", columns);
        Assert.Contains("LastSignerByTemplateJson", columns);

        Exec(connection, """INSERT INTO "UserSettings" ("UserProfileId") VALUES (1);""");
        Assert.Equal(0L, Scalar(connection, """SELECT "ArchiveMineOnly" FROM "UserSettings" WHERE "UserProfileId" = 1"""));

        Assert.Throws<SqliteException>(() =>
            Exec(connection, """INSERT INTO "UserSettings" ("UserProfileId") VALUES (1);"""));
    }

    [Fact]
    public void EnsureUserSettingsTable_ExistingTableMissingColumns_AddsMissingColumns()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Exec(connection, """
            CREATE TABLE "Users" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT, "FullName" TEXT NOT NULL);
            INSERT INTO "Users" ("Id", "FullName") VALUES (1, 'Тест');
            CREATE TABLE "UserSettings" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_UserSettings" PRIMARY KEY AUTOINCREMENT,
                "UserProfileId" INTEGER NOT NULL
            );
            INSERT INTO "UserSettings" ("Id", "UserProfileId") VALUES (1, 1);
            """);

        DatabaseSchemaInitializer.EnsureUserSettingsTable((DbConnection)connection);

        var columns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "UserSettings");
        Assert.Contains("ArchiveMineOnly", columns);
        Assert.Contains("LastManualValuesJson", columns);
        Assert.Contains("LastSignerByTemplateJson", columns);
        Assert.Contains("ActiveIntakeId", columns);
        Assert.Contains("LastPackageId", columns);

        Assert.Equal(0L, Scalar(connection, """SELECT "ArchiveMineOnly" FROM "UserSettings" WHERE "Id" = 1"""));
    }

    [Fact]
    public void EnsureUserSettingsTable_ExistingTableWithoutIndex_GetsUniqueIndex()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Exec(connection, """
            CREATE TABLE "Users" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT, "FullName" TEXT NOT NULL);
            INSERT INTO "Users" ("Id", "FullName") VALUES (1, 'Тест');
            CREATE TABLE "UserSettings" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_UserSettings" PRIMARY KEY AUTOINCREMENT,
                "UserProfileId" INTEGER NOT NULL
            );
            """);

        DatabaseSchemaInitializer.EnsureUserSettingsTable((DbConnection)connection);

        Exec(connection, """INSERT INTO "UserSettings" ("UserProfileId") VALUES (1);""");
        Assert.Throws<SqliteException>(() =>
            Exec(connection, """INSERT INTO "UserSettings" ("UserProfileId") VALUES (1);"""));
    }

    [Theory]
    [InlineData("Recipients", "Nationality")]
    [InlineData("OrganizationSettings", "HrOfficerFullName")]
    [InlineData("Rooms", "Note")]
    [InlineData("Rooms", "CreatedAt")]
    public void IdempotentTail_CoversColumnsFromEveryVersion(string table, string column)
    {
        var source = File.ReadAllText(SchemaInitializerSourcePath());
        var tailStart = source.IndexOf("        EnsureOrgTables(db);", StringComparison.Ordinal);
        Assert.True(tailStart > 0, "Не знайдено ідемпотентний хвіст EnsureInitialized");

        var tail = source[tailStart..];
        var arrays = Regex.Matches(tail, @"AddMissingColumns\(db, ""(?<table>\w+)"", (?<array>\w+)\)")
            .Where(m => m.Groups["table"].Value == table)
            .Select(m => m.Groups["array"].Value)
            .ToList();

        Assert.True(arrays.Count > 0, $"У хвості немає жодного AddMissingColumns для «{table}»");

        var covered = arrays.Any(array =>
        {
            var declaration = Regex.Match(source, @$"{array}\s*=\s*(?:new\[\]\s*)?\{{(?<body>[^}}]*)\}}",
                RegexOptions.Singleline);
            return declaration.Success && declaration.Groups["body"].Value.Contains($"\"{column}\"", StringComparison.Ordinal);
        });

        Assert.True(covered,
            $"Колонка «{column}» таблиці «{table}» не покрита жодним AddMissingColumns в ідемпотентному хвості "
            + $"(перевірені масиви: {string.Join(", ", arrays)})");
    }

    private static string SchemaInitializerSourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "GenDoc", "Services")))
            dir = dir.Parent;
        if (dir is null) throw new DirectoryNotFoundException("Не знайдено корінь репозиторію");
        return Path.Combine(dir.FullName, "GenDoc", "Services", "DatabaseSchemaInitializer.cs");
    }

    private static void CreateOldSchema(SqliteConnection connection)
    {
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE "GeneratedGroupDocuments" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_GeneratedGroupDocuments" PRIMARY KEY AUTOINCREMENT,
                    "ExportTemplateId" INTEGER NOT NULL,
                    "RunId" INTEGER NULL,
                    "IntakeId" INTEGER NULL,
                    "GeneratedAt" TEXT NOT NULL,
                    "GeneratedByUserId" INTEGER NOT NULL,
                    "FileName" TEXT NOT NULL,
                    "ContentHash" TEXT NULL,
                    "RosterHash" TEXT NULL,
                    "SizeBytes" INTEGER NOT NULL DEFAULT 0,
                    "RecipientCount" INTEGER NOT NULL DEFAULT 0,
                    "Version" INTEGER NOT NULL DEFAULT 1,
                    "IsCurrent" INTEGER NOT NULL DEFAULT 1,
                    "HasContent" INTEGER NOT NULL DEFAULT 0,
                    "DeletedAt" TEXT NULL,
                    "DeletedBy" TEXT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE "GeneratedGroupDocumentContents" (
                    "GeneratedGroupDocumentId" INTEGER NOT NULL CONSTRAINT "PK_GeneratedGroupDocumentContents" PRIMARY KEY,
                    "Content" BLOB NOT NULL,
                    CONSTRAINT "FK_GeneratedGroupDocumentContents_GeneratedGroupDocuments_GeneratedGroupDocumentId"
                        FOREIGN KEY ("GeneratedGroupDocumentId") REFERENCES "GeneratedGroupDocuments" ("Id") ON DELETE CASCADE
                );
                """;
            cmd.ExecuteNonQuery();
        }
    }

    private static void InsertOldRow(SqliteConnection connection, int id, int exportTemplateId)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"""
                INSERT INTO "GeneratedGroupDocuments"
                    ("Id", "ExportTemplateId", "GeneratedAt", "GeneratedByUserId", "FileName")
                VALUES ({id}, {exportTemplateId}, '2025-01-01', 1, 'test.xlsx');
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"""
                INSERT INTO "GeneratedGroupDocumentContents" ("GeneratedGroupDocumentId", "Content")
                VALUES ({id}, x'0102');
                """;
            cmd.ExecuteNonQuery();
        }
    }

    [Fact]
    public void BackfillRunIntakeIds_TakesMostFrequentIntakeOfRunDocuments()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Exec(connection, """
            CREATE TABLE "GenerationPackageRuns" ("Id" INTEGER PRIMARY KEY, "IntakeId" INTEGER NULL);
            CREATE TABLE "GeneratedDocuments" ("Id" INTEGER PRIMARY KEY, "RunId" INTEGER NULL, "IntakeId" INTEGER NULL);
            CREATE TABLE "GeneratedGroupDocuments" ("Id" INTEGER PRIMARY KEY, "RunId" INTEGER NULL, "IntakeId" INTEGER NULL);
            INSERT INTO "GenerationPackageRuns" VALUES (1, NULL), (2, NULL), (3, 9);
            INSERT INTO "GeneratedDocuments" VALUES (1, 1, 4), (2, 1, 4), (3, 1, 5), (4, 3, 2);
            INSERT INTO "GeneratedGroupDocuments" VALUES (1, 1, 5);
            """);

        DatabaseSchemaInitializer.BackfillRunIntakeIds((DbConnection)connection);
        DatabaseSchemaInitializer.BackfillRunIntakeIds((DbConnection)connection);

        Assert.Equal(4L, Scalar(connection, """SELECT "IntakeId" FROM "GenerationPackageRuns" WHERE "Id" = 1"""));
        Assert.Equal(DBNull.Value, Scalar(connection, """SELECT "IntakeId" FROM "GenerationPackageRuns" WHERE "Id" = 2"""));
        Assert.Equal(9L, Scalar(connection, """SELECT "IntakeId" FROM "GenerationPackageRuns" WHERE "Id" = 3"""));
    }

    [Fact]
    public void BackfillGroupDocumentIntakeIds_TakesIntakeOfRunAndRestoresOneCurrentPerSeries()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Exec(connection, """
            CREATE TABLE "GenerationPackageRuns" ("Id" INTEGER PRIMARY KEY, "IntakeId" INTEGER NULL);
            CREATE TABLE "GeneratedGroupDocuments" (
                "Id" INTEGER PRIMARY KEY, "RunId" INTEGER NULL, "IntakeId" INTEGER NULL,
                "ExportTemplateId" INTEGER NULL, "TemplateId" INTEGER NULL,
                "Version" INTEGER NOT NULL, "IsCurrent" INTEGER NOT NULL, "DeletedAt" TEXT NULL);
            INSERT INTO "GenerationPackageRuns" VALUES (1, 4), (2, 5), (3, NULL);
            INSERT INTO "GeneratedGroupDocuments" VALUES
                (1, 1, NULL, 7, NULL, 1, 0, NULL),
                (2, 2, NULL, 7, NULL, 2, 1, NULL),
                (3, 3, NULL, 7, NULL, 3, 0, NULL),
                (4, 1, 9, 7, NULL, 1, 1, NULL),
                (5, 1, NULL, 8, NULL, 1, 0, '2026-01-01'),
                (6, NULL, NULL, NULL, 12, 1, 1, NULL),
                (7, 1, NULL, NULL, 13, 1, 0, NULL),
                (8, 1, NULL, NULL, 13, 2, 0, NULL);
            """);

        DatabaseSchemaInitializer.BackfillGroupDocumentIntakeIds((DbConnection)connection);
        DatabaseSchemaInitializer.BackfillGroupDocumentIntakeIds((DbConnection)connection);

        object Intake(int id) => Scalar(connection, $"""SELECT "IntakeId" FROM "GeneratedGroupDocuments" WHERE "Id" = {id}""");
        object Current(int id) => Scalar(connection, $"""SELECT "IsCurrent" FROM "GeneratedGroupDocuments" WHERE "Id" = {id}""");

        Assert.Equal(4L, Intake(1));
        Assert.Equal(5L, Intake(2));
        Assert.Equal(DBNull.Value, Intake(3));
        Assert.Equal(9L, Intake(4));
        Assert.Equal(4L, Intake(5));
        Assert.Equal(DBNull.Value, Intake(6));

        Assert.Equal(1L, Current(1));
        Assert.Equal(1L, Current(2));
        Assert.Equal(1L, Current(3));
        Assert.Equal(1L, Current(4));
        Assert.Equal(0L, Current(5));
        Assert.Equal(1L, Current(6));
        Assert.Equal(0L, Current(7));
        Assert.Equal(1L, Current(8));
    }

    [Fact]
    public void MigrateGenerationPackageRunsForAdHocRuns_MakesPackageNullableAndKeepsRows()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Exec(connection, """
            CREATE TABLE "GenerationPackages" ("Id" INTEGER NOT NULL CONSTRAINT "PK_GenerationPackages" PRIMARY KEY AUTOINCREMENT, "Name" TEXT NOT NULL);
            CREATE TABLE "Users" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT, "FullName" TEXT NOT NULL);
            CREATE TABLE "GenerationPackageRuns" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_GenerationPackageRuns" PRIMARY KEY AUTOINCREMENT,
                "GenerationPackageId" INTEGER NOT NULL,
                "RunAt" TEXT NOT NULL,
                "RunByUserId" INTEGER NOT NULL,
                "GeneratedCount" INTEGER NOT NULL,
                "SkippedCount" INTEGER NOT NULL,
                "ErrorCount" INTEGER NOT NULL,
                "Summary" TEXT NULL,
                "IntakeId" INTEGER NULL,
                "BranchName" TEXT NULL,
                CONSTRAINT "FK_GenerationPackageRuns_GenerationPackages_GenerationPackageId" FOREIGN KEY ("GenerationPackageId") REFERENCES "GenerationPackages" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_GenerationPackageRuns_Users_RunByUserId" FOREIGN KEY ("RunByUserId") REFERENCES "Users" ("Id") ON DELETE CASCADE);
            INSERT INTO "GenerationPackages" VALUES (7, 'П'); INSERT INTO "Users" VALUES (1, 'Тест');
            INSERT INTO "GenerationPackageRuns" ("Id","GenerationPackageId","RunAt","RunByUserId","GeneratedCount","SkippedCount","ErrorCount","Summary","IntakeId","BranchName")
            VALUES (1, 7, '2026-08-12 09:35:00', 1, 4, 0, 0, NULL, 4, NULL);
            """);

        DatabaseSchemaInitializer.MigrateGenerationPackageRunsForAdHocRuns((DbConnection)connection);
        DatabaseSchemaInitializer.MigrateGenerationPackageRunsForAdHocRuns((DbConnection)connection);

        Assert.Equal(1L, Scalar(connection, """SELECT COUNT(*) FROM "GenerationPackageRuns" WHERE "Id" = 1 AND "GenerationPackageId" = 7 AND "IntakeId" = 4"""));
        Exec(connection, """INSERT INTO "GenerationPackageRuns" ("GenerationPackageId","RunAt","RunByUserId","GeneratedCount","SkippedCount","ErrorCount") VALUES (NULL, '2026-08-19', 1, 0, 0, 0);""");
        Assert.Equal(2L, Scalar(connection, """SELECT COUNT(*) FROM "GenerationPackageRuns";"""));
        Assert.False(DatabaseSchemaInitializer.ColumnIsNotNull((DbConnection)connection, "GenerationPackageRuns", "GenerationPackageId"));
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object Scalar(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()!;
    }
}
