using RAG.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RAG.Tests;

public class RagServiceTests
{
    private RagService CreateService(FakeSemanticKernelService? fsk = null, InMemoryVectorStoreService? store = null)
    {
        fsk ??= new FakeSemanticKernelService();
        store ??= new InMemoryVectorStoreService();
        return new RagService(fsk, store, NullLogger<RagService>.Instance);
    }

    [Fact]
    public async Task IngestDocuments_SplitsAndStoresChunks()
    {
        var fsk = new FakeSemanticKernelService();
        var store = new InMemoryVectorStoreService();
        var sut = CreateService(fsk, store);

        await sut.IngestDocumentsAsync(new[] { "Sentence one. Sentence two is here. Sentence three is also here." });

        var listed = await store.ListDocumentsAsync();
        listed.Count.Should().BeGreaterThan(0, "chunks should be produced");
        fsk.EmbeddedTexts.Should().NotBeEmpty();
    }

    [Fact]
    public async Task QueryAsync_ReturnsResponseEvenIfNoDocs()
    {
        var sut = CreateService();
        var response = await sut.QueryAsync("What is the meaning?");
        response.Should().Contain("RESPONSE:");
    }

    [Fact]
    public async Task ListDeleteOperations_Work()
    {
        var store = new InMemoryVectorStoreService();
        var fsk = new FakeSemanticKernelService();
        var sut = CreateService(fsk, store);

        await sut.IngestDocumentsAsync(new[] { "Alpha beta gamma.", "Delta epsilon zeta." });
        var before = await sut.ListDocumentsAsync();
        before.Count.Should().BeGreaterThan(0);

        var first = before.First();
        (await sut.DeleteDocumentAsync(first.Id)).Should().BeTrue();
        var afterDelete = await sut.ListDocumentsAsync();
        afterDelete.Count.Should().Be(before.Count - 1);

        var removedAll = await sut.DeleteAllDocumentsAsync();
        removedAll.Should().BeGreaterThan(0);
        var empty = await sut.ListDocumentsAsync();
        empty.Count.Should().Be(0);
    }
}
