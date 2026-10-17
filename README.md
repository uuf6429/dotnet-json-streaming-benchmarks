# ⚡ dotnet-json-streaming-benchmarks

A C# / .NET proof-of-concept and performance comparison showcase demonstrating **Eager vs. Lazy (Streaming)** ingestion for **JSON Arrays** and **JSON Lines (`.jsonl`)**.

[![CI](https://github.com/uuf6429/dotnet-json-streaming-benchmarks/actions/workflows/ci.yml/badge.svg)](https://github.com/uuf6429/dotnet-json-streaming-benchmarks/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Sponsor](https://img.shields.io/badge/sponsor-uuf6429-ea4aaa.svg)](https://github.com/sponsors/uuf6429)

---

## 🎯 Overview

When handling large datasets in JSON, traditional eager deserializers buffer and parse the entire payload into memory upfront before returning any elements. 

**dotnet-json-streaming-benchmarks** showcases how lazy, forward-only streaming (using `Utf8JsonReader` and `IAsyncEnumerable`/`IEnumerable<T>`) eliminates large memory allocations and dramatically reduces Time-to-First-Item latency.

### Available Loaders

| Loader | Format | Ingestion Strategy | Memory Overhead |
|---|---|---|---|
| `EagerJsonLoader` | JSON Array (`.json`) | Full document buffered into `List<T>` | High ($O(N)$) |
| `LazyJsonLoader` | JSON Array (`.json`) | Streaming via `Utf8JsonReader` + `yield return` | Near-zero ($O(1)$) |
| `EagerJsonLinesLoader` | JSON Lines (`.jsonl`) | Full file read via `File.ReadAllLines` | High ($O(N)$) |
| `LazyJsonLinesLoader` | JSON Lines (`.jsonl`) | Streaming via `StreamReader.ReadLine` + `yield return` | Near-zero ($O(1)$) |
| `BatchingLoader` | Any `IJsonLoader` | Splits streams into discrete chunks on-the-fly | Minimal ($O(\text{BatchSize})$) |

---

## 📊 Comparison Output

Running `dotnet test` against a 30,000-item dataset demonstrates the performance and memory differences:

### 1. Partial Read (Consuming first 10 of 30,000 items)

```text
[JSON Array Partial Read (10 / 30,000 items)]
  Eager Loader:  259 ms,  26,761 KB allocated
  Lazy Loader:     0 ms,     132 KB allocated  (~200x less memory, instantaneous)

[JSON Lines Partial Read (10 / 30,000 items)]
  Eager Loader:  404 ms,  18,202 KB allocated
  Lazy Loader:     0 ms,      73 KB allocated  (~250x less memory, instantaneous)
```

### 2. Time-to-First-Batch Latency (Batch size: 50 from 30,000 items)

```text
[First Batch Latency]
  Eager Loader Batching: 173 ms  (must parse entire 30,000 items before yielding batch 1)
  Lazy Loader Batching:    2 ms  (yields batch 1 as soon as 50 items are read)
```

### 3. Pagination & Skipping (Skip 10,000, Take 50 from 30,000 items)

```text
[JSON Array Pagination (Skip 10,000, Take 50 of 30,000 items)]
  Eager Loader (Full Parse):   77 ms,  26,763 KB allocated
  Lazy Loader + LINQ .Skip():  81 ms,   4,055 KB allocated  (deserializes 10,000 discarded objects)
  Lazy Loader Native .Load():  29 ms,     149 KB allocated  (~27x less memory, token-level skip)

[Batch Pagination (Skip 100 batches of 100, Take 1 batch)]
  BatchingLoader + LINQ .Skip(): 55 ms, 4,158 KB allocated
  BatchingLoader Native Skip:    20 ms,   169 KB allocated  (~25x less memory)
```

---

## 💡 Architectural Deep-Dive: LINQ `.Skip()` vs. Native Skipping

A key finding in this PoC evolution is the difference between standard LINQ pagination and native reader-level skipping:

### What happens when calling `IEnumerable<T>.Skip(count)`?
`IEnumerable.Skip()` is a LINQ extension method that advances the source enumerator by calling `MoveNext()` `count` times.
- Because `IEnumerator<T>.Current` is typed as `T`, every call to `MoveNext()` on a standard lazy loader triggers **full JSON parsing and object deserialization** for that element.
- Even though lazy streaming keeps peak memory bounded ($O(1)$ working set), skipping 10,000 items still creates and discards 10,000 C# objects, generating significant GC heap pressure and CPU waste.

### The Efficient Way: Native Deserialization Bypass
Our native pagination API (`IJsonLoader.Load(filePath, skip, take)`) bypasses deserialization entirely during the skip phase:
- **`LazyJsonLoader` (JSON Array)**: Uses low-level `Utf8JsonReader.TrySkip()` within the byte buffer to skip JSON objects without building `JsonDocument` or allocating C# record instances.
- **`LazyJsonLinesLoader` (JSON Lines)**: Advances through raw text lines via `StreamReader.ReadLine()` without invoking `JsonSerializer.Deserialize<T>()`.
- **`BatchingLoader`**: Directly delegates `skipBatches * batchSize` to the loader's native zero-deserialization skip.

---

## 🚀 Quickstart

### 1. Streaming JSON Array

```csharp
using JsonStreamingBenchmarks;

var loader = new LazyJsonLoader();

// Streams items on-demand without buffering the whole file in memory
foreach (var record in loader.Load<MyRecord>("large-file.json"))
{
    ProcessRecord(record);
}
```

### 2. Streaming JSON Lines (`.jsonl`)

```csharp
using JsonStreamingBenchmarks;

var loader = new LazyJsonLinesLoader();

foreach (var record in loader.Load<MyRecord>("data.jsonl"))
{
    ProcessRecord(record);
}
```

### 3. Chunked Streaming with BatchingLoader

```csharp
using JsonStreamingBenchmarks;

var batchingLoader = new BatchingLoader();
var jsonLoader = new LazyJsonLinesLoader();

// Processes 100 items per batch with minimal working set
foreach (var batch in batchingLoader.Load<MyRecord>(jsonLoader, "data.jsonl", batchSize: 100))
{
    await SaveBatchToDatabaseAsync(batch);
}
```

### 4. Fast Pagination & Batch Skipping

```csharp
using JsonStreamingBenchmarks;

var loader = new LazyJsonLoader();
var batchingLoader = new BatchingLoader();

// Skip 10,000 items and take 50 items with ZERO deserialization of skipped records
foreach (var item in loader.Load<MyRecord>("data.json", skip: 10_000, take: 50))
{
    ProcessRecord(item);
}

// Skip 100 batches and take 2 batches (100 items each) efficiently
foreach (var batch in batchingLoader.Load<MyRecord>(loader, "data.json", batchSize: 100, skipBatches: 100, takeBatches: 2))
{
    await SaveBatchToDatabaseAsync(batch);
}
```

---

## 🧪 Running Tests

Execute the comprehensive unit and benchmark test suite:

```shell
dotnet test --logger "console;verbosity=detailed"
```

---

## 💖 Sponsoring

If you find this project useful, consider [sponsoring on GitHub](https://github.com/sponsors/uuf6429) or via [PayPal](https://paypal.me/uuf6429).

---

## 📄 License

This project is open-sourced under the [MIT License](LICENSE).
