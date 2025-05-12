namespace Digdir.BDB.Dialogporten.ServiceProvider;

public class MaskinportenSchemaResource
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

public class Keyword
{
    public string Word { get; set; } = null!;
    public string Language { get; set; } = null!;
}

public class LocalizedStrings
{
    public string En { get; set; } = null!;
    public string Nb { get; set; } = null!;
    public string Nn { get; set; } = null!;
}

public record ResourceReference
{
    public string ReferenceSource { get; set; } = null!;
    public string Reference { get; set; } = null!;
    public string ReferenceType { get; set; } = null!;
}

public record CompetentAuthority
{
    public LocalizedStrings Name { get; set; } = new();
    public string Organization { get; set; } = null!;
    public string Orgcode { get; set; } = null!;
}

public record AuthorizationReference
{
    public string Id { get; set; } = null!;
    public string Value { get; set; } = null!;
}

//{
//"identifier": "ttd-am-k6",
//"title": {
//    "en": "Maskinporten Schema - AM - K6",
//    "nb": "Maskinporten Schema - AM - K6",
//    "nn": "Maskinporten Schema - AM - K6"
//},
//"description": {
//    "en": "Maskinporten Schema test resource for automated tests",
//    "nb": "Maskinporten Schema test resource for automatiserte tester",
//    "nn": "Maskinporten Schema test resource for automatiserte testar"
//},
//"rightDescription": {
//    "en": "Access to the test scopes",
//    "nb": "Tilgang til test scopene",
//    "nn": "Tilgong til test omfang"
//},
//"homepage": "https://www.digdir.no/",
//"status": "Active",
//"isPartOf": "Altinn",
//"resourceReferences": [
//{
//    "referenceSource": "ExternalPlatform",
//    "reference": "test:am/k6.read",
//    "referenceType": "MaskinportenScope"
//},
//{
//"referenceSource": "ExternalPlatform",
//"reference": "test:am/k6.write",
//"referenceType": "MaskinportenScope"
//}
//],
//"delegable": true,
//"visible": true,
//"hasCompetentAuthority": {
//    "name": {
//        "en": "Test departement",
//        "nb": "Testdepartement",
//        "nn": "Testdepartement"
//    },
//    "organization": "991825827",
//    "orgcode": "TTD"
//},
//"keywords": [],
//"accessListMode": "Disabled",
//"selfIdentifiedUserEnabled": false,
//"enterpriseUserEnabled": false,
//"resourceType": "MaskinportenSchema",
//"authorizationReference": [
//{
//    "id": "urn:altinn:resource",
//    "value": "ttd-am-k6"
//}
//]
//},
