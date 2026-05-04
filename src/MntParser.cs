using UglyToad.PdfPig;

namespace MntParser;

public static class MntApplication
{
  public static ExtractedApplicationDocument Parse(ReadOnlyMemory<byte> pdf)
  {
    using var document = PdfDocument.Open(pdf);
    var pages = PdfPageReader.Read(document);

    return PdfApplicationExtractor.Extract(pages);
  }
}
