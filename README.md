HTML Renderer
=============

#### Documentation (original), Discussion and Issue tracking is on [CodePlex](https://htmlrenderer.codeplex.com/).


#### New Documentation with .NET 2.1 project

var htmlTemplateFile = ".//03.Tables.htm";

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var html = await File.OpenText(htmlTemplateFile).ReadToEndAsync();

var pdf = HtmlRendererCore.PdfSharp.PdfGenerator.GeneratePdf(html, PdfSharp.PageSize.A4);

var generatedPdfFile = ".//03Tables.pdf";

pdf.Save(generatedPdfFile);
			

