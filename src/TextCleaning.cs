using System.Text;
using System.Text.RegularExpressions;

namespace MntParser;

static partial class TextCleaning
{
  private static readonly HashSet<string> LowercaseHeadingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "as", "at", "by", "for", "from", "in", "of", "on", "or", "the", "to", "with"
    };

  private static readonly Dictionary<string, string> Replacements = new(StringComparer.Ordinal)
  {
    ["\u00B4\u00BC\u00FC"] = "fi",
    ["\uFB01"] = "fi",
    ["\uFB02"] = "fl",
    ["\u00D4\u00C7\u00D6"] = "'",
    ["\u00D4\u00C7\u00A3"] = '"'.ToString(),
    ["\u00D4\u00C7\u00A5"] = '"'.ToString(),
    ["\u00D4\u00C7\u00F6"] = "-",
    ["\u00D4\u00C7\u00F4"] = "-",
    ["\u252C\u00FA"] = "\u00A3",
    ["\u00C2\u00A3"] = "\u00A3",
  };

  private static readonly Regex ListItemPattern = ListItemRegex();
  private static readonly Regex LeadingListMarkerPattern = LeadingListMarkerRegex();

  public static string NormalizeInline(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return string.Empty;
    }

    var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\u00ad', ' ');

    foreach (var replacement in Replacements)
    {
      normalized = normalized.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
    }

    normalized = WhitespacePattern().Replace(normalized, " ");
    normalized = SpaceBeforePunctuationPattern().Replace(normalized, "$1");
    normalized = SpaceAfterOpeningBracketPattern().Replace(normalized, "$1");
    normalized = SpaceBeforeClosingBracketPattern().Replace(normalized, "$1");
    normalized = normalized.Replace(" . .", " ...", StringComparison.Ordinal);
    return normalized.Trim();
  }

  public static string? NormalizeParagraphText(IEnumerable<DocumentLine> lines)
  {
    var preparedLines = PrepareLines(lines).ToList();
    if (preparedLines.Count == 0)
    {
      return null;
    }

    var paragraphs = BuildSegments(preparedLines, SegmentMode.Paragraphs)
        .Select(segment => segment.Text)
        .Where(text => !string.IsNullOrWhiteSpace(text))
        .ToList();

    return paragraphs.Count == 0 ? null : string.Join("\n\n", paragraphs);
  }

  public static string? NormalizeStructuredText(IEnumerable<DocumentLine> lines)
  {
    var preparedLines = PrepareLines(lines).ToList();
    if (preparedLines.Count == 0)
    {
      return null;
    }

    var segments = BuildSegments(preparedLines, SegmentMode.Structured)
        .Where(segment => !string.IsNullOrWhiteSpace(segment.Text))
        .ToList();

    if (segments.Count == 0)
    {
      return null;
    }

    var builder = new StringBuilder();
    for (var index = 0; index < segments.Count; index++)
    {
      if (index > 0)
      {
        builder.Append(segments[index - 1].IsListItem && segments[index].IsListItem ? "\n" : "\n\n");
      }

      builder.Append(FormatStructuredSegment(segments[index]));
    }

    return builder.ToString();
  }

  private static IEnumerable<TextSegment> BuildSegments(IReadOnlyList<PreparedLine> preparedLines, SegmentMode mode)
  {
    var baselineGap = EstimateBaselineLineGap(preparedLines);
    var current = new List<PreparedLine>();
    var currentIsListItem = false;

    foreach (var line in preparedLines)
    {
      var startsListItem = IsListItem(line.Text);
      if (current.Count == 0)
      {
        current.Add(line);
        currentIsListItem = startsListItem;
        continue;
      }

      var previous = current[^1];
      var startsNewSegment = startsListItem || StartsNewParagraph(previous, line, baselineGap);

      if (mode == SegmentMode.Paragraphs && startsListItem)
      {
        startsNewSegment = StartsNewParagraph(previous, line, baselineGap);
      }

      if (mode == SegmentMode.Structured && !startsNewSegment && StartsImplicitListItem(current, line))
      {
        startsListItem = true;
        startsNewSegment = true;
      }

      if (!startsNewSegment)
      {
        current.Add(line);
        continue;
      }

      yield return new TextSegment(JoinLines(current), currentIsListItem);
      current = [line];
      currentIsListItem = startsListItem;
    }

    if (current.Count > 0)
    {
      yield return new TextSegment(JoinLines(current), currentIsListItem);
    }
  }

  private static IEnumerable<PreparedLine> PrepareLines(IEnumerable<DocumentLine> lines)
  {
    return lines
        .Select(line => new PreparedLine(line.PageNumber, line.Y, NormalizeInline(line.Text)))
        .Where(line => !string.IsNullOrWhiteSpace(line.Text));
  }

  private static double EstimateBaselineLineGap(IReadOnlyList<PreparedLine> lines)
  {
    var gaps = new List<double>();

    for (var index = 1; index < lines.Count; index++)
    {
      var previous = lines[index - 1];
      var current = lines[index];
      if (previous.PageNumber != current.PageNumber)
      {
        continue;
      }

      var gap = previous.Y - current.Y;
      if (gap > 4 && gap < 40)
      {
        gaps.Add(gap);
      }
    }

    if (gaps.Count == 0)
    {
      return 16;
    }

    gaps.Sort();
    return gaps[gaps.Count / 2];
  }

  private static bool StartsNewParagraph(PreparedLine previous, PreparedLine current, double baselineGap)
  {
    if (LooksLikeStandaloneHeading(previous.Text) || LooksLikeStandaloneHeading(current.Text))
    {
      return true;
    }

    if (previous.PageNumber != current.PageNumber)
    {
      return EndsSentence(previous.Text) && StartsLikelyParagraph(current.Text);
    }

    return previous.Y - current.Y > baselineGap * 1.55;
  }

  private static string JoinLines(IReadOnlyList<PreparedLine> lines)
  {
    var builder = new StringBuilder(lines[0].Text);

    for (var index = 1; index < lines.Count; index++)
    {
      AppendJoinedLine(builder, lines[index].Text);
    }

    return NormalizeInline(builder.ToString());
  }

  private static string FormatStructuredSegment(TextSegment segment)
  {
    if (!segment.IsListItem)
    {
      return segment.Text;
    }

    var text = LeadingListMarkerPattern.Replace(segment.Text, string.Empty);
    return $"- {text}";
  }

  private static void AppendJoinedLine(StringBuilder builder, string nextLine)
  {
    if (builder.Length == 0)
    {
      builder.Append(nextLine);
      return;
    }

    var previousCharacter = builder[^1];
    if (previousCharacter == '-')
    {
      builder.Append(nextLine);
      return;
    }

    if (nextLine.Length > 0 && ",.;:!?)]}".Contains(nextLine[0]))
    {
      builder.Append(nextLine);
      return;
    }

    builder.Append(' ');
    builder.Append(nextLine);
  }

  private static bool IsListItem(string text)
  {
    return ListItemPattern.IsMatch(text);
  }

  private static bool StartsImplicitListItem(IReadOnlyList<PreparedLine> currentSegment, PreparedLine nextLine)
  {
    if (currentSegment.Count == 0)
    {
      return false;
    }

    var previous = currentSegment[^1].Text;
    if (!LooksLikeImplicitListLine(nextLine.Text) && !LooksLikeImplicitListHeading(nextLine.Text))
    {
      return false;
    }

    if (currentSegment.Count == 1 && previous.EndsWith(':'))
    {
      return true;
    }

    if (currentSegment.Count == 1 && LooksLikeImplicitListLine(previous) && LooksLikeImplicitListHeading(nextLine.Text))
    {
      return true;
    }

    return currentSegment.Count == 1
        && LooksLikeImplicitListLine(previous)
        && !EndsSentence(previous)
        && !EndsSentence(nextLine.Text);
  }

  private static bool LooksLikeImplicitListLine(string text)
  {
    return !string.IsNullOrWhiteSpace(text)
        && text.Length <= 120
        && char.IsUpper(text[0])
        && !LooksLikeStandaloneHeading(text);
  }

  private static bool LooksLikeImplicitListHeading(string text)
  {
    return !string.IsNullOrWhiteSpace(text)
        && text.Length <= 120
        && char.IsUpper(text[0])
        && text.EndsWith(':');
  }

  private static bool LooksLikeStandaloneHeading(string text)
  {
    if (IsListItem(text) || text.Length > 80 || text.EndsWith('.') || text.EndsWith(',') || text.EndsWith(';'))
    {
      return false;
    }

    var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (words.Length == 0 || words.Length > 10)
    {
      return false;
    }

    foreach (var word in words)
    {
      var cleanedWord = word.Trim(',', ':', ';', '"', '\'', '(', ')');
      if (cleanedWord.Length == 0 || LowercaseHeadingWords.Contains(cleanedWord))
      {
        continue;
      }

      if (char.IsUpper(cleanedWord[0]) || char.IsDigit(cleanedWord[0]) || cleanedWord[0] == '&' || cleanedWord[0] == '/')
      {
        continue;
      }

      return false;
    }

    return true;
  }

  private static bool EndsSentence(string text)
  {
    return text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') || text.EndsWith(':');
  }

  private static bool StartsLikelyParagraph(string text)
  {
    return IsListItem(text)
        || LooksLikeStandaloneHeading(text)
        || (text.Length > 0 && char.IsUpper(text[0]));
  }

  private sealed record PreparedLine(int PageNumber, double Y, string Text);

  private sealed record TextSegment(string Text, bool IsListItem);

  private enum SegmentMode
  {
    Paragraphs,
    Structured,
  }

  [GeneratedRegex(@"^(?:\d+\.|[A-Za-z]\)|[-*\u2022])\s+")]
  private static partial Regex ListItemRegex();

  [GeneratedRegex(@"^(?:\d+\.|[A-Za-z]\)|[-*\u2022])\s+")]
  private static partial Regex LeadingListMarkerRegex();

  [GeneratedRegex(@"\s+")]
  private static partial Regex WhitespacePattern();

  [GeneratedRegex(@"\s+([,.;:!?])")]
  private static partial Regex SpaceBeforePunctuationPattern();

  [GeneratedRegex(@"([([{])\s+")]
  private static partial Regex SpaceAfterOpeningBracketPattern();

  [GeneratedRegex(@"\s+([)\]}])")]
  private static partial Regex SpaceBeforeClosingBracketPattern();
}
