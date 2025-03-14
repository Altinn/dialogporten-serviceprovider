namespace Digdir.BDB.Dialogporten.ServiceProvider;

public abstract record FieldRecord(string PropertyName)
{
    public sealed record StringRecord(string PropertyName, string Description, bool IsRequired, string? Format) : FieldRecord(PropertyName);
    public sealed record GuidRecord(string PropertyName, string Description, bool IsRequired) : FieldRecord(PropertyName);
    public sealed record DateTimeRecord(string PropertyName, string Description, bool IsRequired) : FieldRecord(PropertyName);
    public sealed record IntRecord(string PropertyName, string Description, bool IsRequired, int Min, int Max, string? Format) : FieldRecord(PropertyName);
    public sealed record EnumRecord(string PropertyName, string Description, bool IsRequired, string[] Options) : FieldRecord(PropertyName);
    public sealed record ObjectRecord(string PropertyName, IEnumerable<FieldRecord> Properties) : FieldRecord(PropertyName);
    public sealed record ArrayRecord(string PropertyName, string Description, IEnumerable<FieldRecord> ItemFormat) : FieldRecord(PropertyName);

}
