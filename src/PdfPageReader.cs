using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace MntParser;

static partial class PdfPageReader
{
  public static IReadOnlyList<PdfPageContent> Read(PdfDocument document)
  {
    return [.. document.GetPages()
        .Select(page =>
        {
          var lines = ExtractLines(page);
          var readOrderText = TextCleaning.NormalizeInline(page.Text);

          return new PdfPageContent
          {
            Number = page.Number,
            ReadOrderText = readOrderText,
            ContentText = BuildContentText(readOrderText, lines),
            Lines = lines,
          };
        })];
  }

  private static string BuildContentText(string readOrderText, IReadOnlyList<DocumentLine> lines)
  {
    var contentText = readOrderText;

    contentText = GeneratedOnPattern().Replace(contentText, " ");
    contentText = CandidateReferencePattern().Replace(contentText, " ");
    contentText = PageNumberPattern().Replace(contentText, " ");

    foreach (var decorativeLine in lines.Where(line => IsDecorativeLine(line.Text)).OrderByDescending(line => line.Text.Length))
    {
      contentText = contentText.Replace(decorativeLine.Text, " ", StringComparison.OrdinalIgnoreCase);
    }

    return TextCleaning.NormalizeInline(contentText);
  }

  private static List<DocumentLine> ExtractLines(Page page)
  {
    var words = page.GetWords()
        .Where(word => !string.IsNullOrWhiteSpace(word.Text))
        .Select(word => new PositionedWord(TextCleaning.NormalizeInline(word.Text.Trim()), GetWordMidpointY(word), GetWordLeftX(word)))
        .OrderByDescending(word => word.Y)
        .ThenBy(word => word.X)
        .ToList();

    var groups = new List<List<PositionedWord>>();
    const double yTolerance = 3.0;

    foreach (var word in words)
    {
      var group = groups.FirstOrDefault(candidate => Math.Abs(candidate[0].Y - word.Y) <= yTolerance);
      if (group is null)
      {
        groups.Add([word]);
        continue;
      }

      group.Add(word);
    }

    return [.. groups
        .Select(group => group.OrderBy(word => word.X).ToList())
        .Select(group => new DocumentLine
        {
          PageNumber = page.Number,
          Y = group.Average(word => word.Y),
          Text = TextCleaning.NormalizeInline(string.Join(' ', group.Select(word => word.Text))),
        })
        .Where(line => !string.IsNullOrWhiteSpace(line.Text))];
  }

  private static double GetWordMidpointY(Word word)
  {
    return word.Letters.Count == 0
        ? 0
        : word.Letters.Average(letter => (letter.BoundingBox.BottomLeft.Y + letter.BoundingBox.TopLeft.Y) / 2.0);
  }

  private static double GetWordLeftX(Word word)
  {
    return word.Letters.Count == 0
        ? 0
        : word.Letters.Min(letter => letter.BoundingBox.BottomLeft.X);
  }

  private static bool IsDecorativeLine(string text)
  {
    return text.StartsWith("Generated on:", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("CANDIDATE REFERENCE:", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("Page ", StringComparison.OrdinalIgnoreCase);
  }

  [GeneratedRegex(@"Generated on:\s*[^\r\n]*?EDV/\d{4}/[A-Z]+/\d+-\d+", RegexOptions.IgnoreCase)]
  private static partial Regex GeneratedOnPattern();

  [GeneratedRegex(@"CANDIDATE REFERENCE:\s*EDV/\d{4}/[A-Z]+/\d+-\d+", RegexOptions.IgnoreCase)]
  private static partial Regex CandidateReferencePattern();

  [GeneratedRegex(@"Page\s+\d+\s+of\s+\d+", RegexOptions.IgnoreCase)]
  private static partial Regex PageNumberPattern();
}
