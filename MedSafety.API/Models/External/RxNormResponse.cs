using System.Text.Json.Serialization;

namespace MedSafety.API.Models.External;

// ─── NIH RxNorm Drug Interaction API response models ───
// Step 1 – resolve RxCUI: https://rxnav.nlm.nih.gov/REST/rxcui.json?name=warfarin
// Step 2 – get interactions: https://rxnav.nlm.nih.gov/REST/interaction/interaction.json?rxcui=11289

// ── RxCUI lookup ──
public class RxCuiResponse
{
    [JsonPropertyName("idGroup")]
    public RxCuiIdGroup? IdGroup { get; set; }
}

public class RxCuiIdGroup
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("rxnormId")]
    public List<string>? RxnormId { get; set; }
}

// ── Interaction lookup ──
public class RxInteractionResponse
{
    [JsonPropertyName("interactionTypeGroup")]
    public List<InteractionTypeGroup>? InteractionTypeGroup { get; set; }
}

public class InteractionTypeGroup
{
    [JsonPropertyName("sourceName")]
    public string? SourceName { get; set; }

    [JsonPropertyName("sourceDisclaimer")]
    public string? SourceDisclaimer { get; set; }

    [JsonPropertyName("interactionType")]
    public List<InteractionType>? InteractionType { get; set; }
}

public class InteractionType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("minConceptItem")]
    public MinConceptItem? MinConceptItem { get; set; }

    [JsonPropertyName("interactionPair")]
    public List<InteractionPair>? InteractionPair { get; set; }
}

public class MinConceptItem
{
    [JsonPropertyName("rxcui")]
    public string? Rxcui { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("tty")]
    public string? Tty { get; set; }
}

public class InteractionPair
{
    [JsonPropertyName("interactionConcept")]
    public List<InteractionConcept>? InteractionConcept { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class InteractionConcept
{
    [JsonPropertyName("minConceptItem")]
    public MinConceptItem? MinConceptItem { get; set; }

    [JsonPropertyName("sourceConceptItem")]
    public SourceConceptItem? SourceConceptItem { get; set; }
}

public class SourceConceptItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
