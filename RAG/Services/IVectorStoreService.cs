namespace RAG.Services;

public interface IVectorStoreService
{
    Task InitializeAsync();
    Task StoreDocumentsAsync(IEnumerable<DocumentChunk> documents);
    Task<IEnumerable<DocumentChunk>> SearchAsync(float[] queryEmbedding, int maxResults = 5);
    Task<IReadOnlyList<(string Id, string Preview, Dictionary<string, object> Metadata)>> ListDocumentsAsync(int limit = 100, int offset = 0);
    Task<bool> DeleteDocumentAsync(string id);
    Task<int> DeleteAllAsync();
}

public class DocumentChunk
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public Dictionary<string, object> Metadata { get; set; } = new();
}