namespace RAG.Configuration;

public class AppSettings
{
    public OllamaSettings Ollama { get; set; } = new();
    public QdrantSettings Qdrant { get; set; } = new();
}

public class OllamaSettings
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string ModelName { get; set; } = "llama3.1";
    public string EmbeddingModelName { get; set; } = "nomic-embed-text";
}

public class QdrantSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 6334; // gRPC port by default
    public bool UseTls { get; set; } = false;
    public string? ApiKey { get; set; }
}