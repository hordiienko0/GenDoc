using System.IO;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GenDoc.Models;

namespace GenDoc.Services.Generation
{
    public class DocumentGenerationService : IDocumentGenerationService
    {
        private static readonly Regex PlaceholderRegex = new(@"\{\{[^{}]+\}\}", RegexOptions.Compiled);

        public GenerationItemResult GenerateOne(Template template, byte[] content, IDictionary<string, string> values, string outputPath)
        {
            try
            {
                // Копія байтів — MemoryStream(byte[]) інакше пише напряму у переданий
                // масив (кеш шаблону), а його не можна мутувати між генераціями.
                using var stream = new MemoryStream();
                stream.Write(content, 0, content.Length);
                stream.Position = 0;

                var unfilled = new List<string>();

                using (var doc = WordprocessingDocument.Open(stream, true))
                {
                    var mainPart = doc.MainDocumentPart;

                    if (mainPart?.Document?.Body is not null)
                        unfilled.AddRange(ReplaceInContainer(mainPart.Document.Body, values));

                    if (mainPart is not null)
                    {
                        foreach (var header in mainPart.HeaderParts)
                            unfilled.AddRange(ReplaceInContainer(header.Header, values));

                        foreach (var footer in mainPart.FooterParts)
                            unfilled.AddRange(ReplaceInContainer(footer.Footer, values));
                    }

                    mainPart?.Document?.Save();
                }

                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                File.WriteAllBytes(outputPath, stream.ToArray());

                return new GenerationItemResult(true, null, unfilled.Distinct().ToList());
            }
            catch (Exception ex)
            {
                return new GenerationItemResult(false, ex.Message, new List<string>());
            }
        }

        private static List<string> ReplaceInContainer(OpenXmlCompositeElement? container, IDictionary<string, string> values)
        {
            var unfilled = new List<string>();
            if (container is null) return unfilled;

            foreach (var paragraph in container.Descendants<Paragraph>())
            {
                var runs = paragraph.Descendants<Run>().ToList();
                if (runs.Count == 0) continue;

                var fullText = string.Concat(runs.SelectMany(r => r.Elements<Text>().Select(t => t.Text)));
                if (!fullText.Contains("{{")) continue;

                var replaced = PlaceholderRegex.Replace(fullText, match =>
                {
                    if (values.TryGetValue(match.Value, out var value) && !string.IsNullOrEmpty(value))
                        return value;

                    unfilled.Add(match.Value);
                    return string.Empty;
                });

                ApplyToFirstRun(runs[0], replaced);

                for (var i = 1; i < runs.Count; i++)
                {
                    foreach (var text in runs[i].Elements<Text>().ToList())
                        text.Remove();
                }
            }

            return unfilled;
        }

        private static void ApplyToFirstRun(Run firstRun, string replacedText)
        {
            var existingTexts = firstRun.Elements<Text>().ToList();

            if (existingTexts.Count == 0)
            {
                firstRun.AppendChild(new Text(replacedText) { Space = SpaceProcessingModeValues.Preserve });
                return;
            }

            existingTexts[0].Text = replacedText;
            existingTexts[0].Space = SpaceProcessingModeValues.Preserve;

            for (var i = 1; i < existingTexts.Count; i++)
                existingTexts[i].Remove();
        }
    }
}
