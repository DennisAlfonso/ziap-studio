namespace ZiapStudio.Core.Documents;

public readonly record struct DocumentId(string Value)
{
    public override string ToString() => Value;
}
