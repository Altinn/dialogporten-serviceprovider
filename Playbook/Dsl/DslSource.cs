namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl;

/// <summary>
/// Strongly-typed root model populated by YamlDotNet from a .playbook.yaml file.
/// Field names map via HyphenatedNamingConvention (PascalCase ↔ lowercase-with-dashes).
/// </summary>
public sealed class PlaybookSource
{
    public string Party { get; set; } = "";
    public string ServiceResource { get; set; } = "";
    public string Language { get; set; } = "en";
    public InitialSeed? Initial { get; set; }
    public string Start { get; set; } = "";

    // Dictionary<,> preserves insertion order in .NET 9, which determines the cursor index per stage.
    public Dictionary<string, StageSource> Stages { get; set; } = new();

    // FCE values may be either a markdown string (shorthand) or an FceSource (object).
    // Stored as object? and normalised in the compiler.
    public Dictionary<string, object?> Fce { get; set; } = new();
}

public sealed class InitialSeed
{
    public string? Title { get; set; }
    public string? Summary { get; set; }
}

public sealed class StageSource
{
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? Status { get; set; }
    public string? ExtendedStatus { get; set; }
    public string? AdditionalInfo { get; set; }
    public string? Content { get; set; }

    // Polymorphic: string (just the type) or object (full ActivitySource).
    public object? Activity { get; set; }
    public List<object>? Activities { get; set; }

    public List<TransmissionSource>? Transmissions { get; set; }

    // Polymorphic: string ("Label → target") or object (full ActionSource).
    public List<object>? Actions { get; set; }
}

public sealed class TransmissionSource
{
    public string Type { get; set; } = "";
    public string? From { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? Content { get; set; }
}

public sealed class ActivitySource
{
    public string? Type { get; set; }
    public string? Description { get; set; }
    public string? By { get; set; }
}

public sealed class ActionSource
{
    public string? Label { get; set; }
    public string? Target { get; set; }
    public string? Priority { get; set; }
}

public sealed class FceSource
{
    public string? MediaType { get; set; }
    public string Content { get; set; } = "";
}
