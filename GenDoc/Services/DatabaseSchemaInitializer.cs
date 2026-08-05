using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services;

public class DatabaseSchemaInitializer : IDatabaseSchemaInitializer
{
    private const int CurrentSchemaVersion = 15;

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

    private static readonly (string Name, string Type)[] RecipientColumnsV5 =
    {
        ("OrgNodeId", "INTEGER"), ("IntakeId", "INTEGER"), ("FitnessCategory", "TEXT")
    };
    private static readonly (string Name, string Type)[] AppSettingsColumnsV5 =
    {
        ("IntakeNumberTemplate", "TEXT")
    };

    private static readonly (string Name, string Type)[] GeneratedDocumentColumnsV6 =
    {
        ("ContentHash", "TEXT"), ("SizeBytes", "INTEGER NOT NULL DEFAULT 0"),
        ("Version", "INTEGER NOT NULL DEFAULT 1"), ("IsCurrent", "INTEGER NOT NULL DEFAULT 1"),
        ("SourceType", "INTEGER NOT NULL DEFAULT 0"), ("RunId", "INTEGER"),
        ("IntakeId", "INTEGER"), ("OrgNodeIdSnapshot", "INTEGER"), ("OrgPathSnapshot", "TEXT"),
        ("HasContent", "INTEGER NOT NULL DEFAULT 0"), ("DeletedAt", "TEXT"), ("DeletedBy", "TEXT")
    };

    private static readonly (string Name, string Type)[] AppSettingsColumnsV6 =
    {
        ("ExportFileNameTemplate", "TEXT"), ("MaxDocumentSizeKb", "INTEGER")
    };

    private static readonly (string Name, string Type)[] RunColumnsV6 =
    {
        ("IntakeId", "INTEGER"), ("BranchName", "TEXT")
    };

    private static readonly (string Name, string Type)[] TemplateColumnsV7 =
    {
        ("ShortName", "TEXT")
    };

    // Default 0 = Required: наявні зв'язки поводяться як раніше.
    private static readonly (string Name, string Type)[] PackageTemplateColumnsV7 =
    {
        ("RequirementRegular", "INTEGER NOT NULL DEFAULT 0"),
        ("RequirementLimited", "INTEGER NOT NULL DEFAULT 0")
    };

    private static readonly (string Name, string Type)[] GeneratedDocumentColumnsV7 =
    {
        ("SourceHash", "TEXT")
    };

    private static readonly (string Name, string Type)[] AppSettingsColumnsV7 =
    {
        ("DetectStaleDocuments", "INTEGER"), ("DefaultGenerationPackageId", "INTEGER")
    };

    private static readonly (string Name, string Type)[] IntakeColumnsV8 =
    {
        ("DateClosed", "TEXT"), ("ClosedBy", "TEXT"),
        ("StatusIsPinned", "INTEGER NOT NULL DEFAULT 0"), ("DefaultPackageId", "INTEGER")
    };

    private static readonly (string Name, string Type)[] ExportTemplateColumnsV10 =
    {
        ("TemplateRowIndex", "INTEGER NOT NULL DEFAULT 2"), ("UsesPlaceholders", "INTEGER NOT NULL DEFAULT 0")
    };

    private static readonly (string Name, string Type)[] ExportTemplateColumnMappingColumnsV10 =
    {
        ("PlaceholderTag", "TEXT NOT NULL DEFAULT ''"), ("SourceType", "INTEGER NOT NULL DEFAULT 0")
    };

    private static readonly (string Name, string Type)[] OrganizationSettingsColumnsV10 =
    {
        ("CommanderPosition", "TEXT"), ("UnitFullName", "TEXT")
    };

    private static readonly (string Name, string Type)[] TemplateColumnsV12 =
    {
        ("Kind", "INTEGER NOT NULL DEFAULT 0")
    };

    private static readonly (string Name, string Type)[] TemplateFieldMappingColumnsV12 =
    {
        ("IsInsideRepeatingBlock", "INTEGER NOT NULL DEFAULT 0")
    };

    private static readonly (string Name, string Type)[] AppSettingsColumnsV13 =
    {
        ("LastManualValuesJson", "TEXT")
    };

    private static readonly (string Name, string Type)[] RecipientColumnsV14 =
    {
        ("Gender", "INTEGER"), ("RankAccusative", "TEXT"),
        ("FullNameAccusative", "TEXT"), ("PositionAccusative", "TEXT")
    };

    private static readonly (string Name, string Type)[] RecipientColumnsV15 =
    {
        ("TravelCertificateDate", "TEXT")
    };

    private static readonly (string Name, string Type)[] AppSettingsColumnsV15 =
    {
        ("LastSignerByTemplateJson", "TEXT")
    };

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
                Description = "Початкова схема (v5, дерево підрозділів)"
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
                currentVersion = 4;
            }

            if (currentVersion < 5)
            {
                AddMissingColumns(db, "Recipients", RecipientColumnsV5);
                AddMissingColumns(db, "AppSettings", AppSettingsColumnsV5);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 5,
                    AppliedAt = DateTime.Now,
                    Description = "Дерево підрозділів і набори"
                });
                currentVersion = 5;
            }

            if (currentVersion < 6)
            {
                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 6,
                    AppliedAt = DateTime.Now,
                    Description = "Архів документів: версії, контент, вкладення"
                });
                currentVersion = 6;
            }

            if (currentVersion < 7)
            {
                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 7,
                    AppliedAt = DateTime.Now,
                    Description = "Комплектність: вимоги пакета, короткі назви, source-хеш"
                });
                currentVersion = 7;
            }

            if (currentVersion < 8)
            {
                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 8,
                    AppliedAt = DateTime.Now,
                    Description = "Набори: закриття/повторне відкриття, дефолтний пакет"
                });
                currentVersion = 8;
            }

            if (currentVersion < 9)
            {
                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 9,
                    AppliedAt = DateTime.Now,
                    Description = "Постійний склад: відрядження/відпустки"
                });
                currentVersion = 9;
            }

            if (currentVersion < 10)
            {
                AddMissingColumns(db, "ExportTemplates", ExportTemplateColumnsV10);
                AddMissingColumns(db, "ExportTemplateColumnMappings", ExportTemplateColumnMappingColumnsV10);
                AddMissingColumns(db, "OrganizationSettings", OrganizationSettingsColumnsV10);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 10,
                    AppliedAt = DateTime.Now,
                    Description = "Експорт за тегами: рядок-шаблон XLSX, посада/повна назва частини"
                });
                currentVersion = 10;
            }

            if (currentVersion < 11)
            {
                EnsureGroupDocumentTables(db);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 11,
                    AppliedAt = DateTime.Now,
                    Description = "XLSX-шаблони у пакетах генерації: групові документи, фільтр придатності"
                });
                currentVersion = 11;
            }

            if (currentVersion < 12)
            {
                AddMissingColumns(db, "Templates", TemplateColumnsV12);
                AddMissingColumns(db, "TemplateFieldMappings", TemplateFieldMappingColumnsV12);
                MigrateGeneratedGroupDocumentsForDocxSupport(db);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 12,
                    AppliedAt = DateTime.Now,
                    Description = "Груповий DOCX: тип шаблону, повторювані блоки, GeneratedGroupDocument.TemplateId"
                });
                currentVersion = 12;
            }

            if (currentVersion < 13)
            {
                AddMissingColumns(db, "AppSettings", AppSettingsColumnsV13);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 13,
                    AppliedAt = DateTime.Now,
                    Description = "Генерація в догонку: збереження останніх ручних міток"
                });
                currentVersion = 13;
            }

            if (currentVersion < 14)
            {
                AddMissingColumns(db, "Recipients", RecipientColumnsV14);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 14,
                    AppliedAt = DateTime.Now,
                    Description = "Граматика: стать, уточнення відмінків (звання/ПІБ/посада у знахідному)"
                });
                currentVersion = 14;
            }

            if (currentVersion < 15)
            {
                AddMissingColumns(db, "Recipients", RecipientColumnsV15);
                AddMissingColumns(db, "AppSettings", AppSettingsColumnsV15);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 15,
                    AppliedAt = DateTime.Now,
                    Description = "Рапорти на котлове: дата посвідчення, пам'ять підписанта"
                });
            }
        }

        // Ідемпотентно, як EnsureExportTemplateTables: таблиці, додані в модель після
        // першого створення бази, EnsureCreated сам не створить.
        EnsureOrgTables(db);
        AddMissingColumns(db, "Recipients", RecipientColumnsV5);
        AddMissingColumns(db, "AppSettings", AppSettingsColumnsV5);
        SeedOrgTree(db);

        AddMissingColumns(db, "GeneratedDocuments", GeneratedDocumentColumnsV6);
        AddMissingColumns(db, "AppSettings", AppSettingsColumnsV6);
        AddMissingColumns(db, "GenerationPackageRuns", RunColumnsV6);
        EnsureArchiveTables(db);
        EnsureArchiveIndexes(db);

        AddMissingColumns(db, "Templates", TemplateColumnsV7);
        AddMissingColumns(db, "GenerationPackageTemplates", PackageTemplateColumnsV7);
        AddMissingColumns(db, "GeneratedDocuments", GeneratedDocumentColumnsV7);
        AddMissingColumns(db, "AppSettings", AppSettingsColumnsV7);

        AddMissingColumns(db, "Intakes", IntakeColumnsV8);

        EnsureStaffTables(db);

        AddMissingColumns(db, "ExportTemplates", ExportTemplateColumnsV10);
        AddMissingColumns(db, "ExportTemplateColumnMappings", ExportTemplateColumnMappingColumnsV10);
        AddMissingColumns(db, "OrganizationSettings", OrganizationSettingsColumnsV10);

        EnsureGroupDocumentTables(db);

        AddMissingColumns(db, "Templates", TemplateColumnsV12);
        AddMissingColumns(db, "TemplateFieldMappings", TemplateFieldMappingColumnsV12);
        MigrateGeneratedGroupDocumentsForDocxSupport(db);

        AddMissingColumns(db, "AppSettings", AppSettingsColumnsV13);
        AddMissingColumns(db, "Recipients", RecipientColumnsV14);
        AddMissingColumns(db, "Recipients", RecipientColumnsV15);
        AddMissingColumns(db, "AppSettings", AppSettingsColumnsV15);

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
        db.Database.ExecuteSqlRaw("UPDATE OrganizationSettings SET CommanderPosition = '' WHERE CommanderPosition IS NULL;");
        db.Database.ExecuteSqlRaw("UPDATE OrganizationSettings SET UnitFullName = '' WHERE UnitFullName IS NULL;");

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

    private static void AddMissingColumns(AppDbContext db, string tableName, (string Name, string Type)[] columns)
    {
        var existingColumns = GetExistingColumns(db, tableName);

        foreach (var (name, type) in columns)
        {
            if (existingColumns.Contains(name)) continue;
            db.Database.ExecuteSqlRaw("ALTER TABLE " + tableName + " ADD COLUMN " + name + " " + type + ";");
        }
    }

    private static HashSet<string> GetExistingColumns(AppDbContext db, string tableName)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();

        try
        {
            return GetExistingColumns(connection, tableName);
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }

    // Перевантаження напряму на з'єднанні — потрібне там, де AppDbContext ще нема
    // (юніт-тести) або де з'єднання вже підняте окремо (перебудова таблиці).
    internal static HashSet<string> GetExistingColumns(System.Data.Common.DbConnection connection, string tableName)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        var nameOrdinal = reader.GetOrdinal("name");
        while (reader.Read())
        {
            existingColumns.Add(reader.GetString(nameOrdinal));
        }

        return existingColumns;
    }

    private static void EnsureOrgTables(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();

        try
        {
            if (!TableExists(connection, "OrgNodes"))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "OrgNodes" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_OrgNodes" PRIMARY KEY AUTOINCREMENT,
                        "Name" TEXT NOT NULL,
                        "DocumentName" TEXT NULL,
                        "ParentId" INTEGER NULL,
                        "Path" TEXT NOT NULL,
                        "Depth" INTEGER NOT NULL,
                        "SortOrder" INTEGER NOT NULL,
                        "IntakeId" INTEGER NULL,
                        "IsActive" INTEGER NOT NULL DEFAULT 1,
                        "DeletedAt" TEXT NULL,
                        "DeletedBy" TEXT NULL,
                        CONSTRAINT "FK_OrgNodes_OrgNodes_ParentId"
                            FOREIGN KEY ("ParentId") REFERENCES "OrgNodes" ("Id")
                    );
                    """;
                command.ExecuteNonQuery();

                using var indexCommand = connection.CreateCommand();
                indexCommand.CommandText = """CREATE INDEX "IX_OrgNodes_Path" ON "OrgNodes" ("Path");""";
                indexCommand.ExecuteNonQuery();
            }

            if (!TableExists(connection, "Intakes"))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "Intakes" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_Intakes" PRIMARY KEY AUTOINCREMENT,
                        "Number" INTEGER NOT NULL,
                        "DisplayNumber" TEXT NOT NULL,
                        "DateStart" TEXT NOT NULL,
                        "DateEnd" TEXT NOT NULL,
                        "Status" INTEGER NOT NULL,
                        "RootOrgNodeId" INTEGER NOT NULL,
                        "DeletedAt" TEXT NULL,
                        "DeletedBy" TEXT NULL
                    );
                    """;
                command.ExecuteNonQuery();
            }
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }

    // Корінь дерева + разова міграція плоских Units у вузли під коренем,
    // з прив'язкою людей до відповідних вузлів.
    private static void SeedOrgTree(AppDbContext db)
    {
        if (db.OrgNodes.IgnoreQueryFilters().Any()) return;

        var orgName = db.OrganizationSettings.Select(o => o.UnitNumber).FirstOrDefault();
        var root = new OrgNode
        {
            Name = string.IsNullOrWhiteSpace(orgName) ? "Військова частина" : orgName,
            Depth = 0,
            SortOrder = 0
        };
        db.OrgNodes.Add(root);
        db.SaveChanges();
        root.Path = $"/{root.Id}/";

        var sortOrder = 0;
        foreach (var unit in db.Units.OrderBy(u => u.Name).ToList())
        {
            var node = new OrgNode
            {
                Name = unit.Name,
                Parent = root,
                Depth = 1,
                SortOrder = sortOrder++
            };
            db.OrgNodes.Add(node);
            db.SaveChanges();
            node.Path = $"{root.Path}{node.Id}/";

            db.Database.ExecuteSqlRaw(
                "UPDATE Recipients SET OrgNodeId = {0} WHERE UnitId = {1};", node.Id, unit.Id);
        }

        db.SaveChanges();
    }

    private static void EnsureArchiveTables(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();

        try
        {
            if (!TableExists(connection, "GeneratedDocumentContents"))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "GeneratedDocumentContents" (
                        "GeneratedDocumentId" INTEGER NOT NULL CONSTRAINT "PK_GeneratedDocumentContents" PRIMARY KEY,
                        "Content" BLOB NOT NULL,
                        CONSTRAINT "FK_GeneratedDocumentContents_GeneratedDocuments_GeneratedDocumentId"
                            FOREIGN KEY ("GeneratedDocumentId") REFERENCES "GeneratedDocuments" ("Id") ON DELETE CASCADE
                    );
                    """;
                command.ExecuteNonQuery();
            }

            if (!TableExists(connection, "DocumentAttachments"))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "DocumentAttachments" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_DocumentAttachments" PRIMARY KEY AUTOINCREMENT,
                        "GeneratedDocumentId" INTEGER NOT NULL,
                        "FileName" TEXT NOT NULL,
                        "Content" BLOB NOT NULL,
                        "SizeBytes" INTEGER NOT NULL,
                        "Note" TEXT NULL,
                        "UploadedBy" TEXT NOT NULL,
                        "UploadedAt" TEXT NOT NULL,
                        "DeletedAt" TEXT NULL,
                        "DeletedBy" TEXT NULL,
                        CONSTRAINT "FK_DocumentAttachments_GeneratedDocuments_GeneratedDocumentId"
                            FOREIGN KEY ("GeneratedDocumentId") REFERENCES "GeneratedDocuments" ("Id") ON DELETE CASCADE
                    );
                    """;
                command.ExecuteNonQuery();

                using var indexCommand = connection.CreateCommand();
                indexCommand.CommandText =
                    """CREATE INDEX "IX_DocumentAttachments_GeneratedDocumentId" ON "DocumentAttachments" ("GeneratedDocumentId");""";
                indexCommand.ExecuteNonQuery();
            }
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }

    private static void EnsureArchiveIndexes(AppDbContext db)
    {
        // Історичний унікальний індекс блокує версійність тієї самої пари.
        db.Database.ExecuteSqlRaw("""DROP INDEX IF EXISTS "IX_GeneratedDocuments_RecipientId_TemplateId";""");
        db.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_GeneratedDocuments_RecipientId_TemplateId_IsCurrent" ON "GeneratedDocuments" ("RecipientId", "TemplateId", "IsCurrent");""");
        db.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_GeneratedDocuments_GeneratedAt" ON "GeneratedDocuments" ("GeneratedAt");""");
        db.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_GeneratedDocuments_RunId" ON "GeneratedDocuments" ("RunId");""");
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
                        "TemplateRowIndex" INTEGER NOT NULL DEFAULT 2,
                        "UsesPlaceholders" INTEGER NOT NULL DEFAULT 0,
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
                        "PlaceholderTag" TEXT NOT NULL DEFAULT '',
                        "SourceType" INTEGER NOT NULL DEFAULT 0,
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

    private static void EnsureGroupDocumentTables(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();

        try
        {
            if (!TableExists(connection, "GenerationPackageExportTemplates"))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "GenerationPackageExportTemplates" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_GenerationPackageExportTemplates" PRIMARY KEY AUTOINCREMENT,
                        "GenerationPackageId" INTEGER NOT NULL,
                        "ExportTemplateId" INTEGER NOT NULL,
                        "SortOrder" INTEGER NOT NULL,
                        "FitnessFilter" INTEGER NOT NULL DEFAULT 0,
                        CONSTRAINT "FK_GenerationPackageExportTemplates_GenerationPackages_GenerationPackageId"
                            FOREIGN KEY ("GenerationPackageId") REFERENCES "GenerationPackages" ("Id") ON DELETE CASCADE
                    );
                    """;
                command.ExecuteNonQuery();

                using var indexCommand = connection.CreateCommand();
                indexCommand.CommandText =
                    """CREATE INDEX "IX_GenerationPackageExportTemplates_GenerationPackageId" ON "GenerationPackageExportTemplates" ("GenerationPackageId");""";
                indexCommand.ExecuteNonQuery();
            }

            if (!TableExists(connection, "GeneratedGroupDocuments"))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "GeneratedGroupDocuments" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_GeneratedGroupDocuments" PRIMARY KEY AUTOINCREMENT,
                        "ExportTemplateId" INTEGER NULL,
                        "TemplateId" INTEGER NULL,
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
                command.ExecuteNonQuery();

                using var indexCommand = connection.CreateCommand();
                indexCommand.CommandText =
                    """CREATE INDEX "IX_GeneratedGroupDocuments_ExportTemplateId_IntakeId_IsCurrent" ON "GeneratedGroupDocuments" ("ExportTemplateId", "IntakeId", "IsCurrent");""";
                indexCommand.ExecuteNonQuery();

                using var indexCommand1b = connection.CreateCommand();
                indexCommand1b.CommandText =
                    """CREATE INDEX "IX_GeneratedGroupDocuments_TemplateId_IntakeId_IsCurrent" ON "GeneratedGroupDocuments" ("TemplateId", "IntakeId", "IsCurrent");""";
                indexCommand1b.ExecuteNonQuery();

                using var indexCommand2 = connection.CreateCommand();
                indexCommand2.CommandText =
                    """CREATE INDEX "IX_GeneratedGroupDocuments_RunId" ON "GeneratedGroupDocuments" ("RunId");""";
                indexCommand2.ExecuteNonQuery();
            }

            if (!TableExists(connection, "GeneratedGroupDocumentContents"))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "GeneratedGroupDocumentContents" (
                        "GeneratedGroupDocumentId" INTEGER NOT NULL CONSTRAINT "PK_GeneratedGroupDocumentContents" PRIMARY KEY,
                        "Content" BLOB NOT NULL,
                        CONSTRAINT "FK_GeneratedGroupDocumentContents_GeneratedGroupDocuments_GeneratedGroupDocumentId"
                            FOREIGN KEY ("GeneratedGroupDocumentId") REFERENCES "GeneratedGroupDocuments" ("Id") ON DELETE CASCADE
                    );
                    """;
                command.ExecuteNonQuery();
            }
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }

    // GeneratedGroupDocuments (до v12) мала ExportTemplateId INTEGER NOT NULL і без
    // TemplateId — лише XLSX-відомості. Груповий DOCX вимагає, щоб рівно одне з двох
    // полів було заповнене, тобто ExportTemplateId має стати nullable. SQLite не вміє
    // ALTER COLUMN, тому перебудовуємо таблицю за офіційно рекомендованою процедурою
    // (create-copy-drop-rename), з вимкненими на час операції foreign keys — інакше
    // DROP TABLE з увімкненим PRAGMA foreign_keys каскадно видалить вміст із
    // GeneratedGroupDocumentContents (ON DELETE CASCADE спрацьовує і на DROP TABLE).
    // Наявність колонки TemplateId — ознака того, що таблиця вже в кінцевому вигляді
    // (і для щойно створених БД, де EnsureGroupDocumentTables одразу створює її
    // правильно, і для вже мігрованих) — тоді нічого не робимо.
    private static void MigrateGeneratedGroupDocumentsForDocxSupport(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();

        try
        {
            MigrateGeneratedGroupDocumentsForDocxSupport(connection);
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }

    // Винесено окремо від AppDbContext-обгортки, щоб можна було перевірити юніт-тестом
    // на звичайному (незашифрованому) SQLite-з'єднанні — сам SQL не залежить від SQLCipher.
    internal static void MigrateGeneratedGroupDocumentsForDocxSupport(System.Data.Common.DbConnection connection)
    {
        if (!TableExists(connection, "GeneratedGroupDocuments")) return;
        if (GetExistingColumns(connection, "GeneratedGroupDocuments").Contains("TemplateId")) return;

        using (var pragmaOff = connection.CreateCommand())
        {
            pragmaOff.CommandText = "PRAGMA foreign_keys=OFF;";
            pragmaOff.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            Exec(connection, transaction, """
                CREATE TABLE "GeneratedGroupDocuments_New" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_GeneratedGroupDocuments" PRIMARY KEY AUTOINCREMENT,
                    "ExportTemplateId" INTEGER NULL,
                    "TemplateId" INTEGER NULL,
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
                """);

            Exec(connection, transaction, """
                INSERT INTO "GeneratedGroupDocuments_New"
                    ("Id", "ExportTemplateId", "TemplateId", "RunId", "IntakeId", "GeneratedAt", "GeneratedByUserId",
                     "FileName", "ContentHash", "RosterHash", "SizeBytes", "RecipientCount", "Version", "IsCurrent",
                     "HasContent", "DeletedAt", "DeletedBy")
                SELECT "Id", "ExportTemplateId", NULL, "RunId", "IntakeId", "GeneratedAt", "GeneratedByUserId",
                       "FileName", "ContentHash", "RosterHash", "SizeBytes", "RecipientCount", "Version", "IsCurrent",
                       "HasContent", "DeletedAt", "DeletedBy"
                FROM "GeneratedGroupDocuments";
                """);

            Exec(connection, transaction, """DROP TABLE "GeneratedGroupDocuments";""");
            Exec(connection, transaction, """ALTER TABLE "GeneratedGroupDocuments_New" RENAME TO "GeneratedGroupDocuments";""");

            Exec(connection, transaction,
                """CREATE INDEX "IX_GeneratedGroupDocuments_ExportTemplateId_IntakeId_IsCurrent" ON "GeneratedGroupDocuments" ("ExportTemplateId", "IntakeId", "IsCurrent");""");
            Exec(connection, transaction,
                """CREATE INDEX "IX_GeneratedGroupDocuments_TemplateId_IntakeId_IsCurrent" ON "GeneratedGroupDocuments" ("TemplateId", "IntakeId", "IsCurrent");""");
            Exec(connection, transaction,
                """CREATE INDEX "IX_GeneratedGroupDocuments_RunId" ON "GeneratedGroupDocuments" ("RunId");""");

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
        finally
        {
            using var pragmaOn = connection.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys=ON;";
            pragmaOn.ExecuteNonQuery();
        }
    }

    private static void Exec(System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void EnsureStaffTables(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();

        try
        {
            if (!TableExists(connection, "StaffEvents"))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "StaffEvents" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_StaffEvents" PRIMARY KEY AUTOINCREMENT,
                        "RecipientId" INTEGER NOT NULL,
                        "Kind" INTEGER NOT NULL,
                        "DateStart" TEXT NOT NULL,
                        "DateEnd" TEXT NOT NULL,
                        "Note" TEXT NULL,
                        "CreatedAt" TEXT NOT NULL,
                        "CreatedBy" TEXT NULL,
                        CONSTRAINT "FK_StaffEvents_Recipients_RecipientId"
                            FOREIGN KEY ("RecipientId") REFERENCES "Recipients" ("Id") ON DELETE CASCADE
                    );
                    """;
                command.ExecuteNonQuery();

                using var indexCommand = connection.CreateCommand();
                indexCommand.CommandText =
                    """CREATE INDEX "IX_StaffEvents_RecipientId" ON "StaffEvents" ("RecipientId");""";
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
