using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// A visitor that validates query nodes against configured options.
/// Collects referenced fields, tracks operations, and applies validation rules.
/// </summary>
public class ValidationVisitor : QueryVisitor
{
    /// <summary>
    /// Visits a GroupNode and tracks nesting depth.
    /// </summary>
    protected override QueryNode Visit(GroupNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();

        // Track nesting depth
        result.CurrentNodeDepth++;

        var visitedNode = base.Visit(node, context);

        result.CurrentNodeDepth--;

        return visitedNode;
    }

    /// <summary>
    /// Visits a FieldQueryNode and validates the field.
    /// </summary>
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();

        // Add field to referenced fields
        if (!string.IsNullOrEmpty(node.Field))
        {
            result.ReferencedFields.Add(node.Field);
        }

        // Add operation
        result.AddOperation("field", node.Field);

        return base.Visit(node, context);
    }

    /// <summary>
    /// Visits a TermNode and validates wildcards.
    /// </summary>
    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();
        var options = context.GetValidationOptions();

        // Add operation
        result.AddOperation("term", null);

        // Check for leading wildcards
        if (!options.AllowLeadingWildcards &&
            !string.IsNullOrEmpty(node.Term) &&
            (node.Term.StartsWith('*') || node.Term.StartsWith('?')))
        {
            context.AddValidationError($"Terms must not start with a wildcard: {node.Term}");
        }

        return node;
    }

    /// <summary>
    /// Visits a PhraseNode.
    /// </summary>
    protected override QueryNode Visit(PhraseNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();
        result.AddOperation("phrase", null);
        return node;
    }

    /// <summary>
    /// Visits a RangeNode.
    /// </summary>
    protected override QueryNode Visit(RangeNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();
        result.AddOperation("range", null);
        return node;
    }

    /// <summary>
    /// Visits an ExistsNode.
    /// </summary>
    protected override QueryNode Visit(ExistsNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();

        if (!string.IsNullOrEmpty(node.Field))
        {
            result.ReferencedFields.Add(node.Field);
        }

        result.AddOperation("exists", node.Field);
        return node;
    }

    /// <summary>
    /// Visits a MissingNode.
    /// </summary>
    protected override QueryNode Visit(MissingNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();

        if (!string.IsNullOrEmpty(node.Field))
        {
            result.ReferencedFields.Add(node.Field);
        }

        result.AddOperation("missing", node.Field);
        return node;
    }

    /// <summary>
    /// Visits a RegexNode.
    /// </summary>
    protected override QueryNode Visit(RegexNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();
        result.AddOperation("regex", null);
        return node;
    }

    /// <summary>
    /// Visits a NotNode.
    /// </summary>
    protected override QueryNode Visit(NotNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();
        result.AddOperation("not", null);
        return base.Visit(node, context);
    }

    /// <summary>
    /// Applies query restrictions after visiting all nodes.
    /// </summary>
    public void ApplyRestrictions(IQueryVisitorContext context)
    {
        var options = context.GetValidationOptions();
        var result = context.GetValidationResult();

        // Check restricted fields
        if (options.RestrictedFields.Count > 0 && result.ReferencedFields.Count > 0)
        {
            var restrictedFieldsUsed = result.ReferencedFields
                .Where(f => options.RestrictedFields.Contains(f))
                .ToList();

            if (restrictedFieldsUsed.Count > 0)
            {
                context.AddValidationError($"Query uses field(s) ({string.Join(", ", restrictedFieldsUsed)}) that are restricted from use.");
            }
        }

        // Check allowed fields
        if (options.AllowedFields.Count > 0 && result.ReferencedFields.Count > 0)
        {
            var nonAllowedFields = result.ReferencedFields
                .Where(f => !string.IsNullOrWhiteSpace(f) && !options.AllowedFields.Contains(f))
                .ToList();

            if (nonAllowedFields.Count > 0)
            {
                context.AddValidationError($"Query uses field(s) ({string.Join(", ", nonAllowedFields)}) that are not allowed.");
            }
        }

        // Check allowed operations
        if (options.AllowedOperations.Count > 0)
        {
            var nonAllowedOperations = result.Operations
                .Where(op => !options.AllowedOperations.Contains(op.Key))
                .Select(op => op.Key)
                .ToList();

            if (nonAllowedOperations.Count > 0)
            {
                context.AddValidationError($"Query uses operation(s) ({string.Join(", ", nonAllowedOperations)}) that are not allowed.");
            }
        }

        // Check restricted operations
        if (options.RestrictedOperations.Count > 0)
        {
            var restrictedOperationsUsed = result.Operations
                .Where(op => options.RestrictedOperations.Contains(op.Key))
                .Select(op => op.Key)
                .ToList();

            if (restrictedOperationsUsed.Count > 0)
            {
                context.AddValidationError($"Query uses operation(s) ({string.Join(", ", restrictedOperationsUsed)}) that are restricted from use.");
            }
        }

        // Check max node depth
        if (options.AllowedMaxNodeDepth > 0 && result.MaxNodeDepth > options.AllowedMaxNodeDepth)
        {
            context.AddValidationError($"Query has a nesting depth of {result.MaxNodeDepth} which exceeds the maximum allowed depth of {options.AllowedMaxNodeDepth}.");
        }

        // Throw if configured to do so
        if (options.ShouldThrow && !result.IsValid)
        {
            throw new QueryValidationException($"Invalid query: {result.Message}", result);
        }
    }

    /// <summary>
    /// Runs the validation visitor on a query node.
    /// </summary>
    /// <param name="node">The node to validate.</param>
    /// <param name="context">Optional context (created if not provided).</param>
    /// <returns>The validation result.</returns>
    public static QueryValidationResult Run(QueryNode node, IQueryVisitorContext? context = null)
    {
        context ??= new QueryVisitorContext();
        var visitor = new ValidationVisitor();
        visitor.Accept(node, context);
        visitor.ApplyRestrictions(context);
        return context.GetValidationResult();
    }

    /// <summary>
    /// Runs the validation visitor on a query node with options.
    /// </summary>
    /// <param name="node">The node to validate.</param>
    /// <param name="options">The validation options.</param>
    /// <param name="context">Optional context (created if not provided).</param>
    /// <returns>The validation result.</returns>
    public static QueryValidationResult Run(QueryNode node, QueryValidationOptions options, IQueryVisitorContext? context = null)
    {
        context ??= new QueryVisitorContext();
        context.SetValidationOptions(options);
        return Run(node, context);
    }

    /// <summary>
    /// Runs the validation visitor on a query node with a list of allowed fields.
    /// </summary>
    /// <param name="node">The node to validate.</param>
    /// <param name="allowedFields">The fields that are allowed.</param>
    /// <param name="context">Optional context (created if not provided).</param>
    /// <returns>The validation result.</returns>
    public static QueryValidationResult Run(QueryNode node, IEnumerable<string> allowedFields, IQueryVisitorContext? context = null)
    {
        var options = new QueryValidationOptions();
        foreach (var field in allowedFields)
            options.AllowedFields.Add(field);
        return Run(node, options, context);
    }
}
