namespace CodeAtlas.Parser;

/// <summary>
/// Declarative attribute for language parser plugins specifying identity, name, version, and optional metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ParserMetadataAttribute : Attribute
{
    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public string? Description { get; set; }
    public string? Author { get; set; }

    public ParserMetadataAttribute(string id, string name, string version)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(name));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(version));

        Id = id;
        Name = name;
        Version = version;
    }

    /// <summary>
    /// Converts this attribute instance to a <see cref="ParserMetadata"/> record.
    /// </summary>
    public ParserMetadata ToMetadata() => new(Id, Name, Version, Description, Author);
}
