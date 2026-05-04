using System.Text.Json;
using System.Text.Json.Serialization;
using MntParser;

if (args.Length == 0)
{
  Console.Error.WriteLine("Usage: sample <pdf-file>");
  return 1;
}

var pdfPath = Path.GetFullPath(args[0]);

if (!File.Exists(pdfPath))
{
  Console.Error.WriteLine($"PDF file not found: {pdfPath}");
  return 1;
}

var outputPath = Path.ChangeExtension(pdfPath, ".json");
var pdf = await File.ReadAllBytesAsync(pdfPath);
var document = MntApplication.Parse(pdf);
var json = JsonSerializer.Serialize(document, new JsonSerializerOptions
{
  DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
  PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  WriteIndented = true,
});

await File.WriteAllTextAsync(outputPath, json);
Console.WriteLine(outputPath);
return 0;
