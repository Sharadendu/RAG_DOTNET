using Microsoft.Extensions.Logging;

namespace RAG.Services;

public interface IRagService
{
    Task InitializeAsync();
    Task IngestDocumentsAsync(IEnumerable<string> documents);
    Task<string> QueryAsync(string question);
    Task<IReadOnlyList<(string Id, string Preview, Dictionary<string, object> Metadata)>> ListDocumentsAsync(int limit = 100);
    Task<bool> DeleteDocumentAsync(string id);
    Task<int> DeleteAllDocumentsAsync();
}

public class RagService : IRagService
{
    private readonly ISemanticKernelService _semanticKernelService;
    private readonly IVectorStoreService _vectorStoreService;
    private readonly ILogger<RagService> _logger;

    public RagService(
        ISemanticKernelService semanticKernelService,
        IVectorStoreService vectorStoreService,
        ILogger<RagService> logger)
    {
        _semanticKernelService = semanticKernelService;
        _vectorStoreService = vectorStoreService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing RAG service...");

        await Task.WhenAll(
            _semanticKernelService.InitializeAsync(),
            _vectorStoreService.InitializeAsync()
        );

        _logger.LogInformation("RAG service initialized successfully");
    }

    public async Task IngestDocumentsAsync(IEnumerable<string> documents)
    {
        var documentList = documents.ToList();
        _logger.LogInformation("Starting document ingestion for {Count} documents", documentList.Count);

        var documentChunks = new List<DocumentChunk>();

        foreach (var (document, index) in documentList.Select((doc, idx) => (doc, idx)))
        {
            try
            {
                _logger.LogInformation("Processing document {Index}/{Total}", index + 1, documentList.Count);

                // Split document into chunks (simple approach - can be enhanced)
                var chunks = SplitIntoChunks(document, maxChunkSize: 1000, overlapSize: 100);
                
                foreach (var (chunk, chunkIndex) in chunks.Select((c, idx) => (c, idx)))
                {
                    // Generate embedding for chunk
                    var embedding = await _semanticKernelService.GenerateEmbeddingAsync(chunk);
                    
                    var documentChunk = new DocumentChunk
                    {
                        Id = $"doc_{index}_chunk_{chunkIndex}",
                        Content = chunk,
                        Embedding = embedding,
                        Metadata = new Dictionary<string, object>
                        {
                            { "document_index", index },
                            { "chunk_index", chunkIndex },
                            { "chunk_size", chunk.Length },
                            { "ingested_at", DateTime.UtcNow.ToString("O") }
                        }
                    };

                    documentChunks.Add(documentChunk);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process document {Index}", index + 1);
            }
        }

        // Store all chunks in vector database
        await _vectorStoreService.StoreDocumentsAsync(documentChunks);

        _logger.LogInformation("Document ingestion completed. Processed {ChunkCount} chunks from {DocumentCount} documents", 
            documentChunks.Count, documentList.Count);
    }

    public async Task<string> QueryAsync(string question)
    {
        _logger.LogInformation("Processing query: {Question}", question);

        try
        {
            // Generate embedding for the question
            var questionEmbedding = await _semanticKernelService.GenerateEmbeddingAsync(question);

            // Retrieve relevant documents from vector store
            var relevantDocuments = await _vectorStoreService.SearchAsync(questionEmbedding, maxResults: 5);
            var relevantDocumentsList = relevantDocuments.ToList();

            _logger.LogInformation("Retrieved {Count} relevant documents", relevantDocumentsList.Count);

            // Build context from retrieved documents
            var context = BuildContext(relevantDocumentsList);

            // Generate response using the retrieved context
            var response = await _semanticKernelService.GenerateChatResponseAsync(question, context);

            _logger.LogInformation("Generated response for query");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process query");
            return "I apologize, but I encountered an error while processing your question. Please try again.";
        }
    }

    public Task<IReadOnlyList<(string Id, string Preview, Dictionary<string, object> Metadata)>> ListDocumentsAsync(int limit = 100)
        => _vectorStoreService.ListDocumentsAsync(limit);

    public Task<bool> DeleteDocumentAsync(string id) => _vectorStoreService.DeleteDocumentAsync(id);

    public Task<int> DeleteAllDocumentsAsync() => _vectorStoreService.DeleteAllAsync();

    private List<string> SplitIntoChunks(string text, int maxChunkSize = 1000, int overlapSize = 100)
    {
        var chunks = new List<string>();
        
        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        // Simple chunking strategy - split by sentences and group them
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(s => s.Trim() + ".")
                           .Where(s => s.Length > 1)
                           .ToList();

        var currentChunk = "";
        
        foreach (var sentence in sentences)
        {
            if (currentChunk.Length + sentence.Length <= maxChunkSize)
            {
                currentChunk += " " + sentence;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(currentChunk))
                {
                    chunks.Add(currentChunk.Trim());
                }

                // Start new chunk with overlap from previous chunk if possible
                var words = currentChunk.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0 && overlapSize > 0)
                {
                    var overlapWords = words.TakeLast(Math.Min(overlapSize / 10, words.Length / 2));
                    currentChunk = string.Join(" ", overlapWords) + " " + sentence;
                }
                else
                {
                    currentChunk = sentence;
                }
            }
        }

        // Add the last chunk
        if (!string.IsNullOrWhiteSpace(currentChunk))
        {
            chunks.Add(currentChunk.Trim());
        }

        return chunks;
    }

    private string BuildContext(List<DocumentChunk> relevantDocuments)
    {
        if (!relevantDocuments.Any())
        {
            return "No relevant context found.";
        }

        var context = "Relevant information:\n\n";
        
        foreach (var (document, index) in relevantDocuments.Select((doc, idx) => (doc, idx)))
        {
            context += $"[Context {index + 1}]\n{document.Content}\n\n";
            
            // Add distance information if available
            if (document.Metadata.TryGetValue("distance", out var distance))
            {
                _logger.LogDebug("Document {Index} similarity distance: {Distance}", index + 1, distance);
            }
        }

        return context;
    }
}