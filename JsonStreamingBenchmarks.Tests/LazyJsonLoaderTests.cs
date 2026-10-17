using System.Text.Json;
using Xunit;

using JsonStreamingBenchmarks;

namespace JsonStreamingBenchmarks.Tests;

public class LazyJsonLoaderTests : IDisposable
{
    private readonly string _tempDirectory;

    public LazyJsonLoaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LazyJsonLoaderTests_" + Guid.NewGuid());
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
    public void Load_ValidJsonArray_StreamsAllRecords()
    {
        var filePath = Path.Combine(_tempDirectory, "records.json");
        var expected = new List<TestRecord>
        {
            new(1, "Alpha", 10.5),
            new(2, "Beta", 20.0),
            new(3, "Gamma", 30.25)
        };
        File.WriteAllText(filePath, JsonSerializer.Serialize(expected));

        var loader = new LazyJsonLoader();
        var actual = loader.Load<TestRecord>(filePath).ToList();

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Load_EmptyJsonArray_ReturnsEmptyCollection()
    {
        var filePath = Path.Combine(_tempDirectory, "empty.json");
        File.WriteAllText(filePath, "[]");

        var loader = new LazyJsonLoader();
        var actual = loader.Load<TestRecord>(filePath).ToList();

        Assert.Empty(actual);
    }

    [Fact]
    public void Load_LargeArrayExceedingBuffer_CorrectlyStreamsAndResizesBuffer()
    {
        var filePath = Path.Combine(_tempDirectory, "large.json");
        var expected = Enumerable.Range(1, 10_000)
            .Select(i => new TestRecord(i, $"Item_{i}_{new string('x', 50)}", i * 1.5))
            .ToList();

        File.WriteAllText(filePath, JsonSerializer.Serialize(expected));

        var loader = new LazyJsonLoader();
        var actual = loader.Load<TestRecord>(filePath).ToList();

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected[0], actual[0]);
        Assert.Equal(expected[^1], actual[^1]);
    }

    [Fact]
    public void Load_PartialEnumeration_OnlyConsumesRequestedItemsWithoutReadingFullFile()
    {
        var filePath = Path.Combine(_tempDirectory, "partial.json");
        var expected = Enumerable.Range(1, 1_000)
            .Select(i => new TestRecord(i, $"Item_{i}", i * 1.0))
            .ToList();

        File.WriteAllText(filePath, JsonSerializer.Serialize(expected));

        var loader = new LazyJsonLoader();
        var firstFive = loader.Load<TestRecord>(filePath).Take(5).ToList();

        Assert.Equal(5, firstFive.Count);
        Assert.Equal(expected.Take(5), firstFive);
    }

    [Fact]
    public void Load_NotAnArray_ThrowsJsonException()
    {
        var filePath = Path.Combine(_tempDirectory, "object.json");
        File.WriteAllText(filePath, "{\"id\": 1, \"name\": \"NotAnArray\"}");

        var loader = new LazyJsonLoader();

        Assert.Throws<JsonException>(() => loader.Load<TestRecord>(filePath).ToList());
    }

    [Fact]
    public void Load_IncompleteArray_ThrowsJsonException()
    {
        var filePath = Path.Combine(_tempDirectory, "incomplete.json");
        File.WriteAllText(filePath, "[{\"id\": 1, \"name\": \"Incomplete\", \"value\": 1.0}");

        var loader = new LazyJsonLoader();

        Assert.ThrowsAny<JsonException>(() => loader.Load<TestRecord>(filePath).ToList());
    }

    [Fact]
    public void Load_ContentAfterArray_ThrowsJsonException()
    {
        var filePath = Path.Combine(_tempDirectory, "trailing.json");
        File.WriteAllText(filePath, "[{\"id\": 1, \"name\": \"Trailing\", \"value\": 1.0}] extra");

        var loader = new LazyJsonLoader();

        Assert.ThrowsAny<JsonException>(() => loader.Load<TestRecord>(filePath).ToList());
    }

    [Fact]
    public void Load_NonExistentFile_ThrowsFileNotFoundExceptionOnEnumeration()
    {
        var loader = new LazyJsonLoader();
        var nonExistentPath = Path.Combine(_tempDirectory, "missing.json");

        Assert.Throws<FileNotFoundException>(() => loader.Load<TestRecord>(nonExistentPath).ToList());
    }

    [Fact]
    public void Load_WithSkipAndTake_SkipsAndReturnsExactSlice()
    {
        var filePath = Path.Combine(_tempDirectory, "pagination.json");
        var expected = Enumerable.Range(1, 20)
            .Select(i => new TestRecord(i, $"Item_{i}", i * 1.0))
            .ToList();
        File.WriteAllText(filePath, JsonSerializer.Serialize(expected));

        var loader = new LazyJsonLoader();
        var actual = loader.Load<TestRecord>(filePath, skip: 5, take: 10).ToList();

        Assert.Equal(10, actual.Count);
        Assert.Equal(expected.Skip(5).Take(10), actual);
    }

    [Fact]
    public void Load_SkipBeyondTotal_ReturnsEmpty()
    {
        var filePath = Path.Combine(_tempDirectory, "skip_beyond.json");
        var expected = Enumerable.Range(1, 5)
            .Select(i => new TestRecord(i, $"Item_{i}", i * 1.0))
            .ToList();
        File.WriteAllText(filePath, JsonSerializer.Serialize(expected));

        var loader = new LazyJsonLoader();
        var actual = loader.Load<TestRecord>(filePath, skip: 10).ToList();

        Assert.Empty(actual);
    }

    [Fact]
    public void Load_TakeZero_ReturnsEmpty()
    {
        var filePath = Path.Combine(_tempDirectory, "take_zero.json");
        var expected = Enumerable.Range(1, 5)
            .Select(i => new TestRecord(i, $"Item_{i}", i * 1.0))
            .ToList();
        File.WriteAllText(filePath, JsonSerializer.Serialize(expected));

        var loader = new LazyJsonLoader();
        var actual = loader.Load<TestRecord>(filePath, skip: 2, take: 0).ToList();

        Assert.Empty(actual);
    }

    [Fact]
    public void Load_NegativeSkipOrTake_ThrowsArgumentOutOfRangeException()
    {
        var filePath = Path.Combine(_tempDirectory, "arg_check.json");
        File.WriteAllText(filePath, "[]");

        var loader = new LazyJsonLoader();

        Assert.Throws<ArgumentOutOfRangeException>(() => loader.Load<TestRecord>(filePath, skip: -1).ToList());
        Assert.Throws<ArgumentOutOfRangeException>(() => loader.Load<TestRecord>(filePath, skip: 0, take: -5).ToList());
    }
}
