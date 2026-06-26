using System.Text.Json.Serialization;

namespace MedSafety.API.Models.External;

// ─── OpenFDA Drug Label API response models ───
// Endpoint: https://api.fda.gov/drug/label.json

public class OpenFdaLabelResponse
{
    [JsonPropertyName("meta")]
    public OpenFdaMeta? Meta { get; set; }

    [JsonPropertyName("results")]
    public List<OpenFdaLabelResult>? Results { get; set; }
}

public class OpenFdaMeta
{
    [JsonPropertyName("disclaimer")]
    public string? Disclaimer { get; set; }

    [JsonPropertyName("results")]
    public OpenFdaMetaResults? Results { get; set; }
}

public class OpenFdaMetaResults
{
    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class OpenFdaLabelResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>FDA Black Box Warnings (strongest safety warnings).</summary>
    [JsonPropertyName("boxed_warning")]
    public List<string>? BoxedWarning { get; set; }

    /// <summary>Contraindications section of the drug label.</summary>
    [JsonPropertyName("contraindications")]
    public List<string>? Contraindications { get; set; }

    /// <summary>Warnings and precautions.</summary>
    [JsonPropertyName("warnings")]
    public List<string>? Warnings { get; set; }

    /// <summary>Warnings and precautions (structured label format).</summary>
    [JsonPropertyName("warnings_and_cautions")]
    public List<string>? WarningsAndCautions { get; set; }

    /// <summary>Drug interaction information.</summary>
    [JsonPropertyName("drug_interactions")]
    public List<string>? DrugInteractions { get; set; }

    /// <summary>Adverse reactions / side effects.</summary>
    [JsonPropertyName("adverse_reactions")]
    public List<string>? AdverseReactions { get; set; }

    /// <summary>Use in specific populations (pregnancy, pediatric, geriatric).</summary>
    [JsonPropertyName("use_in_specific_populations")]
    public List<string>? UseInSpecificPopulations { get; set; }

    /// <summary>Pregnancy category and information.</summary>
    [JsonPropertyName("pregnancy")]
    public List<string>? Pregnancy { get; set; }

    /// <summary>Nursing mothers / lactation info.</summary>
    [JsonPropertyName("nursing_mothers")]
    public List<string>? NursingMothers { get; set; }

    /// <summary>Pediatric use information.</summary>
    [JsonPropertyName("pediatric_use")]
    public List<string>? PediatricUse { get; set; }

    /// <summary>Geriatric use information.</summary>
    [JsonPropertyName("geriatric_use")]
    public List<string>? GeriatricUse { get; set; }

    /// <summary>Indications and usage.</summary>
    [JsonPropertyName("indications_and_usage")]
    public List<string>? IndicationsAndUsage { get; set; }

    /// <summary>OpenFDA metadata (brand/generic names, route, etc.).</summary>
    [JsonPropertyName("openfda")]
    public OpenFdaDrugInfo? OpenFda { get; set; }
}

public class OpenFdaDrugInfo
{
    [JsonPropertyName("brand_name")]
    public List<string>? BrandName { get; set; }

    [JsonPropertyName("generic_name")]
    public List<string>? GenericName { get; set; }

    [JsonPropertyName("manufacturer_name")]
    public List<string>? ManufacturerName { get; set; }

    [JsonPropertyName("product_type")]
    public List<string>? ProductType { get; set; }

    [JsonPropertyName("route")]
    public List<string>? Route { get; set; }

    [JsonPropertyName("substance_name")]
    public List<string>? SubstanceName { get; set; }

    [JsonPropertyName("rxcui")]
    public List<string>? RxCui { get; set; }

    [JsonPropertyName("pharm_class_epc")]
    public List<string>? PharmClassEpc { get; set; }
}
