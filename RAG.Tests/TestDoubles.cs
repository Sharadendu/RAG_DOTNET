using RAG.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace RAG.Tests;

public class FakeSemanticKernelService : ISemanticKernelService
{
    public List<string> EmbeddedTexts { get; } = new();
    public Task InitializeAsync() => Task.CompletedTask;
    public Task<float[]> GenerateEmbeddingAsync(string text)
    {
        EmbeddedTexts.Add(text);
        // return small deterministic vector based on length
        var len = text.Length;
        return Task.FromResult(new float[] { len % 10, (len / 10f) % 10, 1f });
    }
    public Task<string> GenerateChatResponseAsync(string prompt, string context = "")
        => Task.FromResult($"RESPONSE: {prompt} | CTXLEN={context.Length}");
}

public class InMemoryVectorStoreService : IVectorStoreService
{
    private readonly Dictionary<string, DocumentChunk> _store = new();
    public Task InitializeAsync() => Task.CompletedTask;
    public Task StoreDocumentsAsync(IEnumerable<DocumentChunk> documents)
    {
        foreach (var d in documents) _store[d.Id] = d;
        return Task.CompletedTask;
    }
    public Task<IEnumerable<DocumentChunk>> SearchAsync(float[] queryEmbedding, int maxResults = 5)
    {
        // naive: return first N items
        return Task.FromResult(_store.Values.Take(maxResults).AsEnumerable());
    }
    public Task<IReadOnlyList<(string Id, string Preview, Dictionary<string, object> Metadata)>> ListDocumentsAsync(int limit = 100, int offset = 0)
    {
        var list = _store.Values.Skip(offset).Take(limit)
            .Select(d => (d.Id, d.Content.Length > 40 ? d.Content[..40] + "..." : d.Content, d.Metadata))
            .ToList();
        return Task.FromResult((IReadOnlyList<(string, string, Dictionary<string, object>)>)list);
    }
    public Task<bool> DeleteDocumentAsync(string id)
    {
        var removed = _store.Remove(id);
        return Task.FromResult(removed);
    }
    public Task<int> DeleteAllAsync()
    {
        var count = _store.Count; _store.Clear(); return Task.FromResult(count);
    }
}
