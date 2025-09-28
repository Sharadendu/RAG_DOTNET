#pragma warning disable SKEXP0001, SKEXP0070

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.ChatCompletion;
using RAG.Configuration;

namespace RAG.Services;

public interface ISemanticKernelService
{
    Task InitializeAsync();
    Task<float[]> GenerateEmbeddingAsync(string text);
    Task<string> GenerateChatResponseAsync(string prompt, string context = "");
}

public class SemanticKernelService : ISemanticKernelService
{
    private readonly OllamaSettings _settings;
    private readonly ILogger<SemanticKernelService> _logger;
    private Kernel? _kernel;
    private ITextEmbeddingGenerationService? _embeddingService;
    private IChatCompletionService? _chatService;

    public SemanticKernelService(OllamaSettings settings, ILogger<SemanticKernelService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing Semantic Kernel with Ollama...");

            var builder = Kernel.CreateBuilder();

            // Add Ollama chat completion
            builder.AddOllamaChatCompletion(
                modelId: _settings.ModelName,
                endpoint: new Uri(_settings.Endpoint));

            // Add Ollama text embedding
            builder.AddOllamaTextEmbeddingGeneration(
                modelId: _settings.EmbeddingModelName,
                endpoint: new Uri(_settings.Endpoint));

            _kernel = builder.Build();

            // Get services
            _embeddingService = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
            _chatService = _kernel.GetRequiredService<IChatCompletionService>();

            // Test the connection
            await TestConnectionAsync();

            _logger.LogInformation("Semantic Kernel initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Semantic Kernel");
            throw;
        }
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            // Test embedding generation
            var testEmbedding = await _embeddingService!.GenerateEmbeddingAsync("test");
            _logger.LogInformation("Embedding service test successful. Embedding dimension: {Dimension}", testEmbedding.Length);

            // Test chat completion
            var testResponse = await _chatService!.GetChatMessageContentAsync("Hello, this is a test. Please respond with 'OK'.");
            _logger.LogInformation("Chat service test successful. Response: {Response}", testResponse.Content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Service test failed, but continuing...");
        }
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        if (_embeddingService == null)
            throw new InvalidOperationException("Semantic Kernel not initialized. Call InitializeAsync first.");

        try
        {
            _logger.LogDebug("Generating embedding for text of length {Length}", text.Length);
            
            var embedding = await _embeddingService.GenerateEmbeddingAsync(text);
            
            _logger.LogDebug("Generated embedding with {Dimensions} dimensions", embedding.Length);
            return embedding.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding");
            throw;
        }
    }

    public async Task<string> GenerateChatResponseAsync(string prompt, string context = "")
    {
        if (_chatService == null)
            throw new InvalidOperationException("Semantic Kernel not initialized. Call InitializeAsync first.");

        try
        {
            _logger.LogInformation("Generating chat response for prompt");

            var systemMessage = string.IsNullOrWhiteSpace(context) 
                ? "You are a helpful AI assistant."
                : $"You are a helpful AI assistant. Use the following context to answer questions accurately:\n\n{context}\n\nIf the context doesn't contain relevant information for the user's question, say so clearly.";

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemMessage);
            chatHistory.AddUserMessage(prompt);

            var response = await _chatService.GetChatMessageContentAsync(chatHistory);

            _logger.LogInformation("Generated chat response successfully");
            return response.Content ?? "I apologize, but I couldn't generate a response.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate chat response");
            throw;
        }
    }
}