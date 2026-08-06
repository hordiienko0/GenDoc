using System.IO;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Templates;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services
{
    public class ExportTemplateService : IExportTemplateService
    {
        public const string BuiltInTemplateName = "Анкетні дані (прикомандировані)";

        private static readonly Regex PlaceholderRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);

        private static readonly string[] BuiltInHeaders =
        {
            "№ з/п",
            "Військове звання",
            "ПІБ",
            "Дата \nнародження",
            "Національність",
            "ВОС на який навчається",
            "З якого часу прибув на курси П та ПК",
            "Сімейний стан\n (Контакт близької людини)",
            "Адреса реєстрації",
            "Адреса фактичного проживання",
            "телефон",
            "Примітка",
            "Група",
            "ПІБ на іноземній мові",
            "Служив/не служив",
            "Примітка",
            "безпосередній командир ПІП та номер телефону",
            "№ посвідчення про відрядження",
            "Прод аттестат",
            "номер посвідчення офіцера/військового квитка",
            "ВЛК, №, дата",
            "Висновок ВЛК",
            "з якої військової частини прибув",
            "Посада",
            "Автомобіль, номер авто "
        };

        private static readonly ExportFieldKey[] BuiltInFieldKeys =
        {
            ExportFieldKey.RowNumber,
            ExportFieldKey.Rank,
            ExportFieldKey.FullNameFormatted,
            ExportFieldKey.DateOfBirth,
            ExportFieldKey.Nationality,
            ExportFieldKey.Vos,
            ExportFieldKey.CourseArrivalDate,
            ExportFieldKey.MaritalStatus,
            ExportFieldKey.RegistrationAddress,
            ExportFieldKey.ResidenceAddress,
            ExportFieldKey.Phone,
            ExportFieldKey.Note,
            ExportFieldKey.GroupName,
            ExportFieldKey.NameTransliterated,
            ExportFieldKey.ServedBefore,
            ExportFieldKey.ExtraNote,
            ExportFieldKey.CommanderContact,
            ExportFieldKey.TravelCertificateNumber,
            ExportFieldKey.FoodCertificate,
            ExportFieldKey.IdDocumentNumber,
            ExportFieldKey.MedicalBoard,
            ExportFieldKey.MedicalBoardConclusion,
            ExportFieldKey.OriginUnit,
            ExportFieldKey.Position,
            ExportFieldKey.Vehicle
        };

        private static readonly double[] BuiltInColumnWidths =
        {
            7.13, 16, 30.2, 14.2, 19.53, 18.66, 22.2, 37.33, 13, 13,
            16, 19.53, 13.33, 23.13, 16, 19.53, 37.33, 26.66, 13.33, 28.46,
            23.13, 13, 16, 37.33, 26.66
        };

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentUserContext _currentUserContext;

        public ExportTemplateService(
            IDbContextFactory<AppDbContext> dbFactory,
            IAuditLogService auditLogService,
            ICurrentUserContext currentUserContext)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
            _currentUserContext = currentUserContext;
        }

        public void EnsureBuiltInTemplate()
        {
            using var db = _dbFactory.CreateDbContext();
            if (db.ExportTemplates.Any(t => t.IsBuiltIn && t.Name == BuiltInTemplateName)) return;

            var template = new ExportTemplate
            {
                Name = BuiltInTemplateName,
                OriginalFileName = "анкетні_дані_шаблон.xlsx",
                Content = BuildBuiltInWorkbookBytes(),
                IsBuiltIn = true,
                UploadedAt = DateTime.Now
            };

            for (var i = 0; i < BuiltInHeaders.Length; i++)
            {
                template.ColumnMappings.Add(new ExportTemplateColumnMapping
                {
                    ColumnIndex = i + 1,
                    HeaderText = BuiltInHeaders[i],
                    FieldKey = BuiltInFieldKeys[i].ToString()
                });
            }

            db.ExportTemplates.Add(template);
            db.SaveChanges();
        }

        public List<(int Id, string Name)> GetTemplates()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.ExportTemplates
                .OrderByDescending(t => t.IsBuiltIn)
                .ThenBy(t => t.Name)
                .Select(t => new { t.Id, t.Name })
                .AsEnumerable()
                .Select(t => (t.Id, t.Name))
                .ToList();
        }

        public List<(int Id, string Name, string OriginalFileName, DateTime UploadedAt, bool IsBuiltIn, bool UsesPlaceholders, int TagCount, bool RepeatSheetPerDate)> GetTemplateListItems()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.ExportTemplates
                .OrderByDescending(t => t.IsBuiltIn)
                .ThenBy(t => t.Name)
                .Select(t => new
                {
                    t.Id, t.Name, t.OriginalFileName, t.UploadedAt, t.IsBuiltIn, t.UsesPlaceholders, t.RepeatSheetPerDate,
                    TagCount = t.ColumnMappings.Count(m => m.PlaceholderTag != "")
                })
                .AsEnumerable()
                .Select(t => (t.Id, t.Name, t.OriginalFileName, t.UploadedAt, t.IsBuiltIn, t.UsesPlaceholders, t.TagCount, t.RepeatSheetPerDate))
                .ToList();
        }

        public void SetRepeatSheetPerDate(int templateId, bool value)
        {
            using var db = _dbFactory.CreateDbContext();
            var template = db.ExportTemplates.FirstOrDefault(t => t.Id == templateId);
            if (template is null) return;

            template.RepeatSheetPerDate = value;
            db.SaveChanges();
        }

        public List<(int Id, int ColumnIndex, string HeaderText, string FieldKey, string PlaceholderTag, MappingSourceType SourceType)> GetMappings(int templateId)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.ExportTemplateColumnMappings
                .Where(m => m.ExportTemplateId == templateId)
                .OrderBy(m => m.ColumnIndex)
                .Select(m => new { m.Id, m.ColumnIndex, m.HeaderText, m.FieldKey, m.PlaceholderTag, m.SourceType })
                .AsEnumerable()
                .Select(m => (m.Id, m.ColumnIndex, m.HeaderText, m.FieldKey, m.PlaceholderTag, m.SourceType))
                .ToList();
        }

        public List<string> GetManualTags(int templateId)
        {
            using var db = _dbFactory.CreateDbContext();
            return db.ExportTemplateColumnMappings
                .Where(m => m.ExportTemplateId == templateId && m.SourceType == MappingSourceType.Manual)
                .Select(m => m.PlaceholderTag)
                .Distinct()
                .ToList();
        }

        public void SaveMappings(int templateId, List<(int ColumnIndex, string FieldKey)> mappings)
        {
            using var db = _dbFactory.CreateDbContext();
            var existing = db.ExportTemplateColumnMappings
                .Where(m => m.ExportTemplateId == templateId)
                .ToList();

            var oldSnapshot = string.Join(", ", existing.OrderBy(m => m.ColumnIndex).Select(m => $"{m.ColumnIndex}:{m.FieldKey}"));

            foreach (var (columnIndex, fieldKey) in mappings)
            {
                var mapping = existing.FirstOrDefault(m => m.ColumnIndex == columnIndex);
                if (mapping is not null) mapping.FieldKey = fieldKey;
            }

            var newSnapshot = string.Join(", ", existing.OrderBy(m => m.ColumnIndex).Select(m => $"{m.ColumnIndex}:{m.FieldKey}"));

            _auditLogService.LogUpdate(db, "ExportTemplate", templateId, oldSnapshot, newSnapshot, "Оновлено мапінг колонок");
            db.SaveChanges();
        }

        public void SavePlaceholderMappings(int templateId, List<(int Id, MappingSourceType SourceType, string? FieldKey)> mappings)
        {
            using var db = _dbFactory.CreateDbContext();
            var existing = db.ExportTemplateColumnMappings.Where(m => m.ExportTemplateId == templateId).ToList();

            var oldSnapshot = string.Join(", ", existing.OrderBy(m => m.ColumnIndex).Select(m => $"{m.PlaceholderTag}:{m.SourceType}/{m.FieldKey}"));

            foreach (var (id, sourceType, fieldKey) in mappings)
            {
                var mapping = existing.FirstOrDefault(m => m.Id == id);
                if (mapping is null) continue;

                mapping.SourceType = sourceType;
                mapping.FieldKey = sourceType == MappingSourceType.Manual ? string.Empty : fieldKey ?? string.Empty;
            }

            var newSnapshot = string.Join(", ", existing.OrderBy(m => m.ColumnIndex).Select(m => $"{m.PlaceholderTag}:{m.SourceType}/{m.FieldKey}"));

            _auditLogService.LogUpdate(db, "ExportTemplate", templateId, oldSnapshot, newSnapshot, "Оновлено мапінг тегів");
            db.SaveChanges();
        }

        public void UploadTemplate(string filePath)
        {
            using var db = _dbFactory.CreateDbContext();

            var template = new ExportTemplate
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                OriginalFileName = Path.GetFileName(filePath),
                Content = File.ReadAllBytes(filePath),
                IsBuiltIn = false,
                UploadedAt = DateTime.Now
            };

            using (var workbook = new XLWorkbook(filePath))
            {
                var sheet = workbook.Worksheets.First();
                var usedRange = sheet.RangeUsed();
                if (usedRange is null)
                {
                    db.ExportTemplates.Add(template);
                    db.SaveChanges();
                    _auditLogService.LogCreate(db, "ExportTemplate", template.Id, template.Name, $"Завантажено файл {template.OriginalFileName}");
                    db.SaveChanges();
                    return;
                }

                var templateRowIndex = FindTemplateRow(usedRange);

                if (templateRowIndex is null)
                {
                    BuildHeaderRowMappings(template, usedRange);
                }
                else
                {
                    template.UsesPlaceholders = true;
                    template.TemplateRowIndex = templateRowIndex.Value;
                    BuildPlaceholderMappings(template, usedRange, templateRowIndex.Value);
                }
            }

            db.ExportTemplates.Add(template);
            db.SaveChanges();

            _auditLogService.LogCreate(db, "ExportTemplate", template.Id, template.Name,
                $"Завантажено файл {template.OriginalFileName}" + (template.UsesPlaceholders ? $", рядок-шаблон {template.TemplateRowIndex}" : string.Empty));
            db.SaveChanges();
        }

        // Рядок-шаблон — той, де знайдено більше одного різного тегу; якщо теги
        // трапляються лише в одному рядку взагалі (навіть по одному на клітинку),
        // цей єдиний рядок і є шаблонним. Немає жодного {{тегу}} — не placeholder-шаблон.
        private static int? FindTemplateRow(IXLRange usedRange)
        {
            var tagsByRow = new SortedDictionary<int, HashSet<string>>();

            foreach (var cell in usedRange.CellsUsed())
            {
                var text = cell.GetString();
                if (!text.Contains("{{")) continue;

                foreach (Match match in PlaceholderRegex.Matches(text))
                {
                    var row = cell.Address.RowNumber;
                    if (!tagsByRow.TryGetValue(row, out var set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        tagsByRow[row] = set;
                    }
                    set.Add(match.Value);
                }
            }

            if (tagsByRow.Count == 0) return null;

            var multiTagRow = tagsByRow.FirstOrDefault(kv => kv.Value.Count > 1);
            if (multiTagRow.Value is not null) return multiTagRow.Key;

            return tagsByRow.Keys.First();
        }

        private static void BuildPlaceholderMappings(ExportTemplate template, IXLRange usedRange, int templateRowIndex)
        {
            var outsideTags = new HashSet<string>(StringComparer.Ordinal);

            foreach (var cell in usedRange.CellsUsed())
            {
                var text = cell.GetString();
                if (!text.Contains("{{")) continue;

                var row = cell.Address.RowNumber;

                foreach (Match match in PlaceholderRegex.Matches(text))
                {
                    var tag = match.Value;

                    if (row == templateRowIndex)
                    {
                        var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
                        template.ColumnMappings.Add(new ExportTemplateColumnMapping
                        {
                            ColumnIndex = cell.Address.ColumnNumber,
                            HeaderText = string.Empty,
                            FieldKey = fieldName ?? string.Empty,
                            PlaceholderTag = tag,
                            SourceType = sourceType
                        });
                    }
                    else
                    {
                        outsideTags.Add(tag);
                    }
                }
            }

            foreach (var tag in outsideTags)
            {
                var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
                template.ColumnMappings.Add(new ExportTemplateColumnMapping
                {
                    ColumnIndex = 0,
                    HeaderText = string.Empty,
                    FieldKey = fieldName ?? string.Empty,
                    PlaceholderTag = tag,
                    SourceType = sourceType
                });
            }
        }

        private static void BuildHeaderRowMappings(ExportTemplate template, IXLRange usedRange)
        {
            var headerRow = usedRange.FirstRow();
            var columnCount = usedRange.ColumnCount();
            var noteAssigned = false;

            for (var c = 1; c <= columnCount; c++)
            {
                var header = headerRow.Cell(c).GetString().Trim();
                var fieldKey = AutoMapExportHeader(header, ref noteAssigned);
                template.ColumnMappings.Add(new ExportTemplateColumnMapping
                {
                    ColumnIndex = c,
                    HeaderText = header,
                    FieldKey = fieldKey.ToString()
                });
            }
        }

        public void Delete(int templateId)
        {
            using var db = _dbFactory.CreateDbContext();
            var template = db.ExportTemplates.First(t => t.Id == templateId);
            if (template.IsBuiltIn) return;

            var snapshot = template.Name;
            template.DeletedAt = DateTime.Now;
            template.DeletedBy = _currentUserContext.CurrentUserFullName;

            _auditLogService.LogDelete(db, "ExportTemplate", template.Id, snapshot);
            db.SaveChanges();
        }

        private static ExportFieldKey AutoMapExportHeader(string header, ref bool noteAssigned)
        {
            var normalized = HeaderNormalization.Normalize(header);

            if (normalized.Contains("№ з/п")) return ExportFieldKey.RowNumber;

            if (normalized.Contains("іноземній мові")) return ExportFieldKey.NameTransliterated;
            if (normalized.Contains("піб")) return ExportFieldKey.FullNameFormatted;

            if (normalized.Contains("прізвище")) return ExportFieldKey.LastName;
            if (normalized.Contains("по батькові")) return ExportFieldKey.MiddleName;
            if (normalized.Contains("імя")) return ExportFieldKey.FirstName;

            if (normalized.Contains("звання")) return ExportFieldKey.Rank;
            if (normalized.Contains("національність")) return ExportFieldKey.Nationality;
            if (normalized.Contains("вос")) return ExportFieldKey.Vos;
            if (normalized.Contains("прибув на курси")) return ExportFieldKey.CourseArrivalDate;
            if (normalized.Contains("сімейний стан")) return ExportFieldKey.MaritalStatus;
            if (normalized.Contains("адреса реєстрації")) return ExportFieldKey.RegistrationAddress;
            if (normalized.Contains("фактичного проживання")) return ExportFieldKey.ResidenceAddress;

            if (normalized.Contains("командир")) return ExportFieldKey.CommanderContact;
            if (normalized.Contains("телефон")) return ExportFieldKey.Phone;

            if (normalized.Contains("примітка"))
            {
                if (noteAssigned) return ExportFieldKey.ExtraNote;
                noteAssigned = true;
                return ExportFieldKey.Note;
            }

            if (normalized.Contains("група")) return ExportFieldKey.GroupName;
            if (normalized.Contains("служив")) return ExportFieldKey.ServedBefore;

            if (normalized.Contains("посвідчення про відрядження")) return ExportFieldKey.TravelCertificateNumber;
            if (normalized.Contains("прод")) return ExportFieldKey.FoodCertificate;
            if (normalized.Contains("посвідчення офіцера") || normalized.Contains("військового квитка"))
                return ExportFieldKey.IdDocumentNumber;

            if (normalized.Contains("висновок влк")) return ExportFieldKey.MedicalBoardConclusion;
            if (normalized.Contains("влк")) return ExportFieldKey.MedicalBoard;

            if (normalized.Contains("з якої військової частини")) return ExportFieldKey.OriginUnit;

            if (normalized.Contains("посада")) return ExportFieldKey.Position;
            if (normalized.Contains("автомобіль")) return ExportFieldKey.Vehicle;

            if (normalized.Contains("підрозділ")) return ExportFieldKey.UnitName;
            if (normalized.Contains("особовий номер")) return ExportFieldKey.ServiceNumber;
            if (normalized.Contains("дата народження")) return ExportFieldKey.DateOfBirth;
            if (normalized.Contains("кімната")) return ExportFieldKey.RoomDisplay;

            return ExportFieldKey.Empty;
        }

        private static byte[] BuildBuiltInWorkbookBytes()
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("ППО ПС");

            for (var i = 0; i < BuiltInHeaders.Length; i++)
            {
                var cell = sheet.Cell(1, i + 1);
                cell.Value = BuiltInHeaders[i];
                cell.Style.Font.FontName = "Times New Roman";
                cell.Style.Font.FontSize = 11;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.RightBorder = XLBorderStyleValues.Thin;

                sheet.Column(i + 1).Width = BuiltInColumnWidths[i];
            }

            sheet.Row(1).Height = 27.75;

            using var memoryStream = new MemoryStream();
            workbook.SaveAs(memoryStream);
            return memoryStream.ToArray();
        }
    }
}
