using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;
using Microsoft.SemanticKernel.Text;

using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

var MAX_CONTENT_ITEM_SIZE = 2048;

var memoryCollectionName = "memory-collection";

var configBuilder = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.development.json", optional: true, reloadOnChange: true)
    .Build();

//Initialize SK
var embeddingOptions = configBuilder.GetSection("Embedding").Get<AzureOpenAIOptions>();
var completionOptions = configBuilder.GetSection("Completion").Get<AzureOpenAIOptions>();

var kernel = Kernel.CreateBuilder()
    .AddAzureOpenAIChatCompletion(completionOptions.DeploymentId, completionOptions.Endpoint, completionOptions.Key)
    .Build();

var memoryBuilder = new MemoryBuilder();
memoryBuilder.WithAzureOpenAITextEmbeddingGeneration(embeddingOptions.DeploymentId, embeddingOptions.Endpoint,
        embeddingOptions.Key);
memoryBuilder.WithMemoryStore(new VolatileMemoryStore());
var memory = memoryBuilder.Build();

//Parse PDF files and initialize SK memory
var pdfFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.pdf");


foreach (var pdfFileName in pdfFiles)
{
    using var pdfDocument = UglyToad.PdfPig.PdfDocument.Open(pdfFileName);
    foreach (var pdfPage in pdfDocument.GetPages())
    {
        var pageText = ContentOrderTextExtractor.GetText(pdfPage);

        var paragraphs = new List<string>();
        

        if (pageText.Length > MAX_CONTENT_ITEM_SIZE)
        {
            var lines = TextChunker.SplitPlainTextLines(pageText, MAX_CONTENT_ITEM_SIZE);
            paragraphs = TextChunker.SplitPlainTextParagraphs(lines, MAX_CONTENT_ITEM_SIZE);
        }
        else
        {
            paragraphs.Add(pageText);
        }


        foreach (var paragraph in paragraphs)
        {
            var id = pdfFileName + pdfPage.Number + paragraphs.IndexOf(paragraph);

            await memory.SaveInformationAsync(memoryCollectionName, paragraph, id);
        }


    }
}

kernel.ImportPluginFromObject(new TextMemoryPlugin(memory), nameof(TextMemoryPlugin));

//Ask SK
var query = "How is the work by \"R.C. Merkle\" used in this paper?";

var promptTemplate = await File.ReadAllTextAsync("prompt.txt");

kernel.CreateFunctionFromPrompt(promptTemplate, functionName: "QuerySkill");

var result = await kernel.InvokeAsync(kernel.Plugins.GetFunction(nameof(TextMemoryPlugin), "QuerySkill"), new KernelArguments { { "input", query } });

Console.WriteLine(result.GetValue<string>());
