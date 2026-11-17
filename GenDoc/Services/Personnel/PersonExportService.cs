using System.IO;
using System.Text.Json;
using ClosedXML.Excel;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Duty;
using GenDoc.Services.Intakes;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Personnel
{
    public sealed class PersonExportService : IPersonExportService
    {
        internal const string SheetName = "Особовий склад";
        private const double MinColumnWidth = 5;
        private const double MaxColumnWidth = 48;
        private const double MaxHeaderWidth = 18;

        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;
        private readonly IUserSettingsService _userSettings;

        public PersonExportService(
            IDbContextFactory<AppDbContext> dbFactory,
            IAuditLogService auditLogService,
            IUserSettingsService userSettings)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
            _userSettings = userSettings;
        }

        public async Task<IReadOnlyList<PersonExportRow>> LoadAsync(int nodeId)
        {
            using var db = _dbFactory.CreateDbContext();

            var path = await db.OrgNodes.Where(n => n.Id == nodeId).Select(n => n.Path).FirstOrDefaultAsync();
            if (path is null) return Array.Empty<PersonExportRow>();

            return (await LoadRowsAsync(db, path)).Rows;
        }

        public async Task<PersonExportTree> LoadTreeAsync()
        {
            using var db = _dbFactory.CreateDbContext();
            var (rows, nodes, intakes) = await LoadRowsAsync(db, null);
            var byId = nodes.ToDictionary(n => n.Id);

            // Людина рахується в своїй папці й у всіх вищих: відмічена папка бере вкладені разом із собою.
            var peopleByFolder = new Dictionary<int, List<int>>();
            foreach (var row in rows)
            {
                var seen = new HashSet<int>();
                for (var node = byId.GetValueOrDefault(row.Person.OrgNodeId ?? 0);
                     node is not null && seen.Add(node.Id);
                     node = node.ParentId is int parentId ? byId.GetValueOrDefault(parentId) : null)
                {
                    if (!peopleByFolder.TryGetValue(node.Id, out var ids)) peopleByFolder[node.Id] = ids = new List<int>();
                    ids.Add(row.Person.Id);
                }
            }

            var intakeByRoot = intakes.GroupBy(i => i.RootOrgNodeId).ToDictionary(g => g.Key, g => g.First().Id);
            var folders = nodes
                .OrderBy(n => n.Depth).ThenBy(n => n.SortOrder).ThenBy(n => n.Name)
                .Select(n =>
                {
                    var isRoot = intakeByRoot.TryGetValue(n.Id, out var rootOf);
                    IReadOnlyList<int> people = peopleByFolder.TryGetValue(n.Id, out var ids) ? ids : Array.Empty<int>();
                    return new DutyFolderOption(n.Id, n.ParentId, n.Name, n.IntakeId ?? (isRoot ? rootOf : null), isRoot, people);
                })
                .ToList();

            // Той самий вибір, що в «активного набору» програми: активний із найбільшим номером.
            var defaultIntake = intakes
                .Where(i => byId.ContainsKey(i.RootOrgNodeId))
                .OrderByDescending(i => i.Status == IntakeStatus.Active)
                .ThenByDescending(i => i.Number)
                .FirstOrDefault();

            return new PersonExportTree(folders, rows, defaultIntake?.RootOrgNodeId);
        }

        /// <summary>Люди гілки (<paramref name="path"/>) або всього дерева (null) разом із папками й наборами.</summary>
        private static async Task<(List<PersonExportRow> Rows, List<OrgNode> Nodes, List<Intake> Intakes)> LoadRowsAsync(
            AppDbContext db, string? path)
        {
            var query = db.Recipients.Where(r => r.OrgNode != null);
            if (path is not null) query = query.Where(r => r.OrgNode!.Path.StartsWith(path));

            var people = await query
                .Include(r => r.Room)
                .Include(r => r.Weapons)
                .AsNoTracking()
                .ToListAsync();

            // Усе дерево, а не лише гілка: повний шлях папки починається від кореня.
            var nodes = await db.OrgNodes.AsNoTracking().ToListAsync();
            var byId = nodes.ToDictionary(n => n.Id);
            var order = TreeOrder(nodes);

            var intakes = await db.Intakes.AsNoTracking().ToListAsync();
            var intakeNames = intakes.ToDictionary(i => i.Id, i => IntakeLabel.Of(i.Number, i.DisplayNumber));

            var rows = new List<PersonExportRow>(people.Count);
            foreach (var person in people)
            {
                byId.TryGetValue(person.OrgNodeId ?? 0, out var node);
                rows.Add(new PersonExportRow(
                    person,
                    node?.Name ?? string.Empty,
                    node is null ? string.Empty : OrgPathNames(node, byId),
                    node is not null && order.TryGetValue(node.Id, out var position) ? position : int.MaxValue,
                    person.IntakeId is int intakeId && intakeNames.TryGetValue(intakeId, out var name) ? name : null));
            }

            return (rows, nodes, intakes);
        }

        public Task<ExportResult> SaveXlsxAsync(
            IReadOnlyList<PersonExportRow> rows,
            IReadOnlyList<PersonExportField> fields,
            string filePath,
            string scopeTitle)
            => SaveXlsxAsync(new[] { new PersonExportSheet(SheetName, rows) }, fields, filePath, scopeTitle);

        public Task<ExportResult> SaveXlsxAsync(
            IReadOnlyList<PersonExportSheet> sheets,
            IReadOnlyList<PersonExportField> fields,
            string filePath,
            string scopeTitle)
        {
            return Task.Run(() =>
            {
                try
                {
                    // Одна людина може бути на кількох аркушах («Усі слухачі» і «Набір») - у журнал іде раз.
                    var people = sheets.SelectMany(s => s.Rows).Select(r => r.Person.Id).Distinct().Count();

                    using (var workbook = new XLWorkbook())
                    {
                        foreach (var sheet in sheets)
                            WriteSheet(workbook.Worksheets.Add(sheet.Name), PersonExportTable.Build(sheet.Rows, fields));

                        var directory = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(directory))
                            Directory.CreateDirectory(directory);

                        workbook.SaveAs(filePath);
                    }

                    using (var db = _dbFactory.CreateDbContext())
                    {
                        _auditLogService.LogExport(db, "Recipient", people,
                            ExportDetails(people, fields, scopeTitle, Path.GetFileName(filePath), sheets));
                        db.SaveChanges();
                    }

                    return new ExportResult(true, people, filePath, null);
                }
                catch (Exception ex)
                {
                    return new ExportResult(false, 0, null, ex.Message);
                }
            });
        }

        public void LogClipboardExport(int count, IReadOnlyList<PersonExportField> fields, string scopeTitle)
        {
            using var db = _dbFactory.CreateDbContext();
            _auditLogService.LogExport(db, "Recipient", count, ExportDetails(count, fields, scopeTitle, "буфер обміну"));
            db.SaveChanges();
        }

        public async Task<PersonExportPrefs> LoadPrefsAsync()
        {
            var json = (await _userSettings.GetForCurrentUserAsync()).PersonnelExportPrefsJson;
            if (string.IsNullOrWhiteSpace(json)) return new PersonExportPrefs();

            try
            {
                return JsonSerializer.Deserialize<PersonExportPrefs>(json) ?? new PersonExportPrefs();
            }
            catch (JsonException)
            {
                return new PersonExportPrefs();
            }
        }

        public Task SavePrefsAsync(PersonExportPrefs prefs)
        {
            var json = JsonSerializer.Serialize(prefs);
            return _userSettings.UpdateAsync(s => s.PersonnelExportPrefsJson = json);
        }

        internal static string ExportDetails(
            int count, IReadOnlyList<PersonExportField> fields, string scopeTitle, string target,
            IReadOnlyList<PersonExportSheet>? sheets = null)
        {
            // Один аркуш - звичайний список, про аркуші в журналі не згадуємо.
            var bySheets = sheets is { Count: > 1 }
                ? $"аркуші: {string.Join(", ", sheets.Select(s => $"{s.Name} - {s.Rows.Count}"))}, "
                : string.Empty;
            return $"Особовий склад «{scopeTitle}»: {count} осіб, {bySheets}колонки: {string.Join(", ", fields.Select(f => f.Label))} → {target}";
        }

        private static void WriteSheet(IXLWorksheet sheet, PersonExportTable table)
        {
            var columnCount = table.Headers.Count;
            var lastRow = table.Rows.Count + 1;

            for (var c = 0; c < columnCount; c++)
                sheet.Cell(1, c + 1).Value = table.Headers[c];

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var cells = table.Rows[r];
                for (var c = 0; c < columnCount; c++)
                    WriteCell(sheet.Cell(r + 2, c + 1), cells[c]);
            }

            var all = sheet.Range(1, 1, lastRow, columnCount);
            all.Style.Font.FontName = "Times New Roman";
            all.Style.Font.FontSize = 11;
            all.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            all.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            all.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            var header = sheet.Range(1, 1, 1, columnCount);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8E4E1");
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // Ширину дають дані, а не заголовок: довга назва поля над порожньою чи вузькою колонкою переноситься
            // в кілька рядків, а не розтягує її на пів аркуша. Довга адреса теж переноситься - стеля MaxColumnWidth.
            for (var c = 1; c <= columnCount; c++)
            {
                var column = sheet.Column(c);
                var hasData = table.Rows.Any(row => row[c - 1] is not null);
                if (hasData) column.AdjustToContents(2, lastRow);

                var dataWidth = hasData ? column.Width + 1 : 0;
                var headerWidth = Math.Min(table.Headers[c - 1].Length * 1.2 + 2, MaxHeaderWidth);
                column.Width = Math.Clamp(Math.Max(dataWidth, headerWidth), MinColumnWidth, MaxColumnWidth);
            }
            all.Style.Alignment.WrapText = true;

            sheet.SheetView.FreezeRows(1);
            header.SetAutoFilter();

            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
            sheet.PageSetup.FitToPages(1, 0);
            sheet.PageSetup.SetRowsToRepeatAtTop(1, 1);
            sheet.PageSetup.Margins.Left = sheet.PageSetup.Margins.Right = 0.4;
            sheet.PageSetup.Margins.Top = sheet.PageSetup.Margins.Bottom = 0.5;
        }

        private static void WriteCell(IXLCell cell, object? value)
        {
            switch (value)
            {
                case null:
                    break;
                case DateTime date:
                    cell.Value = date;
                    cell.Style.DateFormat.Format = "dd.mm.yyyy";
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    break;
                case int number:
                    cell.Value = number;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    break;
                default:
                    // Завжди текстом: особовий номер, ІПН і телефон не мають втрачати нулі на початку.
                    cell.SetValue(value.ToString() ?? string.Empty);
                    break;
            }
        }

        /// <summary>Місце папки в дереві згори донизу - як його бачать на екрані.</summary>
        private static Dictionary<int, int> TreeOrder(IReadOnlyList<OrgNode> nodes)
        {
            var known = nodes.Select(n => n.Id).ToHashSet();
            var children = nodes
                .Where(n => n.ParentId is int parentId && known.Contains(parentId))
                .ToLookup(n => n.ParentId!.Value);

            var order = new Dictionary<int, int>();

            void Visit(OrgNode node)
            {
                if (!order.TryAdd(node.Id, order.Count)) return;
                foreach (var child in children[node.Id].OrderBy(n => n.SortOrder).ThenBy(n => n.Name))
                    Visit(child);
            }

            foreach (var root in nodes
                         .Where(n => n.ParentId is not int parentId || !known.Contains(parentId))
                         .OrderBy(n => n.SortOrder).ThenBy(n => n.Name))
            {
                Visit(root);
            }

            return order;
        }

        private static string OrgPathNames(OrgNode node, IReadOnlyDictionary<int, OrgNode> byId)
        {
            var names = new List<string>();
            var seen = new HashSet<int>();
            for (var current = node; current is not null && seen.Add(current.Id);
                 current = current.ParentId is int parentId ? byId.GetValueOrDefault(parentId) : null)
            {
                names.Insert(0, current.Name);
            }

            return string.Join(" › ", names);
        }
    }
}
