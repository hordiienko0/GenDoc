using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services;

public class DatabaseSchemaInitializer : IDatabaseSchemaInitializer
{
    private const int CurrentSchemaVersion = 4;

    private static readonly string[] QuestionnaireColumns =
    {
        "Nationality", "Vos", "CourseArrivalDate", "MaritalStatus", "RegistrationAddress",
        "ResidenceAddress", "Phone", "Note", "GroupName", "NameTransliterated",
        "ServedBefore", "ExtraNote", "CommanderContact", "TravelCertificateNumber",
        "FoodCertificate", "IdDocumentNumber", "MedicalBoard", "MedicalBoardConclusion",
        "OriginUnit", "Vehicle"
    };

    private static readonly string[] OrganizationSettingsColumnsV3 = { "HrOfficerFullName" };
    private static readonly string[] RoomColumnsV4 = { "Note", "CreatedAt", "CreatedBy" };

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IExportTemplateService _exportTemplateService;

    public DatabaseSchemaInitializer(IDbContextFactory<AppDbContext> dbFactory, IExportTemplateService exportTemplateService)
    {
        _dbFactory = dbFactory;
        _exportTemplateService = exportTemplateService;
    }

    public void EnsureInitialized()
    {
        using var db = _dbFactory.CreateDbContext();
        db.Database.EnsureCreated();

        // EnsureCreated() — no-op для вже існуючого файлу БД: таблиці, додані в модель
        // ПІСЛЯ першого створення бази (напр. ExportTemplates), самі не з'являться.
        // Тому створюємо їх явно, якщо відсутні — незалежно від SchemaVersion.
        EnsureExportTemplateTables(db);

        if (!db.SchemaVersions.Any())
        {
            db.SchemaVersions.Add(new SchemaVersion
            {
                Version = CurrentSchemaVersion,
                AppliedAt = DateTime.Now,
                Description = "Початкова схема (v4, кімнати)"
            });
        }
        else
        {
            var currentVersion = db.SchemaVersions.Max(s => s.Version);

            if (currentVersion < 2)
            {
                AddMissingColumns(db, "Recipients", QuestionnaireColumns);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 2,
                    AppliedAt = DateTime.Now,
                    Description = "Анкетні дані прикомандированих"
                });
                currentVersion = 2;
            }

            if (currentVersion < 3)
            {
                AddMissingColumns(db, "OrganizationSettings", OrganizationSettingsColumnsV3);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 3,
                    AppliedAt = DateTime.Now,
                    Description = "ПІБ начальника служби персоналу"
                });
                currentVersion = 3;
            }

            if (currentVersion < 4)
            {
                AddMissingColumns(db, "Rooms", RoomColumnsV4);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 4,
                    AppliedAt = DateTime.Now,
                    Description = "Кімнати: примітка, дата створення"
                });
            }
        }

        // Ідемпотентно (IF NOT EXISTS) — самовідновлюється незалежно від SchemaVersion,
        // так само як EnsureExportTemplateTables. Обгорнуто в try/catch: якщо в
        // існуючих даних вже є дублікати (Building, Number), унікальний індекс
        // не повинен зривати запуск застосунку.
        EnsureRoomUniqueIndex(db);

        // Самовідновлення: якщо міграція v3 вже позначена виконаною раніше (до
        // цього виправлення), рядок міг лишитись з HrOfficerFullName = NULL —
        // не-nullable властивість моделі, EF падає при читанні. На цьому етапі
        // колонка вже гарантовано існує (щойно мігровано або створено з нуля).
        db.Database.ExecuteSqlRaw("UPDATE OrganizationSettings SET HrOfficerFullName = '' WHERE HrOfficerFullName IS NULL;");

        if (!db.AppSettings.Any())
        {
            db.AppSettings.Add(new Models.AppSettings());
        }

        if (!db.OrganizationSettings.Any())
        {
            db.OrganizationSettings.Add(new OrganizationSettings());
        }

        db.SaveChanges();

        _exportTemplateService.EnsureBuiltInTemplate();
    }

    private static void AddMissingColumns(AppDbContext db, string tableName, string[] columns)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = command.ExecuteReader();
            var nameOrdinal = reader.GetOrdinal("name");
            while (reader.Read())
            {
                existingColumns.Add(reader.GetString(nameOrdinal));
            }
        }
        finally
        {
            if (wasClosed) connection.Close();
        }

        foreach (var column in columns)
        {
            if (existingColumns.Contains(column)) continue;
            string sql = "ALTER TABLE " + tableName + " ADD COLUMN " + column + " TEXT NULL;";
            db.Database.ExecuteSqlRaw(sql);
        }
    }

    private static void EnsureRoomUniqueIndex(AppDbContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw(
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Rooms_Building_Number" ON "Rooms" ("Building", "Number") WHERE "DeletedAt" IS NULL;""");
        }
        catch
        {
            // Найімовірніша причина — наявні дублікати (Building, Number) у старих даних.
            // Не зриваємо запуск застосунку через це; унікальність далі перевіряється
            // на рівні RoomService при створенні/редагуванні кімнати.
        }
    }

    private static void EnsureExportTemplateTables(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();

        try
        {
            if (!TableExists(connection, "ExportTemplates"))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "ExportTemplates" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_ExportTemplates" PRIMARY KEY AUTOINCREMENT,
                        "Name" TEXT NOT NULL,
                        "OriginalFileName" TEXT NOT NULL,
                        "Content" BLOB NOT NULL,
                        "IsBuiltIn" INTEGER NOT NULL,
                        "UploadedAt" TEXT NOT NULL,
                        "DeletedAt" TEXT NULL,
                        "DeletedBy" TEXT NULL
                    );
                    """;
                command.ExecuteNonQuery();
            }

            if (!TableExists(connection, "ExportTemplateColumnMappings"))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "ExportTemplateColumnMappings" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_ExportTemplateColumnMappings" PRIMARY KEY AUTOINCREMENT,
                        "ExportTemplateId" INTEGER NOT NULL,
                        "ColumnIndex" INTEGER NOT NULL,
                        "HeaderText" TEXT NOT NULL,
                        "FieldKey" TEXT NOT NULL,
                        CONSTRAINT "FK_ExportTemplateColumnMappings_ExportTemplates_ExportTemplateId"
                            FOREIGN KEY ("ExportTemplateId") REFERENCES "ExportTemplates" ("Id") ON DELETE CASCADE
                    );
                    """;
                command.ExecuteNonQuery();

                using var indexCommand = connection.CreateCommand();
                indexCommand.CommandText =
                    """CREATE INDEX "IX_ExportTemplateColumnMappings_ExportTemplateId" ON "ExportTemplateColumnMappings" ("ExportTemplateId");""";
                indexCommand.ExecuteNonQuery();
            }
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }

    private static bool TableExists(System.Data.Common.DbConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $tableName;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$tableName";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        using var reader = command.ExecuteReader();
        return reader.Read();
    }
}
