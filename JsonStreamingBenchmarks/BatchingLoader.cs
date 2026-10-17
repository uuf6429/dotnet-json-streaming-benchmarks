namespace JsonStreamingBenchmarks;

public class BatchingLoader
{
    public IEnumerable<IReadOnlyList<TItemType>> Load<TItemType>(
        IJsonLoader loader,
        string filePath,
        int batchSize = 1000,
        int skipBatches = 0,
        int? takeBatches = null)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(skipBatches);
        if (takeBatches.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(takeBatches.Value);
            if (takeBatches.Value == 0)
            {
                yield break;
            }
        }

        int skipItems = checked(skipBatches * batchSize);
        int? takeItems = takeBatches.HasValue ? checked(takeBatches.Value * batchSize) : null;

        var batch = new List<TItemType>(batchSize);

        foreach (var record in loader.Load<TItemType>(filePath, skip: skipItems, take: takeItems))
        {
            batch.Add(record);

            if (batch.Count == batchSize)
            {
                yield return batch;
                batch = new List<TItemType>(batchSize);
            }
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }
}
