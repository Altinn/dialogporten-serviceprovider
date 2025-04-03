namespace Digdir.BDB.Dialogporten.ServiceProvider;

public abstract record FieldRecord(string PropertyName)
{
    public sealed record StringRecord(string PropertyName, string Description, bool IsNullable, string? Format) : FieldRecord(PropertyName);
    public sealed record GuidRecord(string PropertyName, string Description, bool IsNullable) : FieldRecord(PropertyName);
    public sealed record DateTimeRecord(string PropertyName, string Description, bool IsNullable) : FieldRecord(PropertyName);
    public sealed record IntRecord(string PropertyName, string Description, bool IsNullable, int Min, int Max, string? Format) : FieldRecord(PropertyName);
    public sealed record EnumRecord(string PropertyName, string Description, bool IsNullable, string[] Options) : FieldRecord(PropertyName);
    public sealed record ObjectRecord(string PropertyName, IEnumerable<FieldRecord> Properties, bool IsNullable) : FieldRecord(PropertyName);
    public sealed record ArrayRecord(string PropertyName, string Description, IEnumerable<FieldRecord> ItemFormat, bool IsNullable) : FieldRecord(PropertyName);

}
