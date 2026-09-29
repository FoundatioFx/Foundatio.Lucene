namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents a missing query (<c>_missing_:field</c>).
/// </summary>
public class MissingNode : QueryNode, IFieldNode
{
    private TextValue _field;

    /// <summary>
    /// The field that must be missing as a memory slice.
    /// </summary>
    public ReadOnlyMemory<char> FieldMemory
    {
        get => _field.Memory;
        set => _field.Memory = value;
    }

    /// <summary>
    /// The field that must be missing.
    /// </summary>
    public string Field
    {
        get => _field.GetString();
        set => _field.SetString(value);
    }

    /// <inheritdoc/>
    public override QueryNode Clone() => CopyCommonTo(new MissingNode { FieldMemory = FieldMemory });
}
