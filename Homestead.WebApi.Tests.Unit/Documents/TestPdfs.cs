using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;

namespace Homestead.WebApi.Tests.Unit.Documents;

// Real (tiny) PDFs for code that opens the file itself — ClassificationInputSelector.ReadPdfFacts reads the page
// count and encryption flag with PdfPig, so a byte stub won't do.
public static class TestPdfs
{
    public static byte[] Build(int pages = 1)
    {
        var builder = new PdfDocumentBuilder();
        for (var i = 0; i < pages; i++)
            builder.AddPage(PageSize.A4);
        return builder.Build();
    }

    // Writes a PDF of the given page count to path (creating its directory) and returns the bytes written.
    public static byte[] Write(string path, int pages = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Build(pages);
        File.WriteAllBytes(path, bytes);
        return bytes;
    }
}
