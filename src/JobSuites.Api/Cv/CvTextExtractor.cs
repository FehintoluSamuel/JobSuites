using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace JobSuites.Api.Cv;

/// <summary>
/// Extracts plain text from an uploaded CV.
///
/// This layer only answers "what does the document say". Interpreting the text is
/// CvParser's job, which keeps the two testable independently: here we assert
/// text comes out, there we assert the structure is read correctly.
/// </summary>
public static class CvTextExtractor
{
    public const long MaxBytes = 8 * 1024 * 1024;

    public static string Extract(Stream stream, string fileName)
    {
        if (stream.Length == 0)
        {
            throw new InvalidCvException("That file is empty.");
        }

        if (stream.Length > MaxBytes)
        {
            throw new InvalidCvException("That file is larger than 8 MB.");
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        try
        {
            return ext switch
            {
                ".pdf" => FromPdf(stream),
                ".docx" => FromDocx(stream),
                ".txt" or ".md" or ".rtf" => FromPlainText(stream),
                _ => throw new InvalidCvException(
                    "We can read PDF, DOCX, and plain text files. " +
                    $"'{ext}' is not supported — try exporting your CV as a PDF."),
            };
        }
        catch (InvalidCvException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A corrupt file should read as "we could not read this", not as an
            // unhandled 500 with a stack trace the user cannot act on.
            throw new InvalidCvException(
                $"We could not read that file ({ex.GetType().Name}). " +
                "Try re-exporting it as a PDF.");
        }
    }

    private static string FromPdf(Stream stream)
    {
        var builder = new StringBuilder();

        using var pdf = PdfDocument.Open(stream);
        foreach (var page in pdf.GetPages())
        {
            // PdfPig returns words in layout order on some PDFs and reading order
            // on others. Neither is a substitute for the parser, which works on
            // lines, so join by line-break hints where available.
            var content = page.Text;
            if (!string.IsNullOrWhiteSpace(content))
            {
                builder.AppendLine(content);
            }
        }

        var text = builder.ToString().Trim();
        if (text.Length == 0)
        {
            throw new InvalidCvException(
                "That PDF has no selectable text. If it is a scan or an image, " +
                "we cannot read it — upload a text-based PDF or a DOCX instead.");
        }

        return text;
    }

    private static string FromDocx(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) throw new InvalidCvException("That DOCX has no readable body.");

        var builder = new StringBuilder();

        foreach (var p in body.Descendants<Paragraph>())
        {
            var text = string.Concat(p.Descendants<Text>().Select(t => t.Text));

            // A list item should read as its own line so the skills section can be
            // split on bullets rather than glued into one run-on string. The
            // numbering properties are what Word writes for a bulleted item.
            var isListItem = p.Descendants<NumberingProperties>().Any();
            if (isListItem)
            {
                builder.AppendLine("• " + text);
            }
            else
            {
                builder.AppendLine(text);
            }
        }

        var result = builder.ToString().Trim();
        if (result.Length == 0)
        {
            throw new InvalidCvException("That DOCX has no readable text.");
        }

        return result;
    }

    private static string FromPlainText(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd().Trim();

        if (text.Length == 0) throw new InvalidCvException("That file is empty.");

        // An RTF file carries markup even with a .txt extension.
        if (text.StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidCvException(
                "That looks like an RTF file. Save it as PDF or DOCX and try again.");
        }

        return text;
    }
}

public sealed class InvalidCvException(string message) : Exception(message);
