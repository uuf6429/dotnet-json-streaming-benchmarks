using System.Text.Json;

namespace JsonStreamingBenchmarks;

public class EagerJsonLinesLoader : IJsonLoader
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

        var lines = File.ReadAllLines(filePath);
        var result = new List<TItemType>();

        int skipped = 0;
        foreach (var line in lines)
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

            if (take.HasValue && result.Count >= take.Value)
            {
                break;
            }

            var record = JsonSerializer.Deserialize<TItemType>(line);
            if (record is null)
            {
                throw new JsonException("A record deserialized to null.");
            }

            result.Add(record);
        }

        return result;
    }
}
