using System.Text.RegularExpressions;

namespace MntParser;

static partial class PdfApplicationExtractor
{
  private static IReadOnlyList<DocumentLine> GetLinesBetween(List<DocumentLine> lines, string startMarker, string? endMarker)
  {
    var startIndex = FindLine(lines, startMarker);
    if (startIndex < 0)
    {
      return [];
    }

    var endIndex = endMarker is null ? lines.Count : FindLine(lines, endMarker);
    if (endIndex < 0)
    {
      endIndex = lines.Count;
    }

    var sliceStart = Math.Min(startIndex + 1, lines.Count);
    if (endIndex <= sliceStart)
    {
      return [];
    }

    return [.. lines.Skip(sliceStart).Take(endIndex - sliceStart)];
  }

  private static List<int> FindAllLineIndices(IReadOnlyList<DocumentLine> lines, string marker)
  {
    var indices = new List<int>();

    for (var index = 0; index < lines.Count; index++)
    {
      if (LineEquals(lines[index], marker))
      {
        indices.Add(index);
      }
    }

    return indices;
  }

  private static int FindLine(List<DocumentLine> lines, string marker)
  {
    for (var index = 0; index < lines.Count; index++)
    {
      if (LineEquals(lines[index], marker))
      {
        return index;
      }
    }

    return -1;
  }

  private static string ExtractBetween(string text, string startMarker, string? endMarker)
  {
    var startIndex = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
    if (startIndex < 0)
    {
      return string.Empty;
    }

    startIndex += startMarker.Length;

    var endIndex = endMarker is null
        ? text.Length
        : text.IndexOf(endMarker, startIndex, StringComparison.OrdinalIgnoreCase);

    if (endIndex < 0)
    {
      endIndex = text.Length;
    }

    return TextCleaning.NormalizeInline(text[startIndex..endIndex]);
  }

  private static string ExtractBetweenRaw(string text, string startMarker, string? endMarker)
  {
    var startIndex = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
    if (startIndex < 0)
    {
      return string.Empty;
    }

    startIndex += startMarker.Length;

    var endIndex = endMarker is null
        ? text.Length
        : text.IndexOf(endMarker, startIndex, StringComparison.OrdinalIgnoreCase);

    if (endIndex < 0)
    {
      endIndex = text.Length;
    }

    return text[startIndex..endIndex];
  }

  private static string? EmptyToNull(string? value)
  {
    return string.IsNullOrWhiteSpace(value) ? null : value;
  }

  private static bool LineEquals(DocumentLine line, string value)
  {
    return TextEquals(line.Text, value);
  }

  private static bool TextEquals(string text, string value)
  {
    return text.Equals(value, StringComparison.OrdinalIgnoreCase);
  }

  private static string? TrimTrailingValueLine(string? block, string? trailingValue)
  {
    if (string.IsNullOrWhiteSpace(block) || string.IsNullOrWhiteSpace(trailingValue))
    {
      return block;
    }

    var normalizedBlock = block.TrimEnd();
    var normalizedTrailingValue = trailingValue.Trim();
    if (normalizedBlock.EndsWith(normalizedTrailingValue, StringComparison.OrdinalIgnoreCase))
    {
      normalizedBlock = normalizedBlock[..^normalizedTrailingValue.Length].TrimEnd();
    }

    return string.IsNullOrWhiteSpace(normalizedBlock) ? null : normalizedBlock;
  }

  private static string? TrimTrailingStandaloneListMarker(string? block)
  {
    if (string.IsNullOrWhiteSpace(block))
    {
      return block;
    }

    var normalizedBlock = block.TrimEnd();
    if (normalizedBlock.EndsWith("\n\n-", StringComparison.Ordinal))
    {
      normalizedBlock = normalizedBlock[..^3].TrimEnd();
    }
    else if (normalizedBlock.EndsWith("\n-", StringComparison.Ordinal))
    {
      normalizedBlock = normalizedBlock[..^2].TrimEnd();
    }
    else if (normalizedBlock.Equals("-", StringComparison.Ordinal))
    {
      normalizedBlock = string.Empty;
    }

    return string.IsNullOrWhiteSpace(normalizedBlock) ? null : normalizedBlock;
  }

  private static string? AppendWrappedValueContinuation(string? value, IReadOnlyList<DocumentLine> lines, int labelIndex)
  {
    if (string.IsNullOrWhiteSpace(value) || labelIndex + 1 >= lines.Count)
    {
      return value;
    }

    var nextText = lines[labelIndex + 1].Text;
    if (!LooksLikeWrappedValueContinuation(value, nextText))
    {
      return value;
    }

    return $"{value} {nextText}";
  }

  private static bool LooksLikeWrappedValueContinuation(string value, string nextText)
  {
    if (string.IsNullOrWhiteSpace(nextText)
        || nextText.EndsWith(':')
        || LooksLikeSectionHeading(nextText)
        || IsDecorativeLine(nextText))
    {
      return false;
    }

    if (nextText.Contains(':', StringComparison.Ordinal))
    {
      return false;
    }

    if (value.EndsWith('.') || value.EndsWith('!') || value.EndsWith('?'))
    {
      return false;
    }

    return nextText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length <= 2;
  }

  private static bool IsDecorativeLine(string text)
  {
    return text.StartsWith("Generated on:", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("Page ", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("CANDIDATE REFERENCE:", StringComparison.OrdinalIgnoreCase);
  }

  private static bool LooksLikeSectionHeading(string text)
  {
    return NumberedSectionHeadingPattern().IsMatch(text) || text.Equals(text.ToUpperInvariant(), StringComparison.Ordinal);
  }

  private static string? GetPreviousValue(IReadOnlyList<DocumentLine> lines, int labelIndex)
  {
    for (var index = labelIndex - 1; index >= 0; index--)
    {
      var text = lines[index].Text;
      if (string.IsNullOrWhiteSpace(text))
      {
        continue;
      }

      if (text.EndsWith(':'))
      {
        break;
      }

      return EmptyToNull(text);
    }

    return null;
  }

  private static List<DocumentLine> CollectFollowingLines(IReadOnlyList<DocumentLine> lines, int startIndex, params string[] stopMarkers)
  {
    var values = new List<DocumentLine>();
    var stopSet = new HashSet<string>(stopMarkers, StringComparer.OrdinalIgnoreCase);

    for (var index = startIndex + 1; index < lines.Count; index++)
    {
      if (stopSet.Contains(lines[index].Text))
      {
        break;
      }

      values.Add(lines[index]);
    }

    return values;
  }

  [GeneratedRegex(@"^\d+\.\s")]
  private static partial Regex NumberedSectionHeadingPattern();
}
