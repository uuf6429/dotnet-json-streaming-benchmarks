using System.Text.Json;
using Xunit;

using JsonStreamingBenchmarks;

namespace JsonStreamingBenchmarks.Tests;

public class LazyJsonLinesLoaderTests : IDisposable
{
    private readonly string _tempDirectory;

    public LazyJsonLinesLoaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LazyJsonLinesLoaderTests_" + Guid.NewGuid());
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
    public void Load_ValidJsonLines_StreamsAllRecords()
    {
        var filePath = Path.Combine(_tempDirectory, "records.jsonl");
        var expected = new List<TestRecord>
        {
            new(1, "First", 10.0),
            new(2, "Second", 20.0),
            new(3, "Third", 30.0)
        };

        var lines = expected.Select(r => JsonSerializer.Serialize(r));
        File.WriteAllLines(filePath, lines);

        var loader = new LazyJsonLinesLoader();
        var actual = loader.Load<TestRecord>(filePath).ToList();

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Load_EmptyFile_ReturnsEmptyCollection()
    {
        var filePath = Path.Combine(_tempDirectory, "empty.jsonl");
        File.WriteAllText(filePath, "");

        var loader = new LazyJsonLinesLoader();
        var actual = loader.Load<TestRecord>(filePath).ToList();

        Assert.Empty(actual);
    }

    [Fact]
    public void Load_BlankAndWhitespaceLines_SkipsEmptyLines()
    {
        var filePath = Path.Combine(_tempDirectory, "blank_lines.jsonl");
        var record1 = new TestRecord(1, "One", 1.0);
        var record2 = new TestRecord(2, "Two", 2.0);

        var content = $"{JsonSerializer.Serialize(record1)}\n\n   \r\n{JsonSerializer.Serialize(record2)}\n\n";
        File.WriteAllText(filePath, content);

        var loader = new LazyJsonLinesLoader();
        var actual = loader.Load<TestRecord>(filePath).ToList();

        Assert.Equal(2, actual.Count);
        Assert.Equal(record1, actual[0]);
        Assert.Equal(record2, actual[1]);
    }

    [Fact]
    public void Load_PartialEnumeration_OnlyConsumesRequestedLines()
    {
        var filePath = Path.Combine(_tempDirectory, "large.jsonl");
        var expected = Enumerable.Range(1, 1000)
            .Select(i => new TestRecord(i, $"Item_{i}", i * 1.0))
            .ToList();

        var lines = expected.Select(r => JsonSerializer.Serialize(r));
        File.WriteAllLines(filePath, lines);

        var loader = new LazyJsonLinesLoader();
        var firstThree = loader.Load<TestRecord>(filePath).Take(3).ToList();

        Assert.Equal(3, firstThree.Count);
        Assert.Equal(expected.Take(3), firstThree);
    }

    [Fact]
    public void Load_InvalidJsonLine_ThrowsJsonException()
    {
        var filePath = Path.Combine(_tempDirectory, "invalid.jsonl");
        File.WriteAllLines(filePath, new[]
        {
            "{\"Id\": 1, \"Name\": \"Valid\", \"Value\": 1.0}",
            "{ invalid json line }"
        });

        var loader = new LazyJsonLinesLoader();

        Assert.Throws<JsonException>(() => loader.Load<TestRecord>(filePath).ToList());
    }

    [Fact]
    public void Load_NonExistentFile_ThrowsFileNotFoundExceptionOnEnumeration()
    {
        var loader = new LazyJsonLinesLoader();
        var nonExistentPath = Path.Combine(_tempDirectory, "missing.jsonl");

        Assert.Throws<FileNotFoundException>(() => loader.Load<TestRecord>(nonExistentPath).ToList());
    }

    [Fact]
    public void Load_WithSkipAndTake_SkipsAndReturnsExactSlice()
    {
        var filePath = Path.Combine(_tempDirectory, "pagination.jsonl");
        var expected = Enumerable.Range(1, 20)
            .Select(i => new TestRecord(i, $"Item_{i}", i * 1.0))
            .ToList();
        var lines = expected.Select(r => JsonSerializer.Serialize(r));
        File.WriteAllLines(filePath, lines);

        var loader = new LazyJsonLinesLoader();
        var actual = loader.Load<TestRecord>(filePath, skip: 5, take: 10).ToList();

        Assert.Equal(10, actual.Count);
        Assert.Equal(expected.Skip(5).Take(10), actual);
    }

    [Fact]
    public void Load_SkipBeyondTotal_ReturnsEmpty()
    {
        var filePath = Path.Combine(_tempDirectory, "skip_beyond.jsonl");
        var expected = Enumerable.Range(1, 5)
            .Select(i => new TestRecord(i, $"Item_{i}", i * 1.0))
            .ToList();
        var lines = expected.Select(r => JsonSerializer.Serialize(r));
        File.WriteAllLines(filePath, lines);

        var loader = new LazyJsonLinesLoader();
        var actual = loader.Load<TestRecord>(filePath, skip: 10).ToList();

        Assert.Empty(actual);
    }

    [Fact]
    public void Load_TakeZero_ReturnsEmpty()
    {
        var filePath = Path.Combine(_tempDirectory, "take_zero.jsonl");
        var expected = Enumerable.Range(1, 5)
            .Select(i => new TestRecord(i, $"Item_{i}", i * 1.0))
            .ToList();
        var lines = expected.Select(r => JsonSerializer.Serialize(r));
        File.WriteAllLines(filePath, lines);

        var loader = new LazyJsonLinesLoader();
        var actual = loader.Load<TestRecord>(filePath, skip: 2, take: 0).ToList();

        Assert.Empty(actual);
    }

    [Fact]
    public void Load_NegativeSkipOrTake_ThrowsArgumentOutOfRangeException()
    {
        var filePath = Path.Combine(_tempDirectory, "arg_check.jsonl");
        File.WriteAllText(filePath, "");

        var loader = new LazyJsonLinesLoader();

        Assert.Throws<ArgumentOutOfRangeException>(() => loader.Load<TestRecord>(filePath, skip: -1).ToList());
        Assert.Throws<ArgumentOutOfRangeException>(() => loader.Load<TestRecord>(filePath, skip: 0, take: -5).ToList());
    }
}
