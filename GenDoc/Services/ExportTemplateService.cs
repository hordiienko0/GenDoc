using System.IO;
using ClosedXML.Excel;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services
{
    public class ExportTemplateService : IExportTemplateService
    {
        public const string BuiltInTemplateName = "Анкетні дані (прикомандировані)";

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

        public ExportTemplateService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
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
