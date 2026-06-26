using System.Text.Json.Serialization;

namespace MedSafety.API.Models.External;

// ─── NLM DailyMed SPL API response models ───
// Step 1 – search:   GET https://dailymed.nlm.nih.gov/dailymed/services/v2/spls.json?drug_name={name}&pagesize=1
// Step 2 – sections: GET https://dailymed.nlm.nih.gov/dailymed/services/v2/spls/{setid}/sections.json

// ── SPL Search ──

public class DailyMedSearchResponse
{
    [JsonPropertyName("metadata")]
    public DailyMedMetadata? Metadata { get; set; }

    [JsonPropertyName("data")]
    public List<DailyMedSplItem>? Data { get; set; }
}

public class DailyMedMetadata
{
    [JsonPropertyName("total_elements")]
    public int TotalElements { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}

public class DailyMedSplItem
{
    [JsonPropertyName("setid")]
    public string? SetId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("published")]
    public string? Published { get; set; }
}

// ── SPL Sections ──

public class DailyMedSectionsResponse
{
    [JsonPropertyName("data")]
    public DailyMedSplData? Data { get; set; }
}

public class DailyMedSplData
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("sections")]
    public List<DailyMedSection>? Sections { get; set; }
}

public class DailyMedSection
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("linkname")]
    public string? LinkName { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
