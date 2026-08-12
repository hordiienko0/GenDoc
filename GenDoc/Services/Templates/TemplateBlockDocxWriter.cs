using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    /// <summary>Звання і скорочене ПІБ підписанта, розв'язані з постійного складу.</summary>
    public record SignatoryInfo(string Rank, string ShortName);

    /// <summary>
    /// Збирає .docx із блоків конструктора. Навмисно віддає звичайний шаблон із
    /// {{тегами}}: далі його підхоплює наявний конвеєр генерації, тож окремого
    /// каналу для «конструкторських» шаблонів не існує.
    /// </summary>
    public static class TemplateBlockDocxWriter
    {
        private const string SignatureRule = "_______________";

        public static byte[] Write(
            TemplateBuilderDocument document,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories = null)
        {
            using var stream = new MemoryStream();

            using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = word.AddMainDocumentPart();
                var body = new Body();

                foreach (var block in document.Blocks)
                {
                    foreach (var paragraph in Render(block, signatories))
                        body.AppendChild(paragraph);
                }

                main.Document = new Document(body);
                main.Document.Save();
            }

            return stream.ToArray();
        }

        private static IEnumerable<Paragraph> Render(
            TemplateBlock block,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories)
        {
            switch (block.Kind)
            {
                case TemplateBlockKind.Header:
                    // Гриф притиснутий праворуч; переноси рядків у тексті лишаються переносами.
                    foreach (var line in SplitLines(block.Text))
                        yield return Text(line, JustificationValues.Right);
                    break;

                case TemplateBlockKind.Title:
                    yield return Text(block.Text ?? string.Empty, JustificationValues.Center, bold: true);
                    break;

                case TemplateBlockKind.DateAndCity:
                    yield return Text(block.Text ?? string.Empty, JustificationValues.Both);
                    break;

                case TemplateBlockKind.Paragraph:
                    foreach (var line in SplitLines(block.Text))
                        yield return Text(line, JustificationValues.Both);
                    break;

                case TemplateBlockKind.Signatures:
                    foreach (var line in block.Signatures ?? Array.Empty<SignatureLine>())
                        yield return Text(RenderSignature(line, signatories), JustificationValues.Left);
                    break;

                case TemplateBlockKind.Table:
                    // Таблиця — наступний крок; поки блок не має представлення в .docx.
                    break;
            }
        }

        private static string RenderSignature(
            SignatureLine line,
            IReadOnlyDictionary<int, SignatoryInfo>? signatories)
        {
            SignatoryInfo? person = null;
            if (line.RecipientId is int id && signatories is not null)
                signatories.TryGetValue(id, out person);

            // Підписанта не обрано або його вже нема в постійному складі — лишаємо
            // порожні місця під ручний підпис, а не викидаємо рядок.
            var rank = person?.Rank ?? string.Empty;
            var name = person?.ShortName ?? string.Empty;

            var parts = new[] { $"{line.Caption}:", rank, SignatureRule, name }
                .Where(p => !string.IsNullOrWhiteSpace(p));

            return string.Join(" ", parts);
        }

        private static IEnumerable<string> SplitLines(string? text)
        {
            if (string.IsNullOrEmpty(text)) return new[] { string.Empty };
            return text.Replace("\r\n", "\n").Split('\n');
        }

        private static Paragraph Text(string text, JustificationValues justification, bool bold = false)
        {
            var runProperties = new RunProperties();
            if (bold) runProperties.AppendChild(new Bold());

            var run = new Run(runProperties);
            // Space="preserve" — інакше Word з'їдає провідні й кінцеві пробіли,
            // а в підписах вони тримають розмітку рядка.
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

            var paragraphProperties = new ParagraphProperties(
                new Justification { Val = justification });

            return new Paragraph(paragraphProperties, run);
        }
    }
}
