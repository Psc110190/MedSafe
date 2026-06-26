namespace MedSafety.API.Models;

public class CustomSafetyRule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public AlertLevel Level { get; set; } = AlertLevel.High;
    public string Category { get; set; } = string.Empty;
    public List<string> MedicationTerms { get; set; } = new();
    public List<string> ConditionTerms { get; set; } = new();
    public List<string> AllergyTerms { get; set; } = new();
    public List<string> SymptomTerms { get; set; } = new();
    public List<string> RiskFlagTerms { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
    public string Source { get; set; } = "Custom rule";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class UpsertCustomSafetyRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public AlertLevel Level { get; set; } = AlertLevel.High;
    public string Category { get; set; } = string.Empty;
    public List<string> MedicationTerms { get; set; } = new();
    public List<string> ConditionTerms { get; set; } = new();
    public List<string> AllergyTerms { get; set; } = new();
    public List<string> SymptomTerms { get; set; } = new();
    public List<string> RiskFlagTerms { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
    public string Source { get; set; } = "Custom rule";
}
