namespace Foundatio.Lucene.Ast;

/// <summary>
/// Text backed by a zero-copy slice of the source query that is materialized as a string at most once.
/// </summary>
internal struct TextValue
{
    private ReadOnlyMemory<char> _memory;
    private string? _string;

    public ReadOnlyMemory<char> Memory
    {
        readonly get => _string is not null ? _string.AsMemory() : _memory;
        set
        {
            _memory = value;
            _string = null;
        }
    }

    public readonly ReadOnlySpan<char> Span => _string is not null ? _string.AsSpan() : _memory.Span;

    public readonly bool IsEmpty => _string is not null ? _string.Length == 0 : _memory.IsEmpty;

    public string GetString() => _string ??= _memory.Length == 0 ? string.Empty : _memory.Span.ToString();

    public void SetString(string? value)
    {
        _string = value ?? string.Empty;
        _memory = default;
    }
}
