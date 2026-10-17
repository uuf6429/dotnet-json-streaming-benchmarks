using System.Text.Json;

namespace JsonStreamingBenchmarks;

public class LazyJsonLoader : IJsonLoader
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

        using var parser = new JsonArrayRecordReader<TItemType>(filePath);

        for (int i = 0; i < skip; i++)
        {
            if (!parser.TrySkipRecord())
            {
                yield break;
            }
        }

        int yielded = 0;
        while ((!take.HasValue || yielded < take.Value) && parser.TryRead(out var record))
        {
            yield return record!;
            yielded++;
        }
    }

    private sealed class JsonArrayRecordReader<T>(string filePath) : IDisposable
    {
        private readonly Stream _stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024);

        private byte[] _buffer = new byte[64 * 1024];
        private int _buffered;
        private JsonReaderState _state;
        private bool _finalBlock;
        private bool _startedArray;
        private bool _finishedArray;

        public bool TryRead(out T? record)
        {
            record = default;

            while (true)
            {
                if (!_finalBlock && _buffered < _buffer.Length)
                {
                    var count = _stream.Read(_buffer, _buffered, _buffer.Length - _buffered);

                    if (count == 0)
                        _finalBlock = true;
                    else
                        _buffered += count;
                }

                var reader = new Utf8JsonReader(_buffer.AsSpan(0, _buffered), _finalBlock, _state);

                while (reader.Read())
                {
                    if (_finishedArray)
                        throw new JsonException("Unexpected content after the root array.");

                    if (!_startedArray)
                    {
                        if (reader.TokenType != JsonTokenType.StartArray)
                            throw new JsonException("Expected a root JSON array.");

                        _startedArray = true;
                        continue;
                    }

                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        _finishedArray = true;
                        continue;
                    }

                    if (reader.TokenType != JsonTokenType.StartObject)
                        throw new JsonException("Expected an object in the root array.");

                    using var document = JsonDocument.ParseValue(ref reader);

                    record = document.RootElement.Deserialize<T>();

                    if (record is null)
                        throw new JsonException("A record deserialized to null.");

                    SaveReaderPosition(ref reader);
                    return true;
                }

                SaveReaderPosition(ref reader);

                if (_finalBlock && _buffered == 0)
                {
                    if (!_startedArray || !_finishedArray)
                        throw new JsonException("The JSON array is incomplete.");

                    return false;
                }

                if (!_finalBlock && _buffered == _buffer.Length)
                    Array.Resize(ref _buffer, checked(_buffer.Length * 2));
            }
        }

        public bool TrySkipRecord()
        {
            while (true)
            {
                if (!_finalBlock && _buffered < _buffer.Length)
                {
                    var count = _stream.Read(_buffer, _buffered, _buffer.Length - _buffered);

                    if (count == 0)
                        _finalBlock = true;
                    else
                        _buffered += count;
                }

                var reader = new Utf8JsonReader(_buffer.AsSpan(0, _buffered), _finalBlock, _state);

                while (reader.Read())
                {
                    if (_finishedArray)
                        throw new JsonException("Unexpected content after the root array.");

                    if (!_startedArray)
                    {
                        if (reader.TokenType != JsonTokenType.StartArray)
                            throw new JsonException("Expected a root JSON array.");

                        _startedArray = true;
                        continue;
                    }

                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        _finishedArray = true;
                        continue;
                    }

                    if (reader.TokenType != JsonTokenType.StartObject)
                        throw new JsonException("Expected an object in the root array.");

                    if (!reader.TrySkip())
                    {
                        break;
                    }

                    SaveReaderPosition(ref reader);
                    return true;
                }

                SaveReaderPosition(ref reader);

                if (_finalBlock && _buffered == 0)
                {
                    if (!_startedArray || !_finishedArray)
                        throw new JsonException("The JSON array is incomplete.");

                    return false;
                }

                if (!_finalBlock && _buffered == _buffer.Length)
                    Array.Resize(ref _buffer, checked(_buffer.Length * 2));
            }
        }

        private void SaveReaderPosition(ref Utf8JsonReader reader)
        {
            int consumed = (int)reader.BytesConsumed;
            _state = reader.CurrentState;
            _buffered -= consumed;

            if (_buffered > 0)
            {
                Buffer.BlockCopy(_buffer, consumed, _buffer, 0, _buffered);
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
