namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents an exists query (<c>_exists_:field</c> or <c>field:*</c>).
/// </summary>
public class ExistsNode : QueryNode, IFieldNode
{
    private TextValue _field;

    /// <summary>
    /// The field that must exist as a memory slice.
    /// </summary>
    public ReadOnlyMemory<char> FieldMemory
    {
        get => _field.Memory;
        set => _field.Memory = value;
    }

    /// <summary>
    /// The field that must exist.
    /// </summary>
    public string Field
    {
        get => _field.GetString();
        set => _field.SetString(value);
    }

    /// <summary>
    /// Whether this was parsed from <c>_exists_:field</c> syntax (true) or <c>field:*</c> syntax (false).
    /// </summary>
    public bool IsExistsSyntax { get; set; }

    /// <inheritdoc/>
    public override QueryNode Clone() => CopyCommonTo(new ExistsNode { FieldMemory = FieldMemory, IsExistsSyntax = IsExistsSyntax });
}
