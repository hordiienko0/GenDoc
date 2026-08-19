using System.Data.Common;
using GenDoc.Services;
using Microsoft.Data.Sqlite;

namespace GenDoc.Tests;

// Найризикованіша частина схеми v12: перебудова GeneratedGroupDocuments (стара таблиця
// мала ExportTemplateId NOT NULL, без TemplateId). Перевіряємо на звичайному
// (незашифрованому) SQLite, що дані й дочірній вміст переживають перебудову, а
// зовнішній ключ ON DELETE CASCADE не спрацьовує під час DROP TABLE.
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

        // Колонка TemplateId з'явилась, стара колонка ExportTemplateId - тепер nullable.
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
            Assert.False(reader.Read()); // рівно один рядок
        }

        // Дочірній вміст НЕ мав каскадно видалитись під час DROP TABLE/перебудови.
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """SELECT COUNT(*) FROM "GeneratedGroupDocumentContents" WHERE "GeneratedGroupDocumentId" = 1;""";
            var count = (long)cmd.ExecuteScalar()!;
            Assert.Equal(1, count);
        }

        // Можна вставити новий рядок groupового DOCX: ExportTemplateId = NULL, TemplateId заповнено.
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

    // Ідемпотентність: якщо міграцію вже застосовано (TemplateId є), повторний виклик
    // не повинен нічого ламати чи дублювати.
    [Fact]
    public void MigrateGeneratedGroupDocumentsForDocxSupport_AlreadyMigrated_IsNoOp()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        CreateOldSchema(connection);
        InsertOldRow(connection, id: 1, exportTemplateId: 7);

        DatabaseSchemaInitializer.MigrateGeneratedGroupDocumentsForDocxSupport((DbConnection)connection);
        DatabaseSchemaInitializer.MigrateGeneratedGroupDocumentsForDocxSupport((DbConnection)connection); // повторно

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """SELECT COUNT(*) FROM "GeneratedGroupDocuments";""";
        var count = (long)cmd.ExecuteScalar()!;
        Assert.Equal(1, count);
    }

    // Реальний збій: у робочій базі таблиця Weapons з'явилась з проміжного білда без
    // RawText. CREATE TABLE її вже не перестворює, тож EF валився на генерації з
    // 'no such column: w.RawText'. Ensure* мусить дорощувати колонки, а не лише створювати.
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

        // Наявні дані не втрачені, і запит із новою колонкою тепер виконується.
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """SELECT "Name", "SerialNumber", "RawText" FROM "Weapons" WHERE "Id" = 1;""";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("АКС-74", reader.GetString(0));
            Assert.Equal("903530", reader.GetString(1));
            Assert.True(reader.IsDBNull(2));
        }

        // Вставка з RawText - те, що робить імпорт зброї.
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO "Weapons" ("RecipientId", "Name", "SerialNumber", "RawText")
                VALUES (5, 'ПМ', '1234', 'ПМ №1234');
                """;
            cmd.ExecuteNonQuery();
        }
    }

    // Чиста база: таблиці створюються з нуля вже повними, повторний виклик - no-op.
    [Fact]
    public void EnsureWeaponVehicleTables_FreshDatabase_CreatesTablesAndIsIdempotent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        DatabaseSchemaInitializer.EnsureWeaponVehicleTables((DbConnection)connection);
        DatabaseSchemaInitializer.EnsureWeaponVehicleTables((DbConnection)connection); // повторно

        var weaponColumns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "Weapons");
        Assert.Contains("RawText", weaponColumns);

        var vehicleColumns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "Vehicles");
        Assert.Contains("Model", vehicleColumns);
        Assert.Contains("PlateNumber", vehicleColumns);

        // Жодна колонка не продубльована повторним викликом.
        Assert.Equal(weaponColumns.Count, DatabaseSchemaInitializer
            .GetExistingColumns((DbConnection)connection, "Weapons").Count);
    }

    // v21: конструктору потрібне джерело блоків поруч із байтами .docx. Колонка
    // додається до наявної таблиці з даними, тож мусить бути nullable - інакше
    // Поділ шаблонів на набори й постійний склад. Найважливіше тут - що наявні
    // шаблони дістають Audience = 0 (набори), тобто лишаються там, де були:
    // мовчазне переселення половини шаблонів у постійний склад помітили б не
    // одразу. NOT NULL без DEFAULT SQLite узагалі не дав би додати.
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
            (DbConnection)connection, "Templates", DatabaseSchemaInitializer.TemplateColumnsV23); // повторно

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

    // SQLite не дасть ADD COLUMN без DEFAULT.
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
            (DbConnection)connection, "Templates", DatabaseSchemaInitializer.TemplateColumnsV21); // повторно

        var columns = DatabaseSchemaInitializer.GetExistingColumns((DbConnection)connection, "Templates");
        Assert.Contains("BuilderJson", columns);

        // Шаблон, завантажений файлом, лишається з порожнім джерелом - саме за цим
        // конструктор і відрізняє «своє» від чужого .docx.
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

    // v22 - парна до v21, але для відомостей: ExportTemplates уже жила в базі до
    // конструктора, тож колонка додається до наявної таблиці з даними.
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
            (DbConnection)connection, "ExportTemplates", DatabaseSchemaInitializer.ExportTemplateColumnsV22); // повторно

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

    private static void CreateOldSchema(SqliteConnection connection)
    {
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            // Схема v11 - точна копія CREATE TABLE до цієї міграції.
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
}
