using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RAG.Configuration;
using RAG.Services;
using UglyToad.PdfPig;

namespace RAG;

class Program
{
    private static IServiceProvider? _serviceProvider;
    private static ILogger<Program>? _logger;

    static async Task Main(string[] args)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables() // Allow environment variables to override JSON
            .Build();

        // Setup dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services, configuration);
        _serviceProvider = services.BuildServiceProvider();

        _logger = _serviceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            _logger.LogInformation("Starting RAG Console Application");

            // Initialize services
            var ragService = _serviceProvider.GetRequiredService<IRagService>();
            await ragService.InitializeAsync();

            // Show welcome message
            ShowWelcomeMessage();

            // Main application loop
            await RunMainLoop(ragService);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Application failed to start");
            Console.WriteLine($"❌ Application error: {ex.Message}");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
        finally
        {
            if (_serviceProvider is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Configuration
        var appSettings = new AppSettings();
        configuration.Bind(appSettings);

        // Post-bind fallback: if Host or ApiKey omitted or blank in config, take from environment or sensible defaults
        var envHost = Environment.GetEnvironmentVariable("QDRANT_ENDPOINT") ?? Environment.GetEnvironmentVariable("Qdrant_Endpoint");
        if (string.IsNullOrWhiteSpace(appSettings.Qdrant.Host))
        {
            appSettings.Qdrant.Host = !string.IsNullOrWhiteSpace(envHost) ? envHost : "localhost";
        }
        var envApiKey = Environment.GetEnvironmentVariable("QDRANT_API_KEY");
        if (string.IsNullOrWhiteSpace(appSettings.Qdrant.ApiKey) && !string.IsNullOrWhiteSpace(envApiKey))
        {
            appSettings.Qdrant.ApiKey = envApiKey;
        }
        services.AddSingleton(appSettings);
        services.AddSingleton(appSettings.Ollama);
        services.AddSingleton(appSettings.Qdrant);

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddConsole();
        });

        // Application services
        services.AddSingleton<ISemanticKernelService, SemanticKernelService>();
        services.AddSingleton<IVectorStoreService, QdrantService>();
        services.AddSingleton<IRagService, RagService>();
    }

    private static void ShowWelcomeMessage()
    {
        Console.Clear();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                  🤖 RAG Console Application                  ║");
        Console.WriteLine("║              Powered by Semantic Kernel & Ollama            ║");
        Console.WriteLine("║                     Vector Store: Qdrant                    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("🚀 Ready! Your RAG system is initialized and ready to use.");
        Console.WriteLine();
        ShowCommands();
    }

    private static void ShowCommands()
    {
        Console.WriteLine("Available commands:");
        Console.WriteLine("  📄 ingest    - Add documents to the knowledge base");
        Console.WriteLine("  📋 list      - List stored document chunks");
        Console.WriteLine("  🗑 delete    - Delete one or all document chunks");
        Console.WriteLine("  ❓ query     - Ask questions about your documents");
        Console.WriteLine("  ℹ️  help      - Show this help message");
        Console.WriteLine("  🚪 exit      - Exit the application");
        Console.WriteLine();
    }

    private static async Task RunMainLoop(IRagService ragService)
    {
        while (true)
        {
            Console.Write("RAG> ");
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            try
            {
                switch (input)
                {
                    case "exit" or "quit" or "q":
                        Console.WriteLine("👋 Goodbye!");
                        return;

                    case "help" or "h":
                        ShowCommands();
                        break;

                    case "ingest" or "i":
                        await HandleDocumentIngestion(ragService);
                        break;

                    case "list" or "ls":
                        await HandleListDocuments(ragService);
                        break;

                    case "delete" or "del":
                        await HandleDeleteDocuments(ragService);
                        break;

                    case "query" or "ask" or "q":
                        await HandleQuery(ragService);
                        break;

                    case "clear" or "cls":
                        Console.Clear();
                        ShowWelcomeMessage();
                        break;

                    default:
                        // Treat unknown input as a query
                        await HandleDirectQuery(ragService, input);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing command: {Command}", input);
                Console.WriteLine($"❌ Error: {ex.Message}");
            }

            Console.WriteLine();
        }
    }

    private static async Task HandleDocumentIngestion(IRagService ragService)
    {
        Console.WriteLine("\n📄 Document Ingestion");
        Console.WriteLine("====================");
        Console.WriteLine("You can:");
        Console.WriteLine("1. Enter text directly");
        Console.WriteLine("2. Specify a file path (e.g., 'file:C:\\path\\to\\document.txt' or 'pdf:C:\\path\\to\\doc.pdf')");
        Console.WriteLine("3. Enter multiple documents (type 'END' on a new line when finished)");
        Console.WriteLine();

        var documents = new List<string>();

        Console.WriteLine("Enter your document(s):");
        Console.WriteLine("(Type 'END' on a new line when finished, or press Ctrl+C to cancel)");

        string? line;
        var currentDocument = new List<string>();

        while ((line = Console.ReadLine()) != null)
        {
            if (line.Trim().Equals("END", StringComparison.OrdinalIgnoreCase))
                break;

            if (line.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                var filePath = line[5..].Trim();
                try
                {
                    if (File.Exists(filePath))
                    {
                        var fileContent = await File.ReadAllTextAsync(filePath);
                        documents.Add(fileContent);
                        Console.WriteLine($"✅ Loaded file: {filePath} ({fileContent.Length} characters)");
                    }
                    else
                    {
                        Console.WriteLine($"❌ File not found: {filePath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error reading file: {ex.Message}");
                }
            }
            else if (line.StartsWith("pdf:", StringComparison.OrdinalIgnoreCase))
            {
                var filePath = line[4..].Trim();
                try
                {
                    if (File.Exists(filePath))
                    {
                        documents.Add(await LoadPdfAsync(filePath));
                        Console.WriteLine($"✅ Loaded PDF: {filePath}");
                    }
                    else
                    {
                        Console.WriteLine($"❌ File not found: {filePath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error reading PDF: {ex.Message}");
                }
            }
            else
            {
                currentDocument.Add(line);
            }
        }

        // Add the current document if it has content
        if (currentDocument.Count > 0)
        {
            documents.Add(string.Join(Environment.NewLine, currentDocument));
        }

        if (documents.Count > 0)
        {
            Console.WriteLine($"\n🔄 Processing {documents.Count} document(s)...");
            
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await ragService.IngestDocumentsAsync(documents);
            stopwatch.Stop();

            Console.WriteLine($"✅ Successfully ingested {documents.Count} document(s) in {stopwatch.ElapsedMilliseconds}ms");
        }
        else
        {
            Console.WriteLine("ℹ️ No documents to process.");
        }
    }

    private static async Task<string> LoadPdfAsync(string path)
    {
        // Lazy simple extraction to avoid complicating service layer; a dedicated loader service could be added.
        return await Task.Run(() =>
        {
            try
            {
                using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
                var sb = new System.Text.StringBuilder();
                int pageNum = 1;
                foreach (var page in doc.GetPages())
                {
                    sb.AppendLine($"[Page {pageNum}]");
                    sb.AppendLine(page.Text);
                    sb.AppendLine();
                    pageNum++;
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"(Failed to parse PDF {Path.GetFileName(path)}: {ex.Message})";
            }
        });
    }

    private static async Task HandleListDocuments(IRagService ragService)
    {
        var docs = await ragService.ListDocumentsAsync(200);
        if (docs.Count == 0)
        {
            Console.WriteLine("(No documents found)");
            return;
        }
        Console.WriteLine($"Found {docs.Count} document chunks (showing up to 200):");
        int idx = 1;
        foreach (var d in docs)
        {
            Console.WriteLine($"[{idx}] ID={d.Id} | {d.Preview.Replace("\n", " ")}");
            idx++;
        }
    }

    private static async Task HandleDeleteDocuments(IRagService ragService)
    {
        Console.WriteLine("Delete options:");
        Console.WriteLine("  1) Delete a single document by number");
        Console.WriteLine("  2) Delete all documents");
        Console.Write("Choose (1/2 or Enter to cancel): ");
        var choice = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(choice)) return;

        if (choice == "2")
        {
            Console.Write("Are you sure you want to delete ALL documents? Type 'YES' to confirm: ");
            var confirm = Console.ReadLine();
            if (confirm == "YES")
            {
                await ragService.DeleteAllDocumentsAsync();
                Console.WriteLine("All documents deletion requested.");
            }
            else
            {
                Console.WriteLine("Cancelled.");
            }
            return;
        }

        if (choice == "1")
        {
            var docs = await ragService.ListDocumentsAsync(200);
            if (docs.Count == 0)
            {
                Console.WriteLine("No documents to delete.");
                return;
            }
            int idx = 1;
            foreach (var d in docs)
            {
                Console.WriteLine($"[{idx}] {d.Id} | {d.Preview.Replace("\n", " ")}");
                idx++;
            }
            Console.Write("Enter number to delete (or 0 to cancel): ");
            var numStr = Console.ReadLine();
            if (int.TryParse(numStr, out var num) && num > 0 && num <= docs.Count)
            {
                var target = docs[num - 1];
                Console.Write($"Confirm delete of ID {target.Id}? Type 'YES' to confirm: ");
                var confirm = Console.ReadLine();
                if (confirm == "YES")
                {
                    var ok = await ragService.DeleteDocumentAsync(target.Id);
                    Console.WriteLine(ok ? "Deleted." : "Delete failed.");
                }
                else
                {
                    Console.WriteLine("Cancelled.");
                }
            }
            else
            {
                Console.WriteLine("Cancelled.");
            }
        }
    }

    private static async Task HandleQuery(IRagService ragService)
    {
        Console.WriteLine("\n❓ Query Mode");
        Console.WriteLine("=============");
        Console.WriteLine("Ask any question about your ingested documents.");
        Console.WriteLine("(Press Enter without typing to return to main menu)");
        Console.WriteLine();

        Console.Write("Your question: ");
        var question = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(question))
        {
            Console.WriteLine("ℹ️ Returning to main menu.");
            return;
        }

        await ProcessQuery(ragService, question);
    }

    private static async Task HandleDirectQuery(IRagService ragService, string question)
    {
        Console.WriteLine($"\n❓ Processing: {question}");
        Console.WriteLine(new string('=', Math.Min(50, question.Length + 15)));
        await ProcessQuery(ragService, question);
    }

    private static async Task ProcessQuery(IRagService ragService, string question)
    {
        Console.WriteLine("🤔 Thinking...");
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await ragService.QueryAsync(question);
        stopwatch.Stop();

        Console.WriteLine($"\n🤖 Response ({stopwatch.ElapsedMilliseconds}ms):");
        Console.WriteLine(new string('-', 50));
        
        // Format the response nicely
        var lines = response.Split('\n');
        foreach (var line in lines)
        {
            if (line.Trim().Length > 0)
            {
                Console.WriteLine($"   {line.Trim()}");
            }
            else
            {
                Console.WriteLine();
            }
        }
        
        Console.WriteLine(new string('-', 50));
    }
}