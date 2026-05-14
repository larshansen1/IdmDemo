namespace Backend.Application.Scim;

public sealed class ScimFilter
{
    public ScimFilter(string attributeName, string @operator, string value)
    {
        this.AttributeName = attributeName;
        this.Operator = @operator;
        this.Value = value;
    }

    public string AttributeName { get; }

    public string Operator { get; }

    public string Value { get; }
}
