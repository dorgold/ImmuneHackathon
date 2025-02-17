// Copyright (c) Microsoft. All rights reserved.

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using GettingStarted;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using SemanticKernelExamples;
using System.Net.Http;
using Microsoft.SemanticKernel.Agents;
using OpenAI.Assistants;

namespace SemanticKernelSample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Kusto Agent
            var kernel = CreateKernel();
            kernel.Plugins.AddFromType<Step6_Kusto_Agent>("Kusto");

            var history = new ChatHistory();

            ChatCompletionAgent agent =
                new()
                {
                    Instructions = "You are an expert in KQL (Kusto Query Language).",
                    Name = "KQLmaster",
                    Kernel = kernel,
                    Arguments = new KernelArguments(new OpenAIPromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() }),
                };

            // Respond to user input, invoking functions where appropriate.
            await InvokeAgentAsync("Explain the schema of the table.");

            // Local function to invoke agent and display the conversation messages.
            async Task InvokeAgentAsync(string input)
            {
                ChatMessageContent message = new(AuthorRole.User, input);
                history.Add(message);
                this.WriteAgentChatMessage(message);

                await foreach (ChatMessageContent response in agent.InvokeAsync(history))
                {
                    chat.Add(response);

                    this.WriteAgentChatMessage(response);
                }
            }
        }

        private static Kernel CreateKernel()
        {
            string deploymentName = AppConfig.OpenAI.DeploymentName;
            string modelId = AppConfig.OpenAI.ModelId;
            string endpoint = AppConfig.OpenAI.Endpoint;
            string apiKey = AppConfig.OpenAI.ApiKey;

            var builder = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion(
                    deploymentName: deploymentName,
                    endpoint: endpoint,
                    apiKey: apiKey,
                    serviceId: null,
                    modelId: modelId);

            builder.Services.AddLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Warning);
            });

            return builder.Build();
        }


        protected static void WriteAgentChatMessage(ChatMessageContent message)
        {
            // Include ChatMessageContent.AuthorName in output, if present.
            string authorExpression = message.Role == AuthorRole.User ? string.Empty : $" - {message.AuthorName ?? "*"}";
            // Include TextContent (via ChatMessageContent.Content), if present.
            string contentExpression = string.IsNullOrWhiteSpace(message.Content) ? string.Empty : message.Content;
            Console.WriteLine($"\n# {message.Role}{authorExpression}:{contentExpression}");

            // Provide visibility for inner content (that isn't TextContent).
            foreach (KernelContent item in message.Items)
            {
                if (item is AnnotationContent annotation)
                {
                    Console.WriteLine($"  [{item.GetType().Name}] {annotation.Quote}: File #{annotation.FileId}");
                }
                else if (item is FileReferenceContent fileReference)
                {
                    Console.WriteLine($"  [{item.GetType().Name}] File #{fileReference.FileId}");
                }
                else if (item is ImageContent image)
                {
                    Console.WriteLine($"  [{item.GetType().Name}] {image.Uri?.ToString() ?? image.DataUri ?? $"{image.Data?.Length} bytes"}");
                }
                else if (item is FunctionCallContent functionCall)
                {
                    Console.WriteLine($"  [{item.GetType().Name}] {functionCall.Id}");
                }
                else if (item is FunctionResultContent functionResult)
                {
                    Console.WriteLine($"  [{item.GetType().Name}] {functionResult.CallId} - {functionResult.Result?.AsJson() ?? "*"}");
                }
            }
        }
    }
}
