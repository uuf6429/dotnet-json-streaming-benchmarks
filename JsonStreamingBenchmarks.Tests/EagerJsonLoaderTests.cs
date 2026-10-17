using System.Text.Json;
using Xunit;

using JsonStreamingBenchmarks;

namespace JsonStreamingBenchmarks.Tests;

public class EagerJsonLoaderTests : IDisposable
{
    private readonly string _tempDirectory;

    public EagerJsonLoaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "EagerJsonLoaderTests_" + Guid.NewGuid());
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
    public void Load_ValidJsonArray_ReturnsAllRecords()
    {
        var filePath = Path.Combine(_tempDirectory, "records.json");
        var expected = new List<TestRecord>
        {
            new(1, "Alpha", 10.5),
            new(2, "Beta", 20.0),
            new(3, "Gamma", 30.25)
        };
        File.WriteAllText(filePath, JsonSerializer.Serialize(expected));

        var loader = new EagerJsonLoader();
        var actual = loader.Load<TestRecord>(filePath).ToList();

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Load_EmptyJsonArray_ReturnsEmptyCollection()
    {
        var filePath = Path.Combine(_tempDirectory, "empty.json");
        File.WriteAllText(filePath, "[]");

        var loader = new EagerJsonLoader();
        var actual = loader.Load<TestRecord>(filePath).ToList();

        Assert.Empty(actual);
    }

    [Fact]
    public void Load_NonExistentFile_ThrowsFileNotFoundException()
    {
        var loader = new EagerJsonLoader();
        var nonExistentPath = Path.Combine(_tempDirectory, "missing.json");

        Assert.Throws<FileNotFoundException>(() => loader.Load<TestRecord>(nonExistentPath).ToList());
    }

    [Fact]
    public void Load_InvalidJson_ThrowsJsonException()
    {
        var filePath = Path.Combine(_tempDirectory, "invalid.json");
        File.WriteAllText(filePath, "{ not a valid json array }");

        var loader = new EagerJsonLoader();

        Assert.Throws<JsonException>(() => loader.Load<TestRecord>(filePath).ToList());
    }

    [Fact]
    public void Load_WithSkipAndTake_ReturnsExactSlice()
    {
        var filePath = Path.Combine(_tempDirectory, "pagination.json");
        var expected = Enumerable.Range(1, 20)
            .Select(i => new TestRecord(i, $"Item_{i}", i * 1.0))
            .ToList();
        File.WriteAllText(filePath, JsonSerializer.Serialize(expected));

        var loader = new EagerJsonLoader();
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

        var loader = new EagerJsonLoader();
        var actual = loader.Load<TestRecord>(filePath, skip: 10).ToList();

        Assert.Empty(actual);
    }

    [Fact]
    public void Load_NegativeSkipOrTake_ThrowsArgumentOutOfRangeException()
    {
        var filePath = Path.Combine(_tempDirectory, "arg_check.json");
        File.WriteAllText(filePath, "[]");

        var loader = new EagerJsonLoader();

        Assert.Throws<ArgumentOutOfRangeException>(() => loader.Load<TestRecord>(filePath, skip: -1).ToList());
        Assert.Throws<ArgumentOutOfRangeException>(() => loader.Load<TestRecord>(filePath, skip: 0, take: -5).ToList());
    }
}
