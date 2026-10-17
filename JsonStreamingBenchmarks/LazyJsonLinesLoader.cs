using System.Text.Json;

namespace JsonStreamingBenchmarks;

public class LazyJsonLinesLoader : IJsonLoader
{
    public IEnumerable<TItemType> Load<TItemType>(string filePath)
    {
        return Load<TItemType>(filePath, skip: 0, take: null);
    }

    public IEnumerable<TItemType> Load<TItemType>(string filePath, int skip, int? take = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        if (take.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(take.Value);
        }

        if (take == 0)
        {
            yield break;
        }

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024);
        using var reader = new StreamReader(stream);

        string? line;
        int skipped = 0;
        int yielded = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (skipped < skip)
            {
                skipped++;
                continue;
            }

            var record = JsonSerializer.Deserialize<TItemType>(line);
            if (record is null)
            {
                throw new JsonException("A record deserialized to null.");
            }

            yield return record;
            yielded++;

            if (take.HasValue && yielded >= take.Value)
            {
                yield break;
            }
        }
    }
}
