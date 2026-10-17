namespace JsonStreamingBenchmarks;

public interface IJsonLoader
{
    IEnumerable<TItemType> Load<TItemType>(string filePath);

    IEnumerable<TItemType> Load<TItemType>(string filePath, int skip, int? take = null);
}
