using System.Diagnostics;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace JsonStreamingBenchmarks.Tests;

public class PerformanceComparisonTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDirectory;
    private readonly string _jsonArrayFilePath;
    private readonly string _jsonLinesFilePath;
    private const int LargeDatasetSize = 30_000;

    public PerformanceComparisonTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PerfTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);

        _jsonArrayFilePath = Path.Combine(_tempDirectory, "large_dataset.json");
        _jsonLinesFilePath = Path.Combine(_tempDirectory, "large_dataset.jsonl");

        GenerateTestData();
    }

    private void GenerateTestData()
    {
        var records = Enumerable.Range(1, LargeDatasetSize)
            .Select(i => new TestRecord(i, $"Entity_Name_{i:D6}_LongDescription_{new string('A', 40)}", i * 1.25))
            .ToList();

        // Write JSON Array
        using (var stream = File.Create(_jsonArrayFilePath))
        {
            JsonSerializer.Serialize(stream, records);
        }

        // Write JSON Lines
        using (var writer = new StreamWriter(_jsonLinesFilePath))
        {
            foreach (var record in records)
            {
                writer.WriteLine(JsonSerializer.Serialize(record));
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void PartialRead_JsonArray_LazyIsSignificantlyFasterThanEager()
    {
        const int takeCount = 10;
        var eagerLoader = new EagerJsonLoader();
        var lazyLoader = new LazyJsonLoader();

        // Warm up / JIT
        _ = lazyLoader.Load<TestRecord>(_jsonArrayFilePath).Take(1).ToList();

        // Measure Lazy Loader (Time-to-first-N items)
        var lazySw = Stopwatch.StartNew();
        long lazyAllocStart = GC.GetAllocatedBytesForCurrentThread();
        var lazyResults = lazyLoader.Load<TestRecord>(_jsonArrayFilePath).Take(takeCount).ToList();
        lazySw.Stop();
        long lazyAllocated = GC.GetAllocatedBytesForCurrentThread() - lazyAllocStart;

        // Measure Eager Loader (Must load and deserialize entire file)
        var eagerSw = Stopwatch.StartNew();
        long eagerAllocStart = GC.GetAllocatedBytesForCurrentThread();
        var eagerResults = eagerLoader.Load<TestRecord>(_jsonArrayFilePath).Take(takeCount).ToList();
        eagerSw.Stop();
        long eagerAllocated = GC.GetAllocatedBytesForCurrentThread() - eagerAllocStart;

        _output.WriteLine($"[JSON Array Partial Read ({takeCount} / {LargeDatasetSize} items)]");
        _output.WriteLine(
            $"  Eager Loader:  {eagerSw.ElapsedMilliseconds} ms, {eagerAllocated / 1024:N0} KB allocated");
        _output.WriteLine($"  Lazy Loader:   {lazySw.ElapsedMilliseconds} ms, {lazyAllocated / 1024:N0} KB allocated");

        Assert.Equal(takeCount, lazyResults.Count);
        Assert.Equal(takeCount, eagerResults.Count);
        Assert.Equal(eagerResults, lazyResults);

        // Lazy should be substantially faster and allocate drastically fewer bytes
        Assert.True(lazySw.Elapsed < eagerSw.Elapsed,
            $"Lazy loader ({lazySw.ElapsedMilliseconds}ms) was expected to be faster than eager loader ({eagerSw.ElapsedMilliseconds}ms).");
        Assert.True(lazyAllocated < eagerAllocated,
            $"Lazy loader ({lazyAllocated} bytes) was expected to allocate less memory than eager loader ({eagerAllocated} bytes).");
    }

    [Fact]
    public void PartialRead_JsonLines_LazyIsSignificantlyFasterThanEager()
    {
        const int takeCount = 10;
        var eagerLoader = new EagerJsonLinesLoader();
        var lazyLoader = new LazyJsonLinesLoader();

        // Warm up / JIT
        _ = lazyLoader.Load<TestRecord>(_jsonLinesFilePath).Take(1).ToList();

        // Measure Lazy Loader
        var lazySw = Stopwatch.StartNew();
        long lazyAllocStart = GC.GetAllocatedBytesForCurrentThread();
        var lazyResults = lazyLoader.Load<TestRecord>(_jsonLinesFilePath).Take(takeCount).ToList();
        lazySw.Stop();
        long lazyAllocated = GC.GetAllocatedBytesForCurrentThread() - lazyAllocStart;

        // Measure Eager Loader
        var eagerSw = Stopwatch.StartNew();
        long eagerAllocStart = GC.GetAllocatedBytesForCurrentThread();
        var eagerResults = eagerLoader.Load<TestRecord>(_jsonLinesFilePath).Take(takeCount).ToList();
        eagerSw.Stop();
        long eagerAllocated = GC.GetAllocatedBytesForCurrentThread() - eagerAllocStart;

        _output.WriteLine($"[JSON Lines Partial Read ({takeCount} / {LargeDatasetSize} items)]");
        _output.WriteLine(
            $"  Eager Loader:  {eagerSw.ElapsedMilliseconds} ms, {eagerAllocated / 1024:N0} KB allocated");
        _output.WriteLine($"  Lazy Loader:   {lazySw.ElapsedMilliseconds} ms, {lazyAllocated / 1024:N0} KB allocated");

        Assert.Equal(takeCount, lazyResults.Count);
        Assert.Equal(takeCount, eagerResults.Count);
        Assert.Equal(eagerResults, lazyResults);

        Assert.True(lazySw.Elapsed < eagerSw.Elapsed,
            $"Lazy JSON Lines loader ({lazySw.ElapsedMilliseconds}ms) was expected to be faster than eager loader ({eagerSw.ElapsedMilliseconds}ms).");
        Assert.True(lazyAllocated < eagerAllocated,
            $"Lazy JSON Lines loader ({lazyAllocated} bytes) was expected to allocate less memory than eager loader ({eagerAllocated} bytes).");
    }

    [Fact]
    public void FirstBatchLatency_BatchingLoader_LazyYieldsFirstBatchFarSooner()
    {
        var batchingLoader = new BatchingLoader();
        var eagerLoader = new EagerJsonLoader();
        var lazyLoader = new LazyJsonLoader();

        // Time to first batch with Lazy loader
        var lazySw = Stopwatch.StartNew();
        using var lazyEnumerator = batchingLoader.Load<TestRecord>(lazyLoader, _jsonArrayFilePath, batchSize: 50)
            .GetEnumerator();
        Assert.True(lazyEnumerator.MoveNext());
        lazySw.Stop();
        var lazyFirstBatch = lazyEnumerator.Current;

        // Time to first batch with Eager loader
        var eagerSw = Stopwatch.StartNew();
        using var eagerEnumerator = batchingLoader.Load<TestRecord>(eagerLoader, _jsonArrayFilePath, batchSize: 50)
            .GetEnumerator();
        Assert.True(eagerEnumerator.MoveNext());
        eagerSw.Stop();
        var eagerFirstBatch = eagerEnumerator.Current;

        _output.WriteLine($"[First Batch Latency (batchSize: 50 from {LargeDatasetSize} items)]");
        _output.WriteLine($"  Eager Loader Batching: {eagerSw.ElapsedMilliseconds} ms");
        _output.WriteLine($"  Lazy Loader Batching:   {lazySw.ElapsedMilliseconds} ms");

        Assert.Equal(50, lazyFirstBatch.Count);
        Assert.Equal(50, eagerFirstBatch.Count);
        Assert.Equal(eagerFirstBatch, lazyFirstBatch);

        Assert.True(lazySw.Elapsed < eagerSw.Elapsed,
            $"Lazy batching first item latency ({lazySw.ElapsedMilliseconds}ms) was expected to be faster than eager ({eagerSw.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public void Pagination_JsonArray_NativeSkipIsFarMoreEfficientThanLinqSkip()
    {
        const int skipCount = 10_000;
        const int takeCount = 50;
        var lazyLoader = new LazyJsonLoader();
        var eagerLoader = new EagerJsonLoader();

        // Warm up / JIT
        _ = lazyLoader.Load<TestRecord>(_jsonArrayFilePath, skip: 1, take: 1).ToList();

        // 1. Native Lazy Loader Pagination (Skips tokens in buffer without deserializing skipped items)
        var nativeSw = Stopwatch.StartNew();
        long nativeAllocStart = GC.GetAllocatedBytesForCurrentThread();
        var nativeResults = lazyLoader.Load<TestRecord>(_jsonArrayFilePath, skip: skipCount, take: takeCount).ToList();
        nativeSw.Stop();
        long nativeAllocated = GC.GetAllocatedBytesForCurrentThread() - nativeAllocStart;

        // 2. LINQ Skip on Lazy Loader (Streams file, but deserializes all 10,000 skipped items into C# objects)
        var linqSw = Stopwatch.StartNew();
        long linqAllocStart = GC.GetAllocatedBytesForCurrentThread();
        var linqResults = lazyLoader.Load<TestRecord>(_jsonArrayFilePath).Skip(skipCount).Take(takeCount).ToList();
        linqSw.Stop();
        long linqAllocated = GC.GetAllocatedBytesForCurrentThread() - linqAllocStart;

        // 3. Eager Loader Pagination (Loads and deserializes entire dataset)
        var eagerSw = Stopwatch.StartNew();
        long eagerAllocStart = GC.GetAllocatedBytesForCurrentThread();
        var eagerResults = eagerLoader.Load<TestRecord>(_jsonArrayFilePath, skip: skipCount, take: takeCount).ToList();
        eagerSw.Stop();
        long eagerAllocated = GC.GetAllocatedBytesForCurrentThread() - eagerAllocStart;

        _output.WriteLine(
            $"[JSON Array Pagination (Skip {skipCount:N0}, Take {takeCount} of {LargeDatasetSize:N0} items)]");
        _output.WriteLine(
            $"  Eager Loader (Full File Parse): {eagerSw.ElapsedMilliseconds} ms, {eagerAllocated / 1024:N0} KB allocated");
        _output.WriteLine(
            $"  Lazy Loader + LINQ .Skip():     {linqSw.ElapsedMilliseconds} ms, {linqAllocated / 1024:N0} KB allocated");
        _output.WriteLine(
            $"  Lazy Loader Native .Load():     {nativeSw.ElapsedMilliseconds} ms, {nativeAllocated / 1024:N0} KB allocated");

        Assert.Equal(takeCount, nativeResults.Count);
        Assert.Equal(takeCount, linqResults.Count);
        Assert.Equal(takeCount, eagerResults.Count);
        Assert.Equal(linqResults, nativeResults);
        Assert.Equal(eagerResults, nativeResults);

        // Native skip must allocate far fewer bytes than LINQ Skip (which created 10,000 discarded objects)
        Assert.True(nativeAllocated < linqAllocated,
            $"Native skip ({nativeAllocated} bytes) was expected to allocate less memory than LINQ Skip ({linqAllocated} bytes).");
        Assert.True(nativeSw.Elapsed <= linqSw.Elapsed || nativeSw.ElapsedMilliseconds < 50,
            $"Native skip ({nativeSw.ElapsedMilliseconds}ms) was expected to be faster or equal to LINQ Skip ({linqSw.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public void Pagination_JsonLines_NativeSkipIsFarMoreEfficientThanLinqSkip()
    {
        const int skipCount = 10_000;
        const int takeCount = 50;
        var lazyLoader = new LazyJsonLinesLoader();
        var eagerLoader = new EagerJsonLinesLoader();

        // Warm up / JIT
        _ = lazyLoader.Load<TestRecord>(_jsonLinesFilePath, skip: 1, take: 1).ToList();

        // 1. Native Lazy JSON Lines Pagination (Reads and discards lines without deserialization)
        var nativeSw = Stopwatch.StartNew();
        long nativeAllocStart = GC.GetAllocatedBytesForCurrentThread();
        var nativeResults = lazyLoader.Load<TestRecord>(_jsonLinesFilePath, skip: skipCount, take: takeCount).ToList();
        nativeSw.Stop();
        long nativeAllocated = GC.GetAllocatedBytesForCurrentThread() - nativeAllocStart;

        // 2. LINQ Skip on Lazy JSON Lines (Deserializes all 10,000 skipped lines)
        var linqSw = Stopwatch.StartNew();
        long linqAllocStart = GC.GetAllocatedBytesForCurrentThread();
        var linqResults = lazyLoader.Load<TestRecord>(_jsonLinesFilePath).Skip(skipCount).Take(takeCount).ToList();
        linqSw.Stop();
        long linqAllocated = GC.GetAllocatedBytesForCurrentThread() - linqAllocStart;

        // 3. Eager Loader
        var eagerSw = Stopwatch.StartNew();
        long eagerAllocStart = GC.GetAllocatedBytesForCurrentThread();
        var eagerResults = eagerLoader.Load<TestRecord>(_jsonLinesFilePath, skip: skipCount, take: takeCount).ToList();
        eagerSw.Stop();
        long eagerAllocated = GC.GetAllocatedBytesForCurrentThread() - eagerAllocStart;

        _output.WriteLine(
            $"[JSON Lines Pagination (Skip {skipCount:N0}, Take {takeCount} of {LargeDatasetSize:N0} items)]");
        _output.WriteLine(
            $"  Eager Loader (Full File Parse): {eagerSw.ElapsedMilliseconds} ms, {eagerAllocated / 1024:N0} KB allocated");
        _output.WriteLine(
            $"  Lazy Loader + LINQ .Skip():     {linqSw.ElapsedMilliseconds} ms, {linqAllocated / 1024:N0} KB allocated");
        _output.WriteLine(
            $"  Lazy Loader Native .Load():     {nativeSw.ElapsedMilliseconds} ms, {nativeAllocated / 1024:N0} KB allocated");

        Assert.Equal(takeCount, nativeResults.Count);
        Assert.Equal(takeCount, linqResults.Count);
        Assert.Equal(takeCount, eagerResults.Count);
        Assert.Equal(linqResults, nativeResults);
        Assert.Equal(eagerResults, nativeResults);

        Assert.True(nativeAllocated < linqAllocated,
            $"Native skip ({nativeAllocated} bytes) was expected to allocate less memory than LINQ Skip ({linqAllocated} bytes).");
    }

    [Fact]
    public void BatchPagination_BatchingLoader_NativeSkipBatchesIsMoreEfficient()
    {
        const int batchSize = 100;
        const int skipBatches = 100; // Skips 10,000 items
        const int takeBatches = 1; // Takes 100 items
        var batchingLoader = new BatchingLoader();
        var lazyLoader = new LazyJsonLoader();

        // 1. Native batch skipping
        var nativeSw = Stopwatch.StartNew();
        long nativeAllocStart = GC.GetAllocatedBytesForCurrentThread();
        var nativeBatches = batchingLoader.Load<TestRecord>(
            lazyLoader,
            _jsonArrayFilePath,
            batchSize: batchSize,
            skipBatches: skipBatches,
            takeBatches: takeBatches).ToList();
        nativeSw.Stop();
        long nativeAllocated = GC.GetAllocatedBytesForCurrentThread() - nativeAllocStart;

        // 2. LINQ batch skipping
        var linqSw = Stopwatch.StartNew();
        long linqAllocStart = GC.GetAllocatedBytesForCurrentThread();
        var linqBatches = batchingLoader.Load<TestRecord>(
                lazyLoader,
                _jsonArrayFilePath,
                batchSize: batchSize)
            .Skip(skipBatches)
            .Take(takeBatches)
            .ToList();
        linqSw.Stop();
        long linqAllocated = GC.GetAllocatedBytesForCurrentThread() - linqAllocStart;

        _output.WriteLine($"[Batch Pagination (Skip {skipBatches} batches of {batchSize}, Take {takeBatches} batch)]");
        _output.WriteLine(
            $"  BatchingLoader + LINQ .Skip(): {linqSw.ElapsedMilliseconds} ms, {linqAllocated / 1024:N0} KB allocated");
        _output.WriteLine(
            $"  BatchingLoader Native Skip:    {nativeSw.ElapsedMilliseconds} ms, {nativeAllocated / 1024:N0} KB allocated");

        Assert.Single(nativeBatches);
        Assert.Single(linqBatches);
        Assert.Equal(batchSize, nativeBatches[0].Count);
        Assert.Equal(linqBatches[0], nativeBatches[0]);

        Assert.True(nativeAllocated < linqAllocated,
            $"Native batch skip ({nativeAllocated} bytes) was expected to allocate less memory than LINQ batch skip ({linqAllocated} bytes).");
    }

    [Fact]
    public void Throughput_FullDataset_MeasuresItemsPerSecondAcrossLoaders()
    {
        const int batchSize = 1_000;
        var eagerJsonLoader = new EagerJsonLoader();
        var lazyJsonLoader = new LazyJsonLoader();
        var eagerJsonLinesLoader = new EagerJsonLinesLoader();
        var lazyJsonLinesLoader = new LazyJsonLinesLoader();
        var batchingLoader = new BatchingLoader();

        // Warm up / JIT
        _ = eagerJsonLoader.Load<TestRecord>(_jsonArrayFilePath).Take(10).ToList();
        _ = lazyJsonLoader.Load<TestRecord>(_jsonArrayFilePath).Take(10).ToList();
        _ = eagerJsonLinesLoader.Load<TestRecord>(_jsonLinesFilePath).Take(10).ToList();
        _ = lazyJsonLinesLoader.Load<TestRecord>(_jsonLinesFilePath).Take(10).ToList();
        _ = batchingLoader.Load<TestRecord>(lazyJsonLoader, _jsonArrayFilePath, batchSize: 10).Take(1).ToList();

        // 1. JSON Array - Eager Loader
        var eagerArraySw = Stopwatch.StartNew();
        long eagerArrayAllocStart = GC.GetAllocatedBytesForCurrentThread();
        int eagerArrayCount = 0;
        foreach (var unused in eagerJsonLoader.Load<TestRecord>(_jsonArrayFilePath))
        {
            eagerArrayCount++;
        }

        eagerArraySw.Stop();
        long eagerArrayAllocated = GC.GetAllocatedBytesForCurrentThread() - eagerArrayAllocStart;
        double eagerArrayThroughput = eagerArrayCount / eagerArraySw.Elapsed.TotalSeconds;

        // 2. JSON Array - Lazy Loader (Item-by-item streaming)
        var lazyArraySw = Stopwatch.StartNew();
        long lazyArrayAllocStart = GC.GetAllocatedBytesForCurrentThread();
        int lazyArrayCount = 0;
        foreach (var unused in lazyJsonLoader.Load<TestRecord>(_jsonArrayFilePath))
        {
            lazyArrayCount++;
        }

        lazyArraySw.Stop();
        long lazyArrayAllocated = GC.GetAllocatedBytesForCurrentThread() - lazyArrayAllocStart;
        double lazyArrayThroughput = lazyArrayCount / lazyArraySw.Elapsed.TotalSeconds;

        // 3. JSON Array - Batched Lazy Loader (Batch size: 1,000)
        var batchedArraySw = Stopwatch.StartNew();
        long batchedArrayAllocStart = GC.GetAllocatedBytesForCurrentThread();
        int batchedArrayCount = 0;
        foreach (var batch in batchingLoader.Load<TestRecord>(lazyJsonLoader, _jsonArrayFilePath, batchSize: batchSize))
        {
            batchedArrayCount += batch.Count;
        }

        batchedArraySw.Stop();
        long batchedArrayAllocated = GC.GetAllocatedBytesForCurrentThread() - batchedArrayAllocStart;
        double batchedArrayThroughput = batchedArrayCount / batchedArraySw.Elapsed.TotalSeconds;

        // 4. JSON Lines - Eager Loader
        var eagerLinesSw = Stopwatch.StartNew();
        long eagerLinesAllocStart = GC.GetAllocatedBytesForCurrentThread();
        int eagerLinesCount = 0;
        foreach (var unused in eagerJsonLinesLoader.Load<TestRecord>(_jsonLinesFilePath))
        {
            eagerLinesCount++;
        }

        eagerLinesSw.Stop();
        long eagerLinesAllocated = GC.GetAllocatedBytesForCurrentThread() - eagerLinesAllocStart;
        double eagerLinesThroughput = eagerLinesCount / eagerLinesSw.Elapsed.TotalSeconds;

        // 5. JSON Lines - Lazy Loader (Line-by-line streaming)
        var lazyLinesSw = Stopwatch.StartNew();
        long lazyLinesAllocStart = GC.GetAllocatedBytesForCurrentThread();
        int lazyLinesCount = 0;
        foreach (var unused in lazyJsonLinesLoader.Load<TestRecord>(_jsonLinesFilePath))
        {
            lazyLinesCount++;
        }

        lazyLinesSw.Stop();
        long lazyLinesAllocated = GC.GetAllocatedBytesForCurrentThread() - lazyLinesAllocStart;
        double lazyLinesThroughput = lazyLinesCount / lazyLinesSw.Elapsed.TotalSeconds;

        // 6. JSON Lines - Batched Lazy Loader (Batch size: 1,000)
        var batchedLinesSw = Stopwatch.StartNew();
        long batchedLinesAllocStart = GC.GetAllocatedBytesForCurrentThread();
        int batchedLinesCount = 0;
        foreach (var batch in batchingLoader.Load<TestRecord>(lazyJsonLinesLoader, _jsonLinesFilePath,
                     batchSize: batchSize))
        {
            batchedLinesCount += batch.Count;
        }

        batchedLinesSw.Stop();
        long batchedLinesAllocated = GC.GetAllocatedBytesForCurrentThread() - batchedLinesAllocStart;
        double batchedLinesThroughput = batchedLinesCount / batchedLinesSw.Elapsed.TotalSeconds;

        _output.WriteLine($"[Full Dataset Throughput ({LargeDatasetSize:N0} items, Batch Size: {batchSize:N0})]");
        _output.WriteLine("  JSON Array:");
        _output.WriteLine(
            $"    Eager Loader:        {eagerArraySw.ElapsedMilliseconds,4} ms | {eagerArrayThroughput,10:N0} items/sec | {eagerArrayAllocated / 1024:N0} KB allocated");
        _output.WriteLine(
            $"    Lazy Loader:         {lazyArraySw.ElapsedMilliseconds,4} ms | {lazyArrayThroughput,10:N0} items/sec | {lazyArrayAllocated / 1024:N0} KB allocated");
        _output.WriteLine(
            $"    Batched Lazy Loader: {batchedArraySw.ElapsedMilliseconds,4} ms | {batchedArrayThroughput,10:N0} items/sec | {batchedArrayAllocated / 1024:N0} KB allocated");
        _output.WriteLine("  JSON Lines:");
        _output.WriteLine(
            $"    Eager Loader:        {eagerLinesSw.ElapsedMilliseconds,4} ms | {eagerLinesThroughput,10:N0} items/sec | {eagerLinesAllocated / 1024:N0} KB allocated");
        _output.WriteLine(
            $"    Lazy Loader:         {lazyLinesSw.ElapsedMilliseconds,4} ms | {lazyLinesThroughput,10:N0} items/sec | {lazyLinesAllocated / 1024:N0} KB allocated");
        _output.WriteLine(
            $"    Batched Lazy Loader: {batchedLinesSw.ElapsedMilliseconds,4} ms | {batchedLinesThroughput,10:N0} items/sec | {batchedLinesAllocated / 1024:N0} KB allocated");

        Assert.Equal(LargeDatasetSize, eagerArrayCount);
        Assert.Equal(LargeDatasetSize, lazyArrayCount);
        Assert.Equal(LargeDatasetSize, batchedArrayCount);
        Assert.Equal(LargeDatasetSize, eagerLinesCount);
        Assert.Equal(LargeDatasetSize, lazyLinesCount);
        Assert.Equal(LargeDatasetSize, batchedLinesCount);

        Assert.True(eagerArrayThroughput > 0);
        Assert.True(lazyArrayThroughput > 0);
        Assert.True(batchedArrayThroughput > 0);
        Assert.True(eagerLinesThroughput > 0);
        Assert.True(lazyLinesThroughput > 0);
        Assert.True(batchedLinesThroughput > 0);
    }
}
