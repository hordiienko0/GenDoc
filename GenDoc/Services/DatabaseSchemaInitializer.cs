using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services;

public class DatabaseSchemaInitializer : IDatabaseSchemaInitializer
{
    private const int CurrentSchemaVersion = 26;

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

    private static readonly (string Name, string Type)[] RecipientColumnsV17 =
    {
        ("IsCourseOfficer", "INTEGER NOT NULL DEFAULT 0")
    };

    private static readonly (string Name, string Type)[] RecipientColumnsV18 =
    {
        ("AssignedVehicleId", "INTEGER")
    };

    private static readonly (string Name, string Type)[] UserSettingsColumnsV26 =
    {
        ("UserProfileId", "INTEGER NOT NULL DEFAULT 0"),
        ("ActiveIntakeId", "INTEGER"), ("LastPackageId", "INTEGER"),
        ("ArchiveMineOnly", "INTEGER NOT NULL DEFAULT 0"),
        ("LastManualValuesJson", "TEXT"), ("LastSignerByTemplateJson", "TEXT")
    };

    private static readonly (string Name, string Type)[] WeaponColumnsV18 =
    {
        ("RecipientId", "INTEGER NOT NULL DEFAULT 0"),
        ("Name", "TEXT NOT NULL DEFAULT ''"),
        ("SerialNumber", "TEXT NOT NULL DEFAULT ''"),
        ("RawText", "TEXT"), ("IssuedAt", "TEXT"), ("Note", "TEXT"),
        ("DeletedAt", "TEXT"), ("DeletedBy", "TEXT")
    };

    private static readonly (string Name, string Type)[] VehicleColumnsV18 =
    {
        ("Model", "TEXT NOT NULL DEFAULT ''"), ("PlateNumber", "TEXT NOT NULL DEFAULT ''"),
        ("Note", "TEXT"), ("DeletedAt", "TEXT"), ("DeletedBy", "TEXT")
    };

    private static readonly (string Name, string Type)[] ExportTemplateColumnsV19 =
    {
        ("RepeatSheetPerDate", "INTEGER NOT NULL DEFAULT 0")
    };

    internal static readonly (string Name, string Type)[] TemplateColumnsV21 =
    {
        ("BuilderJson", "TEXT")
    };

    internal static readonly (string Name, string Type)[] ExportTemplateColumnsV22 =
    {
        ("BuilderJson", "TEXT")
    };

    internal static readonly (string Name, string Type)[] TemplateColumnsV23 =
    {
        ("Audience", "INTEGER NOT NULL DEFAULT 0")
    };

    internal static readonly (string Name, string Type)[] AppSettingsColumnsV24 =
    {
        ("DefaultOutputFolder", "TEXT")
    };

    private static readonly (string Name, string Type)[] AppSettingsColumnsV15 =
    {
        ("LastSignerByTemplateJson", "TEXT")
    };

    private static readonly (string Name, string Type)[] TemplateFieldMappingColumnsV16 =
    {
        ("DateFormat", "TEXT")
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

            if (currentVersion < 16)
            {
                AddMissingColumns(db, "TemplateFieldMappings", TemplateFieldMappingColumnsV16);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 16,
                    AppliedAt = DateTime.Now,
                    Description = "Шаблони: обраний формат дати для мапінгу дато-полів"
                });
                currentVersion = 16;
            }

            if (currentVersion < 17)
            {
                AddMissingColumns(db, "Recipients", RecipientColumnsV17);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 17,
                    AppliedAt = DateTime.Now,
                    Description = "Постійний склад: ознака «курсовий офіцер»"
                });
                currentVersion = 17;
            }

            if (currentVersion < 18)
            {
                EnsureWeaponVehicleTables(db);
                AddMissingColumns(db, "Recipients", RecipientColumnsV18);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 18,
                    AppliedAt = DateTime.Now,
                    Description = "Окремі моделі зброї та автомобіля, закріплених за людиною"
                });
                currentVersion = 18;
            }

            if (currentVersion < 19)
            {
                AddMissingColumns(db, "ExportTemplates", ExportTemplateColumnsV19);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 19,
                    AppliedAt = DateTime.Now,
                    Description = "Xlsx-шаблони: аркуш-на-дату (RepeatSheetPerDate)"
                });
                currentVersion = 19;
            }

            if (currentVersion < 20)
            {
                NormalizeTemplateNames(db);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 20,
                    AppliedAt = DateTime.Now,
                    Description = "Назви шаблонів без технічного префікса «Шаблон_» і підкреслень"
                });
                currentVersion = 20;
            }

            if (currentVersion < 21)
            {
                AddMissingColumns(db, "Templates", TemplateColumnsV21);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 21,
                    AppliedAt = DateTime.Now,
                    Description = "Конструктор шаблонів: джерело блоків у Template.BuilderJson"
                });
                currentVersion = 21;
            }

            if (currentVersion < 22)
            {
                AddMissingColumns(db, "ExportTemplates", ExportTemplateColumnsV22);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 22,
                    AppliedAt = DateTime.Now,
                    Description = "Конструктор відомостей: джерело блоків у ExportTemplate.BuilderJson"
                });
                currentVersion = 22;
            }

            if (currentVersion < 23)
            {
                AddMissingColumns(db, "Templates", TemplateColumnsV23);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 23,
                    AppliedAt = DateTime.Now,
                    Description = "Поділ шаблонів на набори й постійний склад: Template.Audience"
                });
                currentVersion = 23;
            }

            if (currentVersion < 24)
            {
                AddMissingColumns(db, "AppSettings", AppSettingsColumnsV24);
                AddMissingColumns(db, "GenerationPackageRuns", RunColumnsV6);
                MigrateGenerationPackageRunsForAdHocRuns(db);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 24,
                    AppliedAt = DateTime.Now,
                    Description = "Тека генерації за замовчуванням; запуски без пакета (вибіркова генерація)"
                });
                currentVersion = 24;
            }

            if (currentVersion < 25)
            {
                EnsureGroupDocumentRecipientsTable(db);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 25,
                    AppliedAt = DateTime.Now,
                    Description = "Учасники групового документа: склад на момент генерації"
                });
                currentVersion = 25;
            }

            if (currentVersion < 26)
            {
                EnsureUserSettingsTable(db);

                db.SchemaVersions.Add(new SchemaVersion
                {
                    Version = 26,
                    AppliedAt = DateTime.Now,
                    Description = "Пер-профільний стан користувача: мій набір, архів «Мої», дані з минулого разу"
                });
                currentVersion = 26;
            }
        }

        EnsureOrgTables(db);

        AddMissingColumns(db, "Recipients", QuestionnaireColumns);
        AddMissingColumns(db, "OrganizationSettings", OrganizationSettingsColumnsV3);
        AddMissingColumns(db, "Rooms", RoomColumnsV4);

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
        EnsureGroupDocumentRecipientsTable(db);
        EnsureUserSettingsTable(db);

        AddMissingColumns(db, "Templates", TemplateColumnsV12);
        AddMissingColumns(db, "TemplateFieldMappings", TemplateFieldMappingColumnsV12);
        MigrateGeneratedGroupDocumentsForDocxSupport(db);
        BackfillRunIntakeIds(db);

        AddMissingColumns(db, "AppSettings", AppSettingsColumnsV13);
        AddMissingColumns(db, "Recipients", RecipientColumnsV14);
        AddMissingColumns(db, "Recipients", RecipientColumnsV15);
        AddMissingColumns(db, "AppSettings", AppSettingsColumnsV15);
        AddMissingColumns(db, "TemplateFieldMappings", TemplateFieldMappingColumnsV16);
        AddMissingColumns(db, "Recipients", RecipientColumnsV17);

        EnsureWeaponVehicleTables(db);
        AddMissingColumns(db, "Recipients", RecipientColumnsV18);

        AddMissingColumns(db, "ExportTemplates", ExportTemplateColumnsV19);

        AddMissingColumns(db, "Templates", TemplateColumnsV21);
        AddMissingColumns(db, "ExportTemplates", ExportTemplateColumnsV22);
        AddMissingColumns(db, "Templates", TemplateColumnsV23);
        AddMissingColumns(db, "AppSettings", AppSettingsColumnsV24);
        MigrateGenerationPackageRunsForAdHocRuns(db);

        EnsureRoomUniqueIndex(db);

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

    internal static void AddMissingColumns(
        System.Data.Common.DbConnection connection, string tableName, (string Name, string Type)[] columns)
    {
        var existingColumns = GetExistingColumns(connection, tableName);

        foreach (var (name, type) in columns)
        {
            if (existingColumns.Contains(name)) continue;
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE " + tableName + " ADD COLUMN " + name + " " + type + ";";
            command.ExecuteNonQuery();
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

    private static void EnsureGroupDocumentRecipientsTable(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();
        try { EnsureGroupDocumentRecipientsTable(connection); }
        finally { if (wasClosed) connection.Close(); }
    }

    internal static void EnsureGroupDocumentRecipientsTable(System.Data.Common.DbConnection connection)
    {
        if (TableExists(connection, "GeneratedGroupDocumentRecipients")) return;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE "GeneratedGroupDocumentRecipients" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_GeneratedGroupDocumentRecipients" PRIMARY KEY AUTOINCREMENT,
                    "GeneratedGroupDocumentId" INTEGER NOT NULL,
                    "RecipientId" INTEGER NOT NULL,
                    CONSTRAINT "FK_GeneratedGroupDocumentRecipients_GeneratedGroupDocuments_GeneratedGroupDocumentId"
                        FOREIGN KEY ("GeneratedGroupDocumentId") REFERENCES "GeneratedGroupDocuments" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_GeneratedGroupDocumentRecipients_Recipients_RecipientId"
                        FOREIGN KEY ("RecipientId") REFERENCES "Recipients" ("Id") ON DELETE CASCADE
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var index = connection.CreateCommand())
        {
            index.CommandText = """
                CREATE INDEX "IX_GeneratedGroupDocumentRecipients_GeneratedGroupDocumentId"
                    ON "GeneratedGroupDocumentRecipients" ("GeneratedGroupDocumentId");
                CREATE INDEX "IX_GeneratedGroupDocumentRecipients_RecipientId"
                    ON "GeneratedGroupDocumentRecipients" ("RecipientId");
                """;
            index.ExecuteNonQuery();
        }
    }

    private static void EnsureUserSettingsTable(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();
        try { EnsureUserSettingsTable(connection); }
        finally { if (wasClosed) connection.Close(); }
    }

    internal static void EnsureUserSettingsTable(System.Data.Common.DbConnection connection)
    {
        if (!TableExists(connection, "UserSettings"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE "UserSettings" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserSettings" PRIMARY KEY AUTOINCREMENT,
                    "UserProfileId" INTEGER NOT NULL,
                    "ActiveIntakeId" INTEGER NULL,
                    "LastPackageId" INTEGER NULL,
                    "ArchiveMineOnly" INTEGER NOT NULL DEFAULT 0,
                    "LastManualValuesJson" TEXT NULL,
                    "LastSignerByTemplateJson" TEXT NULL,
                    CONSTRAINT "FK_UserSettings_Users_UserProfileId"
                        FOREIGN KEY ("UserProfileId") REFERENCES "Users" ("Id") ON DELETE CASCADE
                );
                """;
            command.ExecuteNonQuery();
        }

        AddMissingColumns(connection, "UserSettings", UserSettingsColumnsV26);

        using var index = connection.CreateCommand();
        index.CommandText =
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserSettings_UserProfileId" ON "UserSettings" ("UserProfileId");""";
        index.ExecuteNonQuery();
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

    private static void NormalizeTemplateNames(AppDbContext db)
    {
        foreach (var template in db.Templates.IgnoreQueryFilters().ToList())
        {
            var cleaned = TemplateNaming.Clean(template.Name);
            if (cleaned != template.Name) template.Name = cleaned;
        }

        foreach (var exportTemplate in db.ExportTemplates.IgnoreQueryFilters().ToList())
        {
            var cleaned = TemplateNaming.Clean(exportTemplate.Name);
            if (cleaned != exportTemplate.Name) exportTemplate.Name = cleaned;
        }

        db.SaveChanges();
    }

    private static void EnsureWeaponVehicleTables(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();

        try
        {
            EnsureWeaponVehicleTables(connection);
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }

    internal static void EnsureWeaponVehicleTables(System.Data.Common.DbConnection connection)
    {
        if (!TableExists(connection, "Weapons"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                    CREATE TABLE "Weapons" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_Weapons" PRIMARY KEY AUTOINCREMENT,
                        "RecipientId" INTEGER NOT NULL,
                        "Name" TEXT NOT NULL,
                        "SerialNumber" TEXT NOT NULL,
                        "RawText" TEXT NULL,
                        "IssuedAt" TEXT NULL,
                        "Note" TEXT NULL,
                        "DeletedAt" TEXT NULL,
                        "DeletedBy" TEXT NULL,
                        CONSTRAINT "FK_Weapons_Recipients_RecipientId"
                            FOREIGN KEY ("RecipientId") REFERENCES "Recipients" ("Id") ON DELETE CASCADE
                    );
                    """;
            command.ExecuteNonQuery();

            using var indexCommand = connection.CreateCommand();
            indexCommand.CommandText =
                """CREATE INDEX "IX_Weapons_RecipientId" ON "Weapons" ("RecipientId");""";
            indexCommand.ExecuteNonQuery();
        }

        if (!TableExists(connection, "Vehicles"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                    CREATE TABLE "Vehicles" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_Vehicles" PRIMARY KEY AUTOINCREMENT,
                        "Model" TEXT NOT NULL,
                        "PlateNumber" TEXT NOT NULL,
                        "Note" TEXT NULL,
                        "DeletedAt" TEXT NULL,
                        "DeletedBy" TEXT NULL
                    );
                    """;
            command.ExecuteNonQuery();
        }

        AddMissingColumns(connection, "Weapons", WeaponColumnsV18);
        AddMissingColumns(connection, "Vehicles", VehicleColumnsV18);
    }

    private static void MigrateGenerationPackageRunsForAdHocRuns(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();
        try { MigrateGenerationPackageRunsForAdHocRuns(connection); }
        finally { if (wasClosed) connection.Close(); }
    }

    internal static bool ColumnIsNotNull(System.Data.Common.DbConnection connection, string table, string column)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(reader.GetOrdinal("name")), column, StringComparison.OrdinalIgnoreCase))
                return reader.GetInt64(reader.GetOrdinal("notnull")) == 1;
        }
        return false;
    }

    internal static void MigrateGenerationPackageRunsForAdHocRuns(System.Data.Common.DbConnection connection)
    {
        if (!TableExists(connection, "GenerationPackageRuns")) return;
        if (!ColumnIsNotNull(connection, "GenerationPackageRuns", "GenerationPackageId")) return;

        using (var pragmaOff = connection.CreateCommand())
        {
            pragmaOff.CommandText = "PRAGMA foreign_keys=OFF;";
            pragmaOff.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            Exec(connection, transaction, """
                CREATE TABLE "GenerationPackageRuns_New" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_GenerationPackageRuns" PRIMARY KEY AUTOINCREMENT,
                    "GenerationPackageId" INTEGER NULL,
                    "RunAt" TEXT NOT NULL,
                    "RunByUserId" INTEGER NOT NULL,
                    "GeneratedCount" INTEGER NOT NULL DEFAULT 0,
                    "SkippedCount" INTEGER NOT NULL DEFAULT 0,
                    "ErrorCount" INTEGER NOT NULL DEFAULT 0,
                    "Summary" TEXT NULL,
                    "IntakeId" INTEGER NULL,
                    "BranchName" TEXT NULL,
                    CONSTRAINT "FK_GenerationPackageRuns_GenerationPackages_GenerationPackageId" FOREIGN KEY ("GenerationPackageId") REFERENCES "GenerationPackages" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_GenerationPackageRuns_Users_RunByUserId" FOREIGN KEY ("RunByUserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
                );
                """);
            Exec(connection, transaction, """
                INSERT INTO "GenerationPackageRuns_New"
                    ("Id","GenerationPackageId","RunAt","RunByUserId","GeneratedCount","SkippedCount","ErrorCount","Summary","IntakeId","BranchName")
                SELECT "Id","GenerationPackageId","RunAt","RunByUserId","GeneratedCount","SkippedCount","ErrorCount","Summary","IntakeId","BranchName"
                FROM "GenerationPackageRuns";
                """);
            Exec(connection, transaction, """DROP TABLE "GenerationPackageRuns";""");
            Exec(connection, transaction, """ALTER TABLE "GenerationPackageRuns_New" RENAME TO "GenerationPackageRuns";""");
            Exec(connection, transaction, """CREATE INDEX "IX_GenerationPackageRuns_GenerationPackageId" ON "GenerationPackageRuns" ("GenerationPackageId");""");
            Exec(connection, transaction, """CREATE INDEX "IX_GenerationPackageRuns_RunByUserId" ON "GenerationPackageRuns" ("RunByUserId");""");
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

    private static void BackfillRunIntakeIds(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();
        try { BackfillRunIntakeIds(connection); }
        finally { if (wasClosed) connection.Close(); }
    }

    internal static void BackfillRunIntakeIds(System.Data.Common.DbConnection connection)
    {
        if (!TableExists(connection, "GenerationPackageRuns")) return;
        if (!GetExistingColumns(connection, "GenerationPackageRuns").Contains("IntakeId")) return;
        if (!TableExists(connection, "GeneratedDocuments")) return;
        var hasGroup = TableExists(connection, "GeneratedGroupDocuments")
            && GetExistingColumns(connection, "GeneratedGroupDocuments").Contains("RunId");

        var union = hasGroup
            ? """
              SELECT "IntakeId" FROM "GeneratedDocuments" WHERE "RunId" = "GenerationPackageRuns"."Id" AND "IntakeId" IS NOT NULL
              UNION ALL
              SELECT "IntakeId" FROM "GeneratedGroupDocuments" WHERE "RunId" = "GenerationPackageRuns"."Id" AND "IntakeId" IS NOT NULL
              """
            : """SELECT "IntakeId" FROM "GeneratedDocuments" WHERE "RunId" = "GenerationPackageRuns"."Id" AND "IntakeId" IS NOT NULL""";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            UPDATE "GenerationPackageRuns" SET "IntakeId" = (
                SELECT "IntakeId" FROM ({union}) GROUP BY "IntakeId" ORDER BY COUNT(*) DESC, "IntakeId" ASC LIMIT 1)
            WHERE "IntakeId" IS NULL
              AND EXISTS (SELECT 1 FROM ({union}));
            """;
        cmd.ExecuteNonQuery();
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
