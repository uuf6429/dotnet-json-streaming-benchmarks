using System.Text.Json;
using Xunit;

using JsonStreamingBenchmarks;

namespace JsonStreamingBenchmarks.Tests;

public class BatchingLoaderTests : IDisposable
{
    private readonly string _tempDirectory;

    public BatchingLoaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BatchingLoaderTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Load_ExactMultipleBatchSize_SplitsEvenly()
    {
        var filePath = Path.Combine(_tempDirectory, "records.json");
        var items = Enumerable.Range(1, 10).Select(i => new TestRecord(i, $"Item_{i}", i)).ToList();
        File.WriteAllText(filePath, JsonSerializer.Serialize(items));

        var batchingLoader = new BatchingLoader();
        var fileLoader = new LazyJsonLoader();

        var batches = batchingLoader.Load<TestRecord>(fileLoader, filePath, batchSize: 5).ToList();

        Assert.Equal(2, batches.Count);
        Assert.Equal(5, batches[0].Count);
        Assert.Equal(5, batches[1].Count);
        Assert.Equal(items.Take(5), batches[0]);
        Assert.Equal(items.Skip(5).Take(5), batches[1]);
    }

    [Fact]
    public void Load_NonMultipleBatchSize_EmitsSmallerFinalBatch()
    {
        var filePath = Path.Combine(_tempDirectory, "records.json");
        var items = Enumerable.Range(1, 7).Select(i => new TestRecord(i, $"Item_{i}", i)).ToList();
        File.WriteAllText(filePath, JsonSerializer.Serialize(items));

        var batchingLoader = new BatchingLoader();
        var fileLoader = new LazyJsonLoader();

        var batches = batchingLoader.Load<TestRecord>(fileLoader, filePath, batchSize: 3).ToList();

        Assert.Equal(3, batches.Count);
        Assert.Equal(3, batches[0].Count);
        Assert.Equal(3, batches[1].Count);
        Assert.Single(batches[2]);
        Assert.Equal(items.Take(3), batches[0]);
        Assert.Equal(items.Skip(3).Take(3), batches[1]);
        Assert.Equal(items.Skip(6).Take(1), batches[2]);
    }

    [Fact]
    public void Load_BatchSizeLargerThanItemCount_EmitsSingleBatch()
    {
        var filePath = Path.Combine(_tempDirectory, "records.json");
        var items = Enumerable.Range(1, 3).Select(i => new TestRecord(i, $"Item_{i}", i)).ToList();
        File.WriteAllText(filePath, JsonSerializer.Serialize(items));

        var batchingLoader = new BatchingLoader();
        var fileLoader = new EagerJsonLoader();

        var batches = batchingLoader.Load<TestRecord>(fileLoader, filePath, batchSize: 10).ToList();

        Assert.Single(batches);
        Assert.Equal(3, batches[0].Count);
        Assert.Equal(items, batches[0]);
    }

    [Fact]
    public void Load_EmptySource_EmitsNoBatches()
    {
        var filePath = Path.Combine(_tempDirectory, "empty.json");
        File.WriteAllText(filePath, "[]");

        var batchingLoader = new BatchingLoader();
        var fileLoader = new LazyJsonLoader();

        var batches = batchingLoader.Load<TestRecord>(fileLoader, filePath, batchSize: 5).ToList();

        Assert.Empty(batches);
    }

    [Fact]
    public void Load_BatchSizeZeroOrNegative_ThrowsArgumentOutOfRangeException()
    {
        var filePath = Path.Combine(_tempDirectory, "records.json");
        File.WriteAllText(filePath, "[]");

        var batchingLoader = new BatchingLoader();
        var fileLoader = new LazyJsonLoader();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            batchingLoader.Load<TestRecord>(fileLoader, filePath, batchSize: 0).ToList());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            batchingLoader.Load<TestRecord>(fileLoader, filePath, batchSize: -1).ToList());
    }

    [Fact]
    public void Load_WithJsonLinesLoader_BatchesCorrectly()
    {
        var filePath = Path.Combine(_tempDirectory, "records.jsonl");
        var items = Enumerable.Range(1, 5).Select(i => new TestRecord(i, $"Item_{i}", i)).ToList();
        File.WriteAllLines(filePath, items.Select(r => JsonSerializer.Serialize(r)));

        var batchingLoader = new BatchingLoader();
        var fileLoader = new LazyJsonLinesLoader();

        var batches = batchingLoader.Load<TestRecord>(fileLoader, filePath, batchSize: 2).ToList();

        Assert.Equal(3, batches.Count);
        Assert.Equal(2, batches[0].Count);
        Assert.Equal(2, batches[1].Count);
        Assert.Single(batches[2]);
    }

    [Fact]
    public void Load_WithSkipBatchesAndTakeBatches_ReturnsCorrectBatches()
    {
        var filePath = Path.Combine(_tempDirectory, "records_paging.json");
        var items = Enumerable.Range(1, 25).Select(i => new TestRecord(i, $"Item_{i}", i)).ToList();
        File.WriteAllText(filePath, JsonSerializer.Serialize(items));

        var batchingLoader = new BatchingLoader();
        var fileLoader = new LazyJsonLoader();

        // Skip 2 batches of size 5 (skip 10 items), take 2 batches (take 10 items)
        var batches = batchingLoader.Load<TestRecord>(
            fileLoader,
            filePath,
            batchSize: 5,
            skipBatches: 2,
            takeBatches: 2).ToList();

        Assert.Equal(2, batches.Count);
        Assert.Equal(5, batches[0].Count);
        Assert.Equal(5, batches[1].Count);
        Assert.Equal(items.Skip(10).Take(5), batches[0]);
        Assert.Equal(items.Skip(15).Take(5), batches[1]);
    }

    [Fact]
    public void Load_WithNegativeSkipBatchesOrTakeBatches_ThrowsArgumentOutOfRangeException()
    {
        var filePath = Path.Combine(_tempDirectory, "records_args.json");
        File.WriteAllText(filePath, "[]");

        var batchingLoader = new BatchingLoader();
        var fileLoader = new LazyJsonLoader();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            batchingLoader.Load<TestRecord>(fileLoader, filePath, batchSize: 5, skipBatches: -1).ToList());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            batchingLoader.Load<TestRecord>(fileLoader, filePath, batchSize: 5, skipBatches: 0, takeBatches: -1).ToList());
    }

    [Fact]
    public void Load_WithTakeBatchesZero_ReturnsEmpty()
    {
        var filePath = Path.Combine(_tempDirectory, "records_take_zero.json");
        var items = Enumerable.Range(1, 10).Select(i => new TestRecord(i, $"Item_{i}", i)).ToList();
        File.WriteAllText(filePath, JsonSerializer.Serialize(items));

        var batchingLoader = new BatchingLoader();
        var fileLoader = new LazyJsonLoader();

        var batches = batchingLoader.Load<TestRecord>(
            fileLoader,
            filePath,
            batchSize: 5,
            skipBatches: 1,
            takeBatches: 0).ToList();

        Assert.Empty(batches);
    }
}
