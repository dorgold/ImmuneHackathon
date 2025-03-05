// Copyright (c) Microsoft. All rights reserved.

using ImmuneGettingStartedSemanticKernel.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SemanticKernelExamples;

namespace ImmuneGettingStartedSemanticKernel;

class Program
{
    static async Task Main(string[] args)
    {
        // Kusto Agent
        var kernel = CreateKernel();
        kernel.Plugins.AddFromType<KustoPlugin>("KustoPlugin");
        KernelFunction scoreFunc = kernel.Plugins.GetFunction("KustoPlugin", "Score");
        KernelFunction queryFunc = kernel.Plugins.GetFunction("KustoPlugin", "Query");

        ChatCompletionAgent dataAnalyst =
            new()
            {
                Instructions = """
                               You are an are a data analyst, you job is to formulate a strategy for querying the data that KQLMaster will execute and Validator will validate.
                               """,
                Name = "DataAnalyst",
                Kernel = kernel,
            };

        ChatCompletionAgent agent =
            new()
            {
                Instructions = """
                               You are an expert in KQL (Kusto Query Language). You should run multiple queries to get the best results. You always return a list of comma separated DeviceIds or an error.
                               """,
                Name = "KQLMaster",
                Kernel = kernel,
                Arguments = new KernelArguments(new OpenAIPromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Required([queryFunc], true, new FunctionChoiceBehaviorOptions() {AllowConcurrentInvocation = true, AllowParallelCalls = true}) }),
            };

        ChatCompletionAgent checker =
            new()
            {
                Instructions = """
                               Your job is to check the KQLMaster response (by running a the score function), help him maximize the score. When the score is above 0.8, say it is approved and yield the score and the list of DeviceIds.
                               If KQLMaster didn't output a list of DeviceIds, say that the response is invalid.
                               """,
                Name = "Validator",
                Kernel = kernel,
                Arguments = new KernelArguments(new OpenAIPromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Required([scoreFunc]) }),
            };

        KernelFunction terminationFunction =
            AgentGroupChat.CreatePromptFunctionForStrategy(
                """
                Determine if the result has been approved.  If so, respond with a single word: yes

                History:
                {{$history}}
                """,
                safeParameterNames: "history");

//         KernelFunction selectionFunction =
//             AgentGroupChat.CreatePromptFunctionForStrategy(
//                 $$$"""
//                    Determine which participant takes the next turn in a conversation based on the the most recent participant.
//                    State only the name of the participant to take the next turn.
//                    No participant should take more than one turn in a row.
//
//                    Choose only from these participants:
//                    - {{{ReviewerName}}}
//                    - {{{CopyWriterName}}}
//
//                    Always follow these rules when selecting the next participant:
//                    - After {{{CopyWriterName}}}, it is {{{ReviewerName}}}'s turn.
//                    - After {{{ReviewerName}}}, it is {{{CopyWriterName}}}'s turn.
//
//                    History:
//                    {{$history}}
//                    """,
//                 safeParameterNames: "history");

                // Limit history used for selection and termination to the most recent message.
        ChatHistoryTruncationReducer strategyReducer = new(1);

        // Create a chat for agent interaction.
        AgentGroupChat chat =
            new(dataAnalyst, agent, checker)
            {
                ExecutionSettings =
                    new()
                    {
                        // Here KernelFunctionTerminationStrategy will terminate
                        // when the art-director has given their approval.
                        TerminationStrategy =
                            new KernelFunctionTerminationStrategy(terminationFunction, kernel)
                            {
                                // Only the art-director may approve.
                                Agents = [checker],
                                // Customer result parser to determine if the response is "yes"
                                ResultParser = (result) => result.GetValue<string>()?.Contains("yes", StringComparison.OrdinalIgnoreCase) ?? false,
                                // The prompt variable name for the history argument.
                                HistoryVariableName = "history",
                                // Limit total number of turns
                                MaximumIterations = 10,
                                // Save tokens by not including the entire history in the prompt
                                HistoryReducer = strategyReducer,
                            },
                    }
            };

        // Invoke chat and display messages.
        ChatMessageContent message = new(AuthorRole.User,
            """
                   MyTable has the following columns: DeviceId, DeviceType, ExposureScore, OsPlatformFriendlyName, HasTpmData, OsVersion.
                   Choose 10 different DeviceIds from MyTable, the objective is to maximize uniqueness. We want the devices to be as diverse as possible.
                   Look at the the following columns: `DeviceType, ExposureScore, OsPlatformFriendlyName, HasTpmData, OsVersion` to determine diverseness.
                   Run multiple queries if needed return results in as a list of DeviceIds");
                   """);
        chat.AddChatMessage(message);
        WriteAgentChatMessage(message);

        await foreach (ChatMessageContent responese in chat.InvokeAsync())
        {
            WriteAgentChatMessage(responese);
        }

        Console.WriteLine($"\n[IS COMPLETED: {chat.IsComplete}]");
    }

    private static Kernel CreateKernel()
    {
        var deploymentName = AppConfig.OpenAI.DeploymentName;
        var modelId = AppConfig.OpenAI.ModelId;
        var endpoint = AppConfig.OpenAI.Endpoint;
        var apiKey = AppConfig.OpenAI.ApiKey;

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


    private static void WriteAgentChatMessage(ChatMessageContent message)
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
            // else if (item is FunctionResultContent functionResult)
            // {
            //     Console.WriteLine($"  [{item.GetType().Name}] {functionResult.CallId} - {functionResult.Result?.AsJson() ?? "*"}");
            // }
        }
    }
}