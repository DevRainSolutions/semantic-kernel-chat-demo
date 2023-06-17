using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI.ChatCompletion;
using Microsoft.SemanticKernel.CoreSkills;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Orchestration;
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

var kernel = Kernel.Builder
    .WithAzureTextEmbeddingGenerationService(embeddingOptions.DeploymentId, embeddingOptions.Endpoint,
        embeddingOptions.Key)
    .WithAzureChatCompletionService(completionOptions.DeploymentId, completionOptions.Endpoint, completionOptions.Key)
    .WithMemoryStorage(new VolatileMemoryStore())
    .Build();



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

            await kernel.Memory.SaveInformationAsync(memoryCollectionName, paragraph, id);
        }


    }
}


kernel.ImportSkill(new TextMemorySkill(), nameof(TextMemorySkill));

//Ask SK
var query = "How is the work by \"R.C. Merkle\" used in this paper?";

var promptTemplate = await File.ReadAllTextAsync("prompt.txt");

kernel.CreateSemanticFunction(promptTemplate,
    "Query",
    "QuerySkill",
    maxTokens: 2048);
    
    
var contextVariables = new ContextVariables(query);

contextVariables.Set("collection", memoryCollectionName);

var result = await kernel.RunAsync(contextVariables, kernel.Skills.GetFunction("QuerySkill", "Query"));

if (result.ErrorOccurred)
{
    Console.WriteLine($"Error: {result.LastErrorDescription}");
}
else
{
    Console.WriteLine(result.Result);
}

