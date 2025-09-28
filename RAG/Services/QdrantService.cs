using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using RAG.Configuration;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace RAG.Services;

public class QdrantService : IVectorStoreService
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantService> _logger;
    private readonly string _collectionName = "rag_documents";
    private readonly QdrantSettings _settings;
    private readonly HttpClient _http;
    private readonly string _restBase;
    private bool _documentIdIndexEnsured;

    public QdrantService(QdrantSettings settings, ILogger<QdrantService> logger)
    {
        _settings = settings;
        _logger = logger;
        
        if (!string.IsNullOrEmpty(_settings.ApiKey))
        {
            _client = new QdrantClient($"{_settings.Host}", https: _settings.UseTls, apiKey: _settings.ApiKey);
        }
        else
        {
            _client = new QdrantClient($"{_settings.Host}", https: _settings.UseTls);
        }

        _http = new HttpClient();
        var scheme = _settings.UseTls ? "https" : "http";
        // Qdrant default REST port is 6333; if user supplied gRPC port 6334, map to REST automatically
        var restPort = _settings.Port == 6334 ? 6333 : _settings.Port;
        _restBase = $"{scheme}://{_settings.Host}:{restPort}";
        if (!string.IsNullOrEmpty(_settings.ApiKey))
        {
            _http.DefaultRequestHeaders.Add("api-key", _settings.ApiKey);
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing Qdrant connection to {Host}:{Port}...", _settings.Host, _settings.Port);
            
            // Check if collection exists
            var collections = await _client.ListCollectionsAsync();
            var collectionExists = collections.Contains(_collectionName);

            if (!collectionExists)
            {
                _logger.LogInformation("Creating new Qdrant collection '{CollectionName}'...", _collectionName);
                
                // Create collection with vector configuration
                // Assuming 384 dimensions for nomic-embed-text model - adjust if different
                await _client.CreateCollectionAsync(_collectionName, new VectorParams
                {
                    Size = 384, // Common embedding dimension, adjust based on your model
                    Distance = Distance.Cosine
                });
                
                _logger.LogInformation("Created new Qdrant collection '{CollectionName}'", _collectionName);
            }
            else
            {
                _logger.LogInformation("Qdrant collection '{CollectionName}' already exists", _collectionName);
            }

            // Make sure we have an index for document_id so we can filter/delete efficiently
            await EnsureDocumentIdIndexAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Qdrant");
            throw;
        }
    }

    public async Task StoreDocumentsAsync(IEnumerable<DocumentChunk> documents)
    {
        try
        {
            var documentList = documents.ToList();
            _logger.LogInformation("Storing {Count} documents in Qdrant", documentList.Count);

            var points = new List<PointStruct>();

            foreach (var doc in documentList)
            {
                var payload = new Dictionary<string, Value>
                {
                    ["content"] = doc.Content,
                    ["document_id"] = doc.Id,
                    ["ingested_at"] = ConvertToQdrantValue(DateTime.UtcNow)
                };

                // Add metadata to payload
                foreach (var (key, value) in doc.Metadata)
                {
                    // avoid overwriting system keys
                    if (!payload.ContainsKey(key))
                        payload[key] = ConvertToQdrantValue(value);
                }

                // Use a generated UUID for the actual point id (Qdrant requires valid UUID format when using Uuid field)
                // Keep original logical chunk id in payload (document_id) for listing/deleting via filter.
                var point = new PointStruct
                {
                    Id = new PointId { Uuid = Guid.NewGuid().ToString() },
                    Vectors = doc.Embedding,
                    Payload = { payload }
                };

                points.Add(point);
            }

            await _client.UpsertAsync(_collectionName, points);
            
            _logger.LogInformation("Successfully stored {Count} documents", documentList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store documents in Qdrant");
            throw;
        }
    }

    public async Task<IEnumerable<DocumentChunk>> SearchAsync(float[] queryEmbedding, int maxResults = 5)
    {
        try
        {
            _logger.LogInformation("Searching Qdrant for similar documents (max results: {MaxResults})", maxResults);

            var searchResult = await _client.SearchAsync(_collectionName, queryEmbedding, 
                limit: (ulong)maxResults, 
                payloadSelector: true, 
                vectorsSelector: false);

            var documents = new List<DocumentChunk>();

            foreach (var point in searchResult)
            {
                var content = point.Payload.TryGetValue("content", out var contentValue) 
                    ? contentValue.StringValue 
                    : string.Empty;
                
                var documentId = point.Payload.TryGetValue("document_id", out var idValue) 
                    ? idValue.StringValue 
                    : Guid.NewGuid().ToString();

                var metadata = new Dictionary<string, object>
                {
                    ["score"] = point.Score,
                    ["qdrant_id"] = point.Id.ToString()
                };

                // Extract other metadata
                foreach (var (key, value) in point.Payload)
                {
                    if (key != "content" && key != "document_id")
                    {
                        metadata[key] = ConvertFromQdrantValue(value);
                    }
                }

                documents.Add(new DocumentChunk
                {
                    Id = documentId,
                    Content = content,
                    Metadata = metadata,
                    Embedding = Array.Empty<float>() // We don't need to return embeddings
                });
            }

            _logger.LogInformation("Found {Count} similar documents", documents.Count);
            return documents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search Qdrant");
            throw;
        }
    }

    public async Task<IReadOnlyList<(string Id, string Preview, Dictionary<string, object> Metadata)>> ListDocumentsAsync(int limit = 100, int offset = 0)
    {
        try
        {
            var collected = new List<(string Id, string Preview, Dictionary<string, object> Metadata)>();
            object? pageOffset = null;
            int remaining = limit;
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            while (remaining > 0)
            {
                var batchSize = Math.Min(remaining, 64);
                var request = new
                {
                    limit = batchSize,
                    with_payload = true,
                    with_vector = false,
                    offset = pageOffset,
                    filter = (object?)null
                };

                var json = JsonSerializer.Serialize(request, options);
                var resp = await _http.PostAsync($"{_restBase}/collections/{_collectionName}/points/scroll", new StringContent(json, Encoding.UTF8, "application/json"));
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Scroll request failed: {Status} {Body}", resp.StatusCode, body);
                    break;
                }
                var envelope = JsonSerializer.Deserialize<QdrantEnvelope<QdrantScrollResult>>(body, new JsonSerializerOptions{PropertyNameCaseInsensitive = true});
                var scroll = envelope?.Result;
                if (scroll?.Points == null || scroll.Points.Count == 0) break;

                foreach (var p in scroll.Points)
                {
                    var payload = p.Payload ?? new Dictionary<string, object>();
                    var id = payload.TryGetValue("document_id", out var idObj) ? idObj?.ToString() ?? p.Id : p.Id;
                    var content = payload.TryGetValue("content", out var contentObj) ? contentObj?.ToString() ?? string.Empty : string.Empty;
                    var preview = content.Length > 80 ? content[..80] + "…" : content;
                    var metadata = new Dictionary<string, object>();
                    foreach (var kv in payload)
                    {
                        if (kv.Key is not ("content" or "document_id")) metadata[kv.Key] = kv.Value!;
                    }
                    // Include underlying point id for transparency if different
                    if (!metadata.ContainsKey("point_id") && id != p.Id)
                        metadata["point_id"] = p.Id;
                    collected.Add((id, preview, metadata));
                    if (collected.Count >= limit) break;
                }

                if (collected.Count >= limit || scroll.NextPageOffset == null) break;
                pageOffset = scroll.NextPageOffset;
                remaining = limit - collected.Count;
            }

            return collected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list documents");
            throw;
        }
    }

    public async Task<bool> DeleteDocumentAsync(string id)
    {
        try
        {
            // Delete by filter on payload field 'document_id' to avoid dependence on internal point id
            var request = new
            {
                filter = new
                {
                    must = new object[]
                    {
                        new { key = "document_id", match = new { value = id } }
                    }
                }
            };
            var json = JsonSerializer.Serialize(request);
            var resp = await _http.PostAsync($"{_restBase}/collections/{_collectionName}/points/delete", new StringContent(json, Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                // If index missing, try to create it and retry once
                if ((int)resp.StatusCode == 400 && body.Contains("Index required", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Index for document_id missing. Creating index and retrying delete of {Id}.", id);
                    await EnsureDocumentIdIndexAsync(force:true);
                    resp = await _http.PostAsync($"{_restBase}/collections/{_collectionName}/points/delete", new StringContent(json, Encoding.UTF8, "application/json"));
                    body = await resp.Content.ReadAsStringAsync();
                    if (resp.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Delete request succeeded after creating index for document_id. Id={Id}", id);
                        return true;
                    }
                }

                // Fallback: brute-force scan points (limited batches) to find underlying point ids and delete by ids
                _logger.LogInformation("Falling back to point-id delete for document_id={Id}", id);
                var pointIds = await FindPointIdsByDocumentIdAsync(id, 500);
                if (pointIds.Count > 0)
                {
                    var delByIdsReq = new { points = pointIds };
                    var delJson = JsonSerializer.Serialize(delByIdsReq);
                    var delResp = await _http.PostAsync($"{_restBase}/collections/{_collectionName}/points/delete", new StringContent(delJson, Encoding.UTF8, "application/json"));
                    var delBody = await delResp.Content.ReadAsStringAsync();
                    if (delResp.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Deleted {Count} point(s) for document_id={Id} via direct ids.", pointIds.Count, id);
                        return true;
                    }
                    _logger.LogWarning("Fallback delete by ids failed for {Id}: {Status} {Body}", id, delResp.StatusCode, delBody);
                }
                _logger.LogWarning("Delete document {Id} failed: {Status} {Body}", id, resp.StatusCode, body);
                return false;
            }
            _logger.LogInformation("Delete request acknowledged for document payload id {Id}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document {Id}", id);
            return false;
        }
    }

    public async Task<int> DeleteAllAsync()
    {
        try
        {
            int existing = await TryGetPointsCountAsync();
            var request = new { filter = new { } }; // empty filter matches all
            var json = JsonSerializer.Serialize(request);
            var resp = await _http.PostAsync($"{_restBase}/collections/{_collectionName}/points/delete", new StringContent(json, Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Delete-all failed: {Status} {Body}", resp.StatusCode, body);
                return -1;
            }
            _logger.LogWarning("Issued delete-all request for collection {Collection}. Previous count estimate: {Count}", _collectionName, existing);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete all documents in collection {Collection}", _collectionName);
            throw;
        }
    }

    private static Value ConvertToQdrantValue(object obj) => obj switch
    {
        string s => new Value { StringValue = s },
        int i => new Value { IntegerValue = i },
        long l => new Value { IntegerValue = l },
        double d => new Value { DoubleValue = d },
        float f => new Value { DoubleValue = f },
        bool b => new Value { BoolValue = b },
        DateTime dt => new Value { StringValue = dt.ToString("O") },
        _ => new Value { StringValue = obj?.ToString() ?? string.Empty }
    };

    private static object ConvertFromQdrantValue(Value value) => value.KindCase switch
    {
        Value.KindOneofCase.StringValue => value.StringValue,
        Value.KindOneofCase.IntegerValue => value.IntegerValue,
        Value.KindOneofCase.DoubleValue => value.DoubleValue,
        Value.KindOneofCase.BoolValue => value.BoolValue,
        _ => value.ToString()
    };

    public void Dispose() => _client?.Dispose();

    private async Task<int> TryGetPointsCountAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"{_restBase}/collections/{_collectionName}");
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return -1;
            var info = JsonSerializer.Deserialize<QdrantEnvelope<QdrantCollectionInfo>>(body, new JsonSerializerOptions{PropertyNameCaseInsensitive = true});
            return info?.Result?.PointsCount ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    private async Task EnsureDocumentIdIndexAsync(bool force = false)
    {
        if (_documentIdIndexEnsured && !force) return;
        try
        {
            var body = JsonSerializer.Serialize(new { field_name = "document_id", field_schema = "keyword" });
            var resp = await _http.PostAsync($"{_restBase}/collections/{_collectionName}/index", new StringContent(body, Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("Ensured payload index for document_id");
                _documentIdIndexEnsured = true;
            }
            else
            {
                var respBody = await resp.Content.ReadAsStringAsync();
                // If already exists, treat as success
                if (respBody.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    _documentIdIndexEnsured = true;
                }
                else
                {
                    _logger.LogDebug("Index creation for document_id returned {Status}: {Body}", resp.StatusCode, respBody);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to ensure document_id index (continuing)");
        }
    }

    private async Task<List<string>> FindPointIdsByDocumentIdAsync(string documentId, int maxScan)
    {
        var found = new List<string>();
        object? pageOffset = null;
        int scanned = 0;
        while (scanned < maxScan)
        {
            var req = new { limit = 64, with_payload = true, with_vector = false, offset = pageOffset };
            var json = JsonSerializer.Serialize(req);
            var resp = await _http.PostAsync($"{_restBase}/collections/{_collectionName}/points/scroll", new StringContent(json, Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) break;
            var envelope = JsonSerializer.Deserialize<QdrantEnvelope<QdrantScrollResult>>(body, new JsonSerializerOptions{PropertyNameCaseInsensitive = true});
            var scroll = envelope?.Result;
            if (scroll?.Points == null || scroll.Points.Count == 0) break;
            foreach (var p in scroll.Points)
            {
                var payload = p.Payload;
                if (payload != null && payload.TryGetValue("document_id", out var val) && val?.ToString() == documentId)
                {
                    found.Add(p.Id);
                }
            }
            scanned += scroll.Points.Count;
            if (scroll.NextPageOffset == null || found.Count > 0) break; // stop after first match to minimize scan
            pageOffset = scroll.NextPageOffset;
        }
        return found;
    }
}

internal sealed class QdrantEnvelope<T>
{
    public T? Result { get; set; }
    public string? Status { get; set; }
}

internal sealed class QdrantScrollResult
{
    public List<QdrantPoint> Points { get; set; } = new();
    public object? NextPageOffset { get; set; }
}

internal sealed class QdrantPoint
{
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, object>? Payload { get; set; }
}

internal sealed class QdrantCollectionInfo
{
    public int PointsCount { get; set; }
}