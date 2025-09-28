# RAG Console Application

A C# console application that implements Retrieval-Augmented Generation (RAG) using Semantic Kernel, Ollama, and ChromaDB.

## Features

- 🤖 **Semantic Kernel Integration**: Uses Microsoft Semantic Kernel for orchestrating AI operations
- 🦙 **Ollama Support**: Connects to local Ollama instance for LLM and embedding generation
- 🔍 **ChromaDB Vector Store**: Stores and retrieves document embeddings using ChromaDB
- 📚 **Document Ingestion**: Support for text input and file-based document ingestion
- ❓ **Interactive Querying**: Natural language querying with context-aware responses
- 📊 **Chunking Strategy**: Intelligent document chunking with overlap for better context retention

## Prerequisites

### 1. Ollama Setup
Install and run Ollama with required models:

```bash
# Install Ollama (https://ollama.ai/)

# Pull the required models
ollama pull llama3.1          # For chat completion
ollama pull nomic-embed-text  # For embeddings
```

Make sure Ollama is running on `http://localhost:11434`

### 2. ChromaDB Setup
Run ChromaDB using Docker:

```bash
# Run ChromaDB on port 8000
docker run -p 8000:8000 chromadb/chroma:latest
```

Or install ChromaDB directly:
```bash
pip install chromadb
chroma run --host localhost --port 8000
```

## Configuration

The application uses `appsettings.json` for configuration:

```json
{
  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "ModelName": "llama3.1",
    "EmbeddingModelName": "nomic-embed-text"
  },
  "ChromaDB": {
    "Endpoint": "http://localhost:8000"
  }
}
```

## Usage

### 1. Build and Run
```bash
dotnet build
dotnet run
```

### 2. Available Commands

- **`ingest`** - Add documents to the knowledge base
  - Enter text directly
  - Load from file using `file:/path/to/document.txt`
  - Add multiple documents (type 'END' when finished)

- **`query`** - Ask questions about your documents
  - Interactive query mode
  - Context-aware responses based on ingested documents

- **`help`** - Show available commands

- **`exit`** - Exit the application

### 3. Example Workflow

1. Start the application
2. Use `ingest` to add some documents:
   ```
   RAG> ingest
   Enter your document(s):
   
   Machine learning is a subset of artificial intelligence that focuses on algorithms 
   that can learn from data without being explicitly programmed.
   
   END
   ```

3. Query your documents:
   ```
   RAG> query
   Your question: What is machine learning?
   
   🤖 Response:
   Machine learning is a subset of artificial intelligence that focuses on algorithms 
   that can learn from data without being explicitly programmed, according to the 
   context provided.
   ```

## Architecture

### Services

- **`RagService`** - Main orchestration service that coordinates document ingestion and querying
- **`SemanticKernelService`** - Handles Ollama integration for chat completion and embeddings
- **`ChromaDBService`** - Manages vector storage and retrieval operations
- **`DocumentChunk`** - Represents a chunk of a document with its embedding and metadata

### Key Features

1. **Smart Chunking**: Documents are split into overlapping chunks for better context retention
2. **Vector Search**: Semantic similarity search using embeddings
3. **Context Building**: Retrieved chunks are used to build context for LLM responses
4. **Error Handling**: Comprehensive error handling and logging
5. **Configurable**: Easy configuration through appsettings.json

## Troubleshooting

### Common Issues

1. **Ollama Connection Error**
   - Ensure Ollama is running on the correct port
   - Verify the required models are pulled

2. **ChromaDB Connection Error**
   - Check if ChromaDB is running on port 8000
   - Verify Docker container is accessible

3. **Model Not Found**
   - Run `ollama list` to check available models
   - Pull missing models using `ollama pull <model-name>`

### Logging

The application provides detailed logging. Check the console output for:
- Service initialization status
- Document ingestion progress
- Query processing details
- Error messages and stack traces

## Extending the Application

### Adding New Document Sources
Extend the ingestion logic in `RagService.IngestDocumentsAsync()` to support:
- PDF files
- Web scraping
- Database connections
- APIs

### Customizing Chunking Strategy
Modify `SplitIntoChunks()` method in `RagService` to implement:
- Semantic chunking
- Different overlap strategies
- Custom chunk sizes

### Adding More Vector Stores
Implement `IVectorStoreService` to support:
- Pinecone
- Weaviate
- Azure Cognitive Search
- Other vector databases

## Dependencies

- Microsoft.SemanticKernel
- Microsoft.SemanticKernel.Connectors.Ollama
- Microsoft.Extensions.*
- System.Text.Json

## License

This project is open source. Feel free to modify and distribute as needed.

## Vector Store (Qdrant)

New commands:
- list : Shows up to 200 stored document chunks with IDs and previews
- delete : Interactive deletion
  - Option 1: Delete a single chunk by its listed number (with confirmation)
  - Option 2: Delete all chunks (requires typing YES)

Configuration sample (appsettings.json):
```
"Qdrant": {
  "Host": "ac668052-4b57-450e-bade-80aee4c1dceb.us-east4-0.gcp.cloud.qdrant.io",
  "Port": 6334,          // gRPC port (REST assumed 6333 automatically)
  "UseTls": true,
  "ApiKey": "<your-api-key>"
}
```

If using Qdrant Cloud ensure you supply ApiKey. REST operations for listing/deleting are performed against port 6333 (automatically inferred when 6334 specified).