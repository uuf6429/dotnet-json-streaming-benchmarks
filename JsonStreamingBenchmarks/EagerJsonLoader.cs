using System.Text.Json;

namespace JsonStreamingBenchmarks;

public class EagerJsonLoader : IJsonLoader
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

        var items = JsonSerializer.Deserialize<List<TItemType>>(File.ReadAllText(filePath)) ?? [];
        var query = items.Skip(skip);
        if (take.HasValue)
        {
            query = query.Take(take.Value);
        }

        return query.ToList();
    }
}
