namespace MedSafety.API.Models;

/// <summary>
/// Represents a medication/drug in the knowledge base.
/// </summary>
public class Drug
{
    public string DrugId { get; set; } = string.Empty;
    public string GenericName { get; set; } = string.Empty;
    public List<string> BrandNames { get; set; } = new();
    public string DrugClass { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<string> Indications { get; set; } = new();
    public List<Contraindication> Contraindications { get; set; } = new();
    public List<string> BlackBoxWarnings { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> UseWithCaution { get; set; } = new();
    public List<DrugInteraction> Interactions { get; set; } = new();
    public List<string> AllergyGroups { get; set; } = new();
    public List<string> SideEffects { get; set; } = new();
}

/// <summary>
/// Represents a specific contraindication for a drug.
/// </summary>
public class Contraindication
{
    public string Condition { get; set; } = string.Empty;
    public SeverityLevel Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Represents a drug-drug interaction.
/// </summary>
public class DrugInteraction
{
    public string InteractingDrugId { get; set; } = string.Empty;
    public string InteractingDrugName { get; set; } = string.Empty;
    public InteractionSeverity Severity { get; set; }
    public string Effect { get; set; } = string.Empty;
    public string Mechanism { get; set; } = string.Empty;
    public string ClinicalManagement { get; set; } = string.Empty;
}

public enum SeverityLevel
{
    Absolute,       // Must NOT give
    Relative,       // Use with extreme caution
    Conditional     // Monitor closely
}

public enum InteractionSeverity
{
    Major,          // Avoid combination
    Moderate,       // Use with caution / monitor
    Minor           // Be aware
}
