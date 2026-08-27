namespace CodeGraphMcp.Filtering;

public abstract record FilterNode;

public sealed record LiteralNode(string Value) : FilterNode;

public sealed record SingleCharWildcardNode : FilterNode;

public sealed record MultiCharWildcardNode : FilterNode;

public sealed record CharClassNode(string ClassContent) : FilterNode;

public sealed record StartAnchorNode : FilterNode;

public sealed record EndAnchorNode : FilterNode;

public sealed record GroupNode(FilterNode Inner) : FilterNode;

public sealed record SequenceNode(IReadOnlyList<FilterNode> Nodes) : FilterNode;

public sealed record AlternationNode(IReadOnlyList<FilterNode> Branches) : FilterNode;
