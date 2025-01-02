// See https://aka.ms/new-console-template for more information
using System.ClientModel;
using AssistantPoc.Core;
using OpenAI.Assistants;
using OpenAI.Files;

// await Sample1();
await CreateAhiAssistant();

async static Task CreateAhiAssistant()
{
    var assistantHelper = new AssistantHelper();
    assistantHelper.SetEndpointEnv("https://assistant-poc.openai.azure.com/");
    assistantHelper.SetInstructionsPathEnv("/Users/trungtran/MyPlace/Yokogawa/Projects/ahi-apps/Projects/open-ai-assistant/Documents/Instructions.md");
    assistantHelper.SetKnowledgeBasePathEnv("/Users/trungtran/MyPlace/Yokogawa/Projects/ahi-apps/Projects/open-ai-assistant/Documents/KnowledgeBase");
    assistantHelper.InitClient();

    await assistantHelper.CreateAhiAssistant();
}

async static Task Sample1()
{
    var assistantHelper = new AssistantHelper();
    assistantHelper.InitClient();
    var client = assistantHelper.Client;

    OpenAIFileClient fileClient = client.GetOpenAIFileClient();
    AssistantClient assistantClient = client.GetAssistantClient();

    // First, let's contrive a document we'll use retrieval with and upload it.
    using Stream document = BinaryData.FromString("""
{
    "description": "This document contains the sale history data for Contoso products.",
    "sales": [
        {
            "month": "January",
            "by_product": {
                "113043": 15,
                "113045": 12,
                "113049": 2
            }
        },
        {
            "month": "February",
            "by_product": {
                "113045": 22
            }
        },
        {
            "month": "March",
            "by_product": {
                "113045": 16,
                "113055": 5
            }
        }
    ]
}
""").ToStream();

    ClientResult<OpenAIFile> salesFileResult = await fileClient.UploadFileAsync(
        document,
        "monthly_sales.json",
        FileUploadPurpose.Assistants);

    // Now, we'll create a client intended to help with that data
    AssistantCreationOptions assistantOptions = new()
    {
        Name = "Example: Contoso sales RAG",
        Instructions =
            "You are an assistant that looks up sales data and helps visualize the information based"
            + " on user queries. When asked to generate a graph, chart, or other visualization, use"
            + " the code interpreter tool to do so.",
        Tools =
    {
        new FileSearchToolDefinition(),
        // new CodeInterpreterToolDefinition(),
        // new FunctionToolDefinition()
    },
        ToolResources = new()
        {
            FileSearch = new()
            {
                NewVectorStores =
            {
                new VectorStoreCreationHelper([salesFileResult.Value.Id]),
            }
            }
        },
        Temperature = 0.5f,
        // ResponseFormat = AssistantResponseFormat.JsonObject
        ResponseFormat = AssistantResponseFormat.Auto
    };

    ClientResult<Assistant> assistantResult = await assistantClient.CreateAssistantAsync(model: "gpt-4o", assistantOptions);

    // Create and run a thread with a user query about the data already associated with the assistant
    ThreadCreationOptions threadOptions = new()
    {
        InitialMessages = { "How well did product 113045 sell in February? Graph its trend over time." }
    };
    ThreadRun threadRun = await assistantClient.CreateThreadAndRunAsync(assistantResult.Value.Id, threadOptions);

    // Check back to see when the run is done
    do
    {
        Thread.Sleep(TimeSpan.FromSeconds(1));
        threadRun = assistantClient.GetRun(threadRun.ThreadId, threadRun.Id);
    } while (!threadRun.Status.IsTerminal);

    // Finally, we'll print out the full history for the thread that includes the augmented generation
    AsyncCollectionResult<ThreadMessage> messages
        = assistantClient.GetMessagesAsync(
            threadRun.ThreadId,
            new MessageCollectionOptions() { Order = MessageCollectionOrder.Ascending });

    await foreach (ThreadMessage message in messages)
    {
        Console.Write($"[{message.Role.ToString().ToUpper()}]: ");
        foreach (MessageContent contentItem in message.Content)
        {
            if (!string.IsNullOrEmpty(contentItem.Text))
            {
                Console.WriteLine($"{contentItem.Text}");

                if (contentItem.TextAnnotations.Count > 0)
                {
                    Console.WriteLine();
                }

                // Include annotations, if any.
                foreach (TextAnnotation annotation in contentItem.TextAnnotations)
                {
                    if (!string.IsNullOrEmpty(annotation.InputFileId))
                    {
                        Console.WriteLine($"* File citation, file ID: {annotation.InputFileId}");
                    }
                    if (!string.IsNullOrEmpty(annotation.OutputFileId))
                    {
                        Console.WriteLine($"* File output, new file ID: {annotation.OutputFileId}");
                    }
                }
            }
            if (!string.IsNullOrEmpty(contentItem.ImageFileId))
            {
                ClientResult<OpenAIFile> imageInfo = await fileClient.GetFileAsync(contentItem.ImageFileId);
                BinaryData imageBytes = await fileClient.DownloadFileAsync(contentItem.ImageFileId);
                using FileStream stream = File.OpenWrite($"{imageInfo.Value.Filename}.png");
                imageBytes.ToStream().CopyTo(stream);

                Console.WriteLine($"<image: {imageInfo.Value.Filename}.png>");
            }
        }
        Console.WriteLine();
    }
}