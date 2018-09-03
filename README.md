HTML Renderer with .NET Standard 2.0
=============

#### Documentation (original), Discussion and Issue tracking is on [CodePlex](https://htmlrenderer.codeplex.com/).


#### New Documentation for .NET 2.1 project

## Simply load html template and save it as new PDF file

var htmlTemplateFile = ".//03.Tables.htm";

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var html = await File.OpenText(htmlTemplateFile).ReadToEndAsync();

var pdf = HtmlRendererCore.PdfSharp.PdfGenerator.GeneratePdf(html, PdfSharp.PageSize.A4);

var generatedPdfFile = ".//03Tables.pdf";

pdf.Save(generatedPdfFile);
			

