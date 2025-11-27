using Foundatio.LuceneQueryParser.Ast;

namespace Foundatio.LuceneQueryParser.Visitors;

/// <summary>
/// A visitor that validates query nodes against configured options.
/// Collects referenced fields, tracks operations, and applies validation rules.
/// </summary>
public class ValidationVisitor : QueryNodeVisitor
{
    /// <summary>
    /// Visits a GroupNode and tracks nesting depth.
    /// </summary>
    public override async Task<QueryNode> VisitAsync(GroupNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();

        // Track nesting depth
        result.CurrentNodeDepth++;

        var visitedNode = await base.VisitAsync(node, context).ConfigureAwait(false);

        result.CurrentNodeDepth--;

        return visitedNode;
    }

    /// <summary>
    /// Visits a FieldQueryNode and validates the field.
    /// </summary>
    public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();

        // Add field to referenced fields
        if (!string.IsNullOrEmpty(node.Field))
        {
            result.ReferencedFields.Add(node.Field);
        }

        // Add operation
        result.AddOperation("field", node.Field);

        return await base.VisitAsync(node, context).ConfigureAwait(false);
    }

    /// <summary>
    /// Visits a TermNode and validates wildcards.
    /// </summary>
    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
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

        return Task.FromResult<QueryNode>(node);
    }

    /// <summary>
    /// Visits a PhraseNode.
    /// </summary>
    public override Task<QueryNode> VisitAsync(PhraseNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();
        result.AddOperation("phrase", null);
        return Task.FromResult<QueryNode>(node);
    }

    /// <summary>
    /// Visits a RangeNode.
    /// </summary>
    public override Task<QueryNode> VisitAsync(RangeNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();
        result.AddOperation("range", null);
        return Task.FromResult<QueryNode>(node);
    }

    /// <summary>
    /// Visits an ExistsNode.
    /// </summary>
    public override Task<QueryNode> VisitAsync(ExistsNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();

        if (!string.IsNullOrEmpty(node.Field))
        {
            result.ReferencedFields.Add(node.Field);
        }

        result.AddOperation("exists", node.Field);
        return Task.FromResult<QueryNode>(node);
    }

    /// <summary>
    /// Visits a MissingNode.
    /// </summary>
    public override Task<QueryNode> VisitAsync(MissingNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();

        if (!string.IsNullOrEmpty(node.Field))
        {
            result.ReferencedFields.Add(node.Field);
        }

        result.AddOperation("missing", node.Field);
        return Task.FromResult<QueryNode>(node);
    }

    /// <summary>
    /// Visits a RegexNode.
    /// </summary>
    public override Task<QueryNode> VisitAsync(RegexNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();
        result.AddOperation("regex", null);
        return Task.FromResult<QueryNode>(node);
    }

    /// <summary>
    /// Visits a NotNode.
    /// </summary>
    public override async Task<QueryNode> VisitAsync(NotNode node, IQueryVisitorContext context)
    {
        var result = context.GetValidationResult();
        result.AddOperation("not", null);
        return await base.VisitAsync(node, context).ConfigureAwait(false);
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
    /// Runs the validation visitor on a query node asynchronously.
    /// </summary>
    /// <param name="node">The node to validate.</param>
    /// <param name="context">Optional context (created if not provided).</param>
    /// <returns>The validation result.</returns>
    public static async Task<QueryValidationResult> RunAsync(QueryNode node, IQueryVisitorContext? context = null)
    {
        context ??= new QueryVisitorContext();
        var visitor = new ValidationVisitor();
        await visitor.AcceptAsync(node, context).ConfigureAwait(false);
        visitor.ApplyRestrictions(context);
        return context.GetValidationResult();
    }

    /// <summary>
    /// Runs the validation visitor on a query node asynchronously with options.
    /// </summary>
    /// <param name="node">The node to validate.</param>
    /// <param name="options">The validation options.</param>
    /// <param name="context">Optional context (created if not provided).</param>
    /// <returns>The validation result.</returns>
    public static async Task<QueryValidationResult> RunAsync(QueryNode node, QueryValidationOptions options, IQueryVisitorContext? context = null)
    {
        context ??= new QueryVisitorContext();
        context.SetValidationOptions(options);
        return await RunAsync(node, context).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the validation visitor on a query node asynchronously with a list of allowed fields.
    /// </summary>
    /// <param name="node">The node to validate.</param>
    /// <param name="allowedFields">The fields that are allowed.</param>
    /// <param name="context">Optional context (created if not provided).</param>
    /// <returns>The validation result.</returns>
    public static async Task<QueryValidationResult> RunAsync(QueryNode node, IEnumerable<string> allowedFields, IQueryVisitorContext? context = null)
    {
        var options = new QueryValidationOptions();
        foreach (var field in allowedFields)
            options.AllowedFields.Add(field);
        return await RunAsync(node, options, context).ConfigureAwait(false);
    }
}
