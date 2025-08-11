namespace Digdir.BDB.Dialogporten.ServiceProvider;

public sealed class MaskinportenSchemaResource
{
    public string Identifier { get; set; } = null!;
    public LocalizedStrings Title { get; set; } = new();
    public LocalizedStrings Description { get; set; } = new();
    public LocalizedStrings RightDescription { get; set; } = new();
    public string Homepage { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string IsPartOf { get; set; } = null!;
    public List<ResourceReference> ResourceReferences { get; set; } = new();
    public bool Delegable { get; set; }
    public bool Visible { get; set; }
    public CompetentAuthority HasCompetentAuthority { get; set; } = new();
    public List<Keyword> Keywords { get; set; } = new();
    public string AccessListMode { get; set; } = null!;
    public bool SelfIdentifiedUserEnabled { get; set; }
    public bool EnterpriseUserEnabled { get; set; }
    public string ResourceType { get; set; } = null!;
    public List<AuthorizationReference> AuthorizationReference { get; set; } = new();
}

public sealed class Keyword
{
    public string Word { get; set; } = null!;
    public string Language { get; set; } = null!;
}

public sealed class LocalizedStrings
{
    public string En { get; set; } = null!;
    public string Nb { get; set; } = null!;
    public string Nn { get; set; } = null!;
}

public sealed record ResourceReference
{
    public string ReferenceSource { get; set; } = null!;
    public string Reference { get; set; } = null!;
    public string ReferenceType { get; set; } = null!;
}

public sealed record CompetentAuthority
{
    public LocalizedStrings Name { get; set; } = new();
    public string Organization { get; set; } = null!;
    public string Orgcode { get; set; } = null!;
}

public sealed record AuthorizationReference
{
    public string Id { get; set; } = null!;
    public string Value { get; set; } = null!;
}