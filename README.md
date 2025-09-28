# RAG Console Application (Semantic Kernel + Ollama + Qdrant)

End‑to‑end Retrieval‑Augmented Generation (RAG) sample in C# (.NET 8) featuring:

* Microsoft Semantic Kernel for orchestration (embeddings + chat)
* Local Ollama models (LLM + embedding model)
* Qdrant vector database (collection: `rag_documents`)
* Text + file + PDF ingestion with automatic chunking & metadata
* Interactive CLI: ingest, query, list, delete

This README goes beyond a quick start: it explains internals, rationale behind design choices, trade‑offs, and how to extend / tune the system for production‑like scenarios.

🤖 Semantic Kernel Integration: Uses Microsoft Semantic Kernel for orchestrating AI operations
🦙 Ollama Support: Connects to local Ollama instance for LLM and embedding generation
🔍 QdrantDB Vector Store: Stores and retrieves document embeddings using Qdrant
📚 Document Ingestion: Support for text input and file-based document ingestion
❓ Interactive Querying: Natural language querying with context-aware responses
📊 Chunking Strategy: Intelligent document chunking with overlap for better context retention


## 🌟 Features (Overview)

| Icon | Feature | Description |
|------|---------|-------------|
| 🤖 | Semantic Orchestration | Unified handling of embeddings + chat via Semantic Kernel abstraction |
| 🦙 | Local LLM & Embeddings | Runs fully local with Ollama (privacy + rapid iteration) |
| 📄 | Multi‑Source Ingestion | Inline text, plaintext files, PDFs (page-aware) in one session |
| ✂️ | Smart Chunking | Sentence grouping with overlap to preserve semantic continuity |
| 🧠 | Vector Similarity Search | Cosine similarity retrieval over dense embeddings in Qdrant |
| 🗂 | Rich Metadata | Each chunk tagged with indices, timestamps, and logical IDs |
| 🔍 | Context Grounding | Retrieved chunks assembled into structured system prompt |
| 🗑 | Managed Deletion | Single or bulk delete with index + fallback strategies |
| 📋 | Listing & Inspection | Scroll-based enumeration (up to user-defined cap) |
| 🧱 | Resilience | Auto index creation; fallback delete path if filters fail |
| 🔐 | Cloud Ready | TLS + API key support for Qdrant Cloud |
| ⚙️ | Configurable | Environment overrides + easy extension points |
| 🧪 | Testable Design | Clear seams for mocking vector store / LLM in future tests |
| 🚀 | Performance Conscious | Batching strategy readiness & minimal allocations |

## 🧰 Tech Stack

| Layer | Technology | Purpose |
|-------|------------|---------|
| Runtime | .NET 8 | Modern, fast, cross-platform managed runtime |
| AI Orchestration | Microsoft Semantic Kernel | Uniform interface for chat + embeddings |
| LLM / Embeddings | Ollama (llama3.1 / nomic-embed-text) | Local model execution |
| Vector Store | Qdrant | High-performance similarity search + filtering |
| PDF Parsing | UglyToad.PdfPig | Text extraction from PDFs (non-OCR) |
| DI / Config / Logging | Microsoft.Extensions.* | Standard .NET host building blocks |
| Serialization | System.Text.Json | Fast JSON handling for REST calls |
| Networking | Qdrant.Client + HttpClient | gRPC (collection mgmt) + REST (list/delete) |

Design Principle Snapshot:
* Keep core domain (RagService) free of transport/storage specifics.
* Push database specifics into `QdrantService` via narrow interface `IVectorStoreService`.
* Prefer composition over inheritance; each service has a single responsibility.
* Fail fast on initialization issues (model unavailable, collection mismatch) while keeping query path lean.

---

## ✨ High‑Level Flow

1. You ingest raw content (typed text, `file:...`, or `pdf:...`).
2. Content is split into overlapping semantic-ish chunks.
3. Each chunk is embedded using the Ollama embedding model (`nomic-embed-text` by default).
4. Chunks + embeddings + metadata are stored as points in Qdrant.
5. A user query is embedded; similarity search retrieves top N chunks.
6. Retrieved chunks are assembled into a context block.
7. Chat model (`llama3.1` default) generates an answer grounded in that context.

Conceptual Diagram (text form):
```
+----------------+        +-----------------------+        +------------------+
|  User Input    | -----> |  RagService (orches.) | -----> | Embedding Model  |
+----------------+        +-----------------------+        +------------------+
  |                           |                               |
  |                           v                               |
  |                  +-----------------+                      |
  |                  |  Chunk Builder  |                      |
  |                  +-----------------+                      |
  |                           |                               |
  |                           v                               v
  |                   +------------------+          +--------------------+
  |                   |  QdrantService   | <------> |   Qdrant (Vector)  |
  |                   +------------------+          +--------------------+
  |                           ^                               |
  |                           | (Top-K)                        |
  v                           |                                v
+----------------+        +-----------------------+        +------------------+
|   CLI Output   | <----- |  Response Assembler   | <----- |  Chat Completion  |
+----------------+        +-----------------------+        +------------------+
```

---

## ✅ Feature Summary

| Category | Capabilities |
|----------|--------------|
| Ingestion | Multi-doc session input, plain text, local file, PDF parsing (PdfPig) |
| Chunking | Length-based with sentence grouping + configurable overlap |
| Embeddings | Ollama embedding model via Semantic Kernel abstraction |
| Storage | Qdrant (Cosine distance, payload metadata, keyword index on `document_id`) |
| Query | Top‑K semantic similarity → context → grounded LLM answer |
| Management | List (paged scroll), delete single (filter), delete all (bulk) with safe confirmations |
| Resilience | Automatic index creation, fallback delete path (scan + point-id) |
| Logging | Initialization, ingestion timing, query timing, search hits |

Additional Non-Functional Characteristics:
* Deterministic logical chunk IDs (`doc_{n}_chunk_{m}`) decoupled from Qdrant's point UUIDs.
* Separation of concerns (vector store vs orchestration vs LLM binding) enables swapping components independently.
* Minimal external dependencies to keep footprint small.
* Progressive hardening: index creation, safe delete confirmations, fallback delete path.

---

## 🔧 Prerequisites

### 1. Install & Run Ollama
```bash
ollama pull llama3.1           # Chat model
ollama pull nomic-embed-text   # Embedding model
```
Ollama must be reachable at `http://localhost:11434` (default).

### 2. Qdrant
You can use:
* Local Docker
  ```bash
  docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant:latest
  ```
* Or Qdrant Cloud (obtain API Key + endpoint host)

### 3. .NET SDK
Install .NET 8 SDK: https://dotnet.microsoft.com/en-us/download

---

## ⚙️ Configuration (`appsettings.json`)

```json
{
  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "ModelName": "llama3.1",
    "EmbeddingModelName": "nomic-embed-text"
  },
  "Qdrant": {
    "Host": "localhost",
    "Port": 6334,          // gRPC port; REST (6333) inferred automatically
    "UseTls": false,
    "ApiKey": ""          // Set for cloud usage
  }
}
```

Environment variable overrides (optional):
* `QDRANT_ENDPOINT` / `Qdrant_Endpoint` → Host fallback
* `QDRANT_API_KEY` → ApiKey fallback

Advanced Configuration Ideas (not yet implemented but easy to add):
* `EmbeddingCache:Enabled` – reuse computed embeddings for identical chunks.
* `Chunking:MaxSize` / `Chunking:Overlap` – externalize current constants.
* `Query:TopK` – adjust number of retrieved chunks per query.
* `Collection:VectorSize` – guard runtime dimension mismatches (validate on startup).
* `Answer:MaxContextChars` – truncate overly large context to control prompt cost.

---

## ▶️ Build & Run

```bash
dotnet build
dotnet run --project RAG
```

You should see a welcome banner and the command list.

---

## 🖥 CLI Commands

| Command | Aliases | Description |
|---------|---------|-------------|
| ingest  | i       | Multi-line ingestion session (text / file / pdf) |
| list    | ls      | Show up to N stored chunks (default examples show 200) |
| delete  | del     | Delete one chunk (interactive) or ALL (guarded) |
| query   | ask     | Ask a question, retrieves context + answers |
| help    | h       | Show command help |
| exit    | quit,q  | Exit program |

### Ingest Examples
```
RAG> ingest
📄 Document Ingestion
file:C:\docs\intro.txt
pdf:C:\docs\guide.pdf
This is an inline paragraph I want to store.
END
```
Explanation:
* Each `file:` or `pdf:` line is ingested as a separate source document.
* Inline paragraphs entered before `END` are concatenated into one document.
* Mixed modes are allowed (text + multiple files in a single session).
* PDF pages are annotated with `[Page N]` to preserve ordering cues for retrieval.

### Query Example
```
RAG> query
Your question: What does the PDF say about architecture?

🤖 Response (512ms):
--------------------------------------------------
  <Grounded answer referencing retrieved chunks>
--------------------------------------------------
```
Explanation:
* System collects top similarity matches.
* Chunks are injected into a system prompt the model receives.
* If context has no relevant info, the system prompt instructs the model to admit uncertainty (avoid hallucination).
* Latency shown is the end‑to‑end time: embed question + vector search + LLM answer.

### List & Delete
```
RAG> list
Found 15 document chunks (showing up to 200):
[1] ID=doc_0_chunk_0 | [Page 1] This system uses Semantic Kernel…
[2] ID=doc_0_chunk_1 | Overlapping chunking improves recall…
...

RAG> delete
Delete options:
  1) Delete a single document by number
  2) Delete all documents
Choose (1/2 or Enter to cancel): 1
Enter number to delete (or 0 to cancel): 2
Confirm delete of ID doc_0_chunk_1? Type 'YES' to confirm: YES
Deleted.
```
Explanation:
* Listing uses paged `scroll` endpoint (stops when limit or dataset end reached).
* Displayed ID is the logical `document_id` payload; underlying Qdrant UUID stays internal.
* Single delete filters on `document_id`; if index missing, it is created then retried.
* Fallback (scan + point id delete) ensures robustness even if indexing fails.

---

## 🧱 Architecture Breakdown

### 1. `Program` (Composition Root / CLI)
* Builds configuration + DI container.
* Binds `AppSettings` → environment overrides for Qdrant host/API key.
* Initializes `IRagService` then enters an interactive loop.
* Implements handlers: ingestion, query, list, delete, direct single-line query.
* PDF handling done inline via PdfPig (`pdf:` prefix) for simplicity.

### 2. `RagService`
Responsible for orchestration:
* Splits input documents into overlapping chunks (simple sentence heuristics).
* Calls `ISemanticKernelService` for embeddings.
* Persists chunks through `IVectorStoreService` (`QdrantService`).
* For queries: embed question → vector search → context assembly → ask LLM.
* Returns grounded answer text.

Chunking Logic (simplified):
* Split on sentence terminators (`. ! ?`).
* Aggregate until size threshold (~1000 chars) reached.
* Start a new chunk; carry small overlap (words back-calculated from desired char overlap) to preserve context.

### 3. `SemanticKernelService`
* Configures a Semantic Kernel instance with:
  * Ollama Chat Completion (LLM)
  * Ollama Embedding Generation
* Provides two methods:
  * `GenerateEmbeddingAsync(text)` → float[]
  * `GenerateChatResponseAsync(prompt, context)` → grounded answer
* Performs a light connectivity test during initialization (embedding + short chat).

### 4. `QdrantService`
Core vector store implementation:
* Creates collection `rag_documents` if absent (assumes embedding dimension 384 — adjust if your model differs).
* Ensures payload index on `document_id` (keyword) for fast filtered delete / listing.
* Stores each chunk as a point:
  * Random UUID point id
  * Payload: `content`, `document_id` (logical ID like `doc_0_chunk_3`), `ingested_at`, plus chunk metadata.
* Search: cosine similarity → returns top matches with scores.
* Listing: uses REST `/points/scroll` with pagination until limit.
* Delete single: filter on `document_id`; if index missing → create → retry; fallback: scan + point-id delete.
* Delete all: empty filter delete request.
* Defensive coding: if a delete by filter fails with index error, index is created and the operation is retried automatically.
* Minimizes read amplification by limiting scroll batch size to 64 and halting early when enough results are collected.

### 5. Data Model (`DocumentChunk`)
```
Id: logical chunk id (doc_{N}_chunk_{M})
Content: raw text of the chunk
Embedding: float[] vector (not kept on retrieval results)
Metadata: dictionary (chunk_index, size, ingested_at, etc.)
```

### 6. PDF Ingestion
* Triggered by input line starting with `pdf:`.
* Uses PdfPig to extract page text sequentially.
* Adds simple page headers `[Page N]` to preserve pagination context.
* Full text then flows through the standard chunking + embedding pipeline.
* Trade‑off: Entire PDF combined first then chunked; a future optimization could stream pages → embed incrementally to reduce peak memory.
* For scanned/image PDFs you must add OCR (e.g., Tesseract) before ingestion.

---

## 🔍 Example End-to-End (Pseudo Trace)
1. User enters: `pdf:C:\whitepaper.pdf` + `END`.
2. `Program` reads & extracts PDF text → adds to documents list.
3. `RagService.IngestDocumentsAsync` splits into 12 chunks.
4. For each chunk → embedding (dim=384) → collect.
5. `QdrantService.StoreDocumentsAsync` upserts 12 points.
6. Query: "Explain the architecture goals" → embed (query vector).
7. Qdrant search returns 5 chunks with highest cosine similarity.
8. Context builder wraps each chunk with `[Context i]` markers.
9. LLM receives system prompt containing those contexts.
10. Response streamed (internally) → combined answer printed.

Detailed Timing (illustrative, will vary):
```
PDF extraction:            80 ms
Sentence splitting:        15 ms
Chunk assembly (12):       5 ms
Embedding generation:      12 x 45 ms = 540 ms
Qdrant upsert:             40 ms
---------------------------------
Ingestion total:           ~680 ms

Query embedding:           45 ms
Vector search (top 5):     25 ms
Prompt assembly:           2 ms
LLM answer (short):        350 ms
---------------------------------
Query total:               ~422 ms
```
Numbers depend on hardware, model size, and Ollama caching.

---

## 🛠 Adjusting / Extending

| Goal | Where | Notes |
|------|-------|-------|
| Change embedding model | `appsettings.json` | Ensure dimension matches collection or recreate collection |
| Different chunk size | `RagService.SplitIntoChunks` | Tune `maxChunkSize` & overlap heuristic |
| Add HTML ingestion | `Program.HandleDocumentIngestion` | Add `html:` prefix + parser, reuse ingestion pipeline |
| Switch to another vector DB | New `IVectorStoreService` implementation | Keep method contract consistent |
| Add citations | Modify `BuildContext` + answer formatting | Include chunk IDs in final output |
| Add source provenance | Add `source_path` to payload | Display in list + cite in answers |
| Streaming answers | Wrap chat call with incremental token callback | Print tokens as they arrive |
| Add evaluation | New test harness project | Compute retrieval precision / MRR |
| Multi-collection support | Parameterize collection name | Useful for multi-tenant scenarios |

---

## ⚠️ Common Pitfalls & Fixes

| Symptom | Cause | Fix |
|---------|-------|-----|
| Empty / generic answers | No matching chunks or context too small | Verify ingestion; increase chunk size; ingest more data |
| Delete by id not working | Missing payload index | Service auto-creates; re-run delete; check logs |
| 400 error on delete | Filter needs indexed field | Index creation logic handles; else fallback kicks in |
| Low relevance | Embedding dimension mismatch | Confirm model dimension (e.g., 384) matches collection params |
| PDF gibberish | Scanned / image-based PDF | Needs OCR layer (add Tesseract wrapper) |
| High memory on huge docs | Entire doc buffered before chunking | Stream pages, flush chunks incrementally |
| Duplicate ingestion | Same file added multiple times | Hash content & skip if previously stored |
| Slow deletes at scale | Large collection & no index | Ensure `document_id` index present (auto) |
| Mixed embedding sizes | Switched model mid-run | Recreate collection or store vector size per point (multi-vector feature) |

---

## 🔐 Qdrant Cloud Notes
* Set `UseTls: true` and supply `ApiKey`.
* Provide host (no protocol) e.g. `your-id.eu-central.aws.cloud.qdrant.io`.
* gRPC port (6334) configured in settings; REST assumed at 6333 internally for management calls.
* Consider enabling TLS certificate verification (default when `UseTls=true`) – avoid plain HTTP for production.
* Rate limits: batch operations (upsert in groups) reduce request overhead.

---
## 🚀 Performance & Tuning

| Area | Lever | Effect |
|------|-------|--------|
| Chunk Size | Increase from 1000→1500 | Fewer embeddings, risk of context dilution |
| Overlap | Reduce if many chunks | Faster ingestion, slightly lower cross-chunk recall |
| TopK | Tune per query complexity | Higher recall vs longer prompt & latency |
| Embedding Model | Faster (smaller) model | Lower latency, potential accuracy drop |
| Parallel Embeddings | Task.WhenAll for batches | Throughput increase; watch CPU/GPU saturation |
| Collection Reuse | Keep single collection | Simplifies ops vs per-topic collections |

Potential Optimization (not implemented): Batch embedding generation by sending multiple texts to a model that supports it (Semantic Kernel Ollama connector currently processes sequential single calls).

---
## 🧪 Suggested Automated Tests (Future)

* IngestionRoundTrip: ingest known text → query phrase → expect snippet present.
* DeleteSingle: ingest 2 docs, delete 1 chunk, ensure only remaining chunk returned.
* DeleteAll: ingest N, delete all, verify list empty.
* DimensionMismatchGuard: simulate wrong vector size → expect initialization error.
* PDFParseSmoke: small PDF with 2 pages → ensure `[Page 2]` marker present in stored content.

---
## ❓ FAQ

**Q: Why fix vector size at 384?**  
Because `nomic-embed-text` outputs 384‑dim embeddings. Qdrant collection vectors must match. If you switch models (e.g., 768 dims) recreate the collection.

**Q: Can I store original full documents?**  
Yes—add another payload field or separate blob storage and a `source_id` in payload.

**Q: How do I avoid re‑embedding unchanged docs?**  
Compute a SHA256 hash of raw content; store it; skip if hash already seen.

**Q: Why not store chunk vectors in memory too?**  
Unnecessary after persistence; retrieval uses Qdrant. Keeping them in memory increases RAM with little gain.

**Q: How do I add citations in answers?**  
Include `[Context n]` markers already present; append `(Source: doc_X_chunk_Y)` inline or footnote style.

**Q: How to support streaming answers?**  
Use Semantic Kernel's streaming APIs (or directly call Ollama via HTTP streaming) and write tokens as they arrive.

---

## 🧪 Testing Ideas (Manual)
1. Ingest small text; query an exact phrase → expect direct retrieval.
2. Ingest large PDF; query term from late pages → verify chunk recall.
3. Delete one chunk; re-run similar query → answer quality may degrade slightly.
4. Delete all; query again → expect "No relevant context found." message influence.

---

## 📦 Dependencies
* Microsoft.SemanticKernel.Connectors.Ollama
* Qdrant.Client
* UglyToad.PdfPig
* Microsoft.Extensions.* (Configuration, Logging, DI)
* System.Text.Json
* (Optional future) Tesseract (OCR), AngleSharp (HTML), Azure.Storage.Blobs (remote source persistence)

---

## 🗺 Historical Note
Project originally scaffolded with ChromaDB; migrated to Qdrant for richer indexing and operational features. References to Chroma removed from runtime code; this README reflects the current (Qdrant) implementation.

---

## 📝 License
Open source – use, adapt, extend freely. Attribution appreciated but not required.

---

## 🙋 Support / Next Steps
Ideas to explore:
* Add streaming output for responses
* Implement hybrid search (keyword + vector)
* Embed citation markers with chunk IDs
* Persist original source file metadata (path, type)
* Add evaluation harness (precision / recall tests)
* Implement embedding cache (hash → vector)
* Add multi-turn conversation history preservation
* Add guardrails (prompt injection detection, context length truncation)

Enjoy building with Semantic Kernel + Qdrant! 🚀