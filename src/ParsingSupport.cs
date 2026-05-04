using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MntParser;

static class OrderedFieldParser
{
  public static Dictionary<string, string?> Parse(string sectionText, params string[] labels)
  {
    var results = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    var positions = new List<FieldPosition>();
    var (condensedText, indexMap) = BuildCondensedText(sectionText);
    var searchFrom = 0;

    foreach (var label in labels)
    {
      var condensedLabel = RemoveWhitespace(label);
      var labelIndex = condensedText.IndexOf(condensedLabel, searchFrom, StringComparison.OrdinalIgnoreCase);
      if (labelIndex < 0)
      {
        continue;
      }

      positions.Add(new FieldPosition(label, labelIndex, condensedLabel.Length));
      searchFrom = labelIndex + condensedLabel.Length;
    }

    for (var index = 0; index < positions.Count; index++)
    {
      var current = positions[index];
      var condensedValueStart = current.CondensedIndex + current.CondensedLength;
      var valueStart = condensedValueStart < indexMap.Count ? indexMap[condensedValueStart] : sectionText.Length;
      var valueEnd = index + 1 < positions.Count ? indexMap[positions[index + 1].CondensedIndex] : sectionText.Length;
      var value = valueEnd <= valueStart ? string.Empty : sectionText[valueStart..valueEnd];
      results[current.Label] = CleanValue(value);
    }

    return results;
  }

  public static Dictionary<string, string?> ParseRegex(string sectionText, params (string Key, Regex Pattern)[] labels)
  {
    var results = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    var positions = new List<FieldPosition>();
    var (condensedText, indexMap) = BuildCondensedText(sectionText);
    var searchFrom = 0;

    foreach (var (key, pattern) in labels)
    {
      var match = pattern.Match(condensedText, searchFrom);
      if (!match.Success)
      {
        continue;
      }

      positions.Add(new FieldPosition(key, match.Index, match.Length));
      searchFrom = match.Index + match.Length;
    }

    for (var index = 0; index < positions.Count; index++)
    {
      var current = positions[index];
      var condensedValueStart = current.CondensedIndex + current.CondensedLength;
      var valueStart = condensedValueStart < indexMap.Count ? indexMap[condensedValueStart] : sectionText.Length;
      var valueEnd = index + 1 < positions.Count ? indexMap[positions[index + 1].CondensedIndex] : sectionText.Length;
      var value = valueEnd <= valueStart ? string.Empty : sectionText[valueStart..valueEnd];
      results[current.Label] = CleanValue(value);
    }

    return results;
  }

  private static (string CondensedText, List<int> IndexMap) BuildCondensedText(string value)
  {
    var condensed = new StringBuilder(value.Length);
    var indexMap = new List<int>(value.Length);

    for (var index = 0; index < value.Length; index++)
    {
      if (char.IsWhiteSpace(value[index]))
      {
        continue;
      }

      condensed.Append(value[index]);
      indexMap.Add(index);
    }

    return (condensed.ToString(), indexMap);
  }

  private static string RemoveWhitespace(string value)
  {
    var builder = new StringBuilder(value.Length);

    foreach (var character in value)
    {
      if (!char.IsWhiteSpace(character))
      {
        builder.Append(character);
      }
    }

    return builder.ToString();
  }

  private static string? CleanValue(string? value)
  {
    return string.IsNullOrWhiteSpace(value) ? null : TextCleaning.NormalizeInline(value);
  }

  private sealed record FieldPosition(string Label, int CondensedIndex, int CondensedLength);
}

static class ValueParsing
{
  public static bool? ParseYesNo(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    var normalizedValue = TextCleaning.NormalizeInline(value);

    if (normalizedValue.StartsWith("Yes", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    if (normalizedValue.StartsWith("No", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    return null;
  }
}

static partial class DateParsing
{
  public const string MonthPattern = "January|February|March|April|May|June|July|August|September|October|November|December";
  private static readonly string[] FlexibleDateFormats =
  [
      "MMMM d, yyyy",
        "d MMMM, yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "dd/MM/yy",
        "d/M/yy",
        "dd/MM/yyyy",
        "d/M/yyyy",
        "MMMM yyyy",
    ];

  public static string? ParseMonthYear(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    return DateTime.TryParseExact(value.Trim(), "MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
        ? parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture)
        : null;
  }

  public static string? ParseFlexibleDate(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    var cleaned = OrdinalSuffixPattern().Replace(value.Trim(), string.Empty);
    foreach (var format in FlexibleDateFormats)
    {
      if (!DateTime.TryParseExact(cleaned, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
      {
        continue;
      }

      return format == "MMMM yyyy"
          ? parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture)
          : parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    return null;
  }
  [GeneratedRegex(@"(?<=\d)(st|nd|rd|th)", RegexOptions.IgnoreCase)]
  private static partial Regex OrdinalSuffixPattern();
}

static partial class DateRangeParsing
{
  public static void ApplyMonthYearRange(IDateRangeTarget target, string? rawValue)
  {
    if (string.IsNullOrWhiteSpace(rawValue))
    {
      return;
    }

    var match = MonthYearRangePattern().Match(TextCleaning.NormalizeInline(rawValue));
    if (!match.Success)
    {
      return;
    }

    var startText = match.Groups["start"].Value.Trim();
    var endText = match.Groups["end"].Value.Trim();

    target.StartDate = DateParsing.ParseMonthYear(startText);
    target.EndDate = endText.Equals("Present", StringComparison.OrdinalIgnoreCase)
        ? null
        : DateParsing.ParseMonthYear(endText);
    target.DurationText = match.Groups["duration"].Value.Trim();
  }

  [GeneratedRegex(@"^(?<start>(?:" + DateParsing.MonthPattern + @")\s+\d{4})\s+(?<end>Present|(?:" + DateParsing.MonthPattern + @")\s+\d{4})\s+\((?<duration>[^)]+)\)$", RegexOptions.IgnoreCase)]
  private static partial Regex MonthYearRangePattern();
}
