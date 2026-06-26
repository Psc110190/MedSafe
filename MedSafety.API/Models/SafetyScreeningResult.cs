namespace MedSafety.API.Models;

/// <summary>
/// Complete safety screening result returned to the caller.
/// </summary>
public class SafetyScreeningResult
{
    public DateTime ScreenedAt { get; set; } = DateTime.UtcNow;
    public string? PatientId { get; set; }
    public List<DrugSafetyReport> DrugReports { get; set; } = new();
    public int TotalAlerts => DrugReports.Sum(r => r.TotalAlerts);
    public bool HasBlackBoxWarnings => DrugReports.Any(r => r.BlackBoxWarnings.Count > 0);
    public bool HasAbsoluteContraindications => DrugReports.Any(r => r.MustAvoidReasons.Count > 0);

    /// <summary>Data sources used to generate this report.</summary>
    public List<string> DataSources { get; set; } = new() { "MedSafety Static Knowledge Base" };
}

/// <summary>
/// Safety report for a single proposed drug.
/// </summary>
public class DrugSafetyReport
{
    public string DrugName { get; set; } = string.Empty;
    public string DrugId { get; set; } = string.Empty;
    public string DrugClass { get; set; } = string.Empty;

    /// <summary>Reasons the drug MUST be avoided (absolute contraindications).</summary>
    public List<SafetyAlert> MustAvoidReasons { get; set; } = new();

    /// <summary>FDA Black Box Warnings applicable.</summary>
    public List<SafetyAlert> BlackBoxWarnings { get; set; } = new();

    /// <summary>General warnings.</summary>
    public List<SafetyAlert> Warnings { get; set; } = new();

    /// <summary>Conditions that require extra caution / monitoring.</summary>
    public List<SafetyAlert> UseWithCaution { get; set; } = new();

    /// <summary>Interactions with current medications.</summary>
    public List<InteractionAlert> DrugInteractions { get; set; } = new();

    /// <summary>Allergy-related alerts.</summary>
    public List<SafetyAlert> AllergyAlerts { get; set; } = new();

    public SafetyVerdict OverallVerdict { get; set; }

    public int TotalAlerts =>
        MustAvoidReasons.Count + BlackBoxWarnings.Count + Warnings.Count +
        UseWithCaution.Count + DrugInteractions.Count + AllergyAlerts.Count;
}

public class SafetyAlert
{
    public AlertLevel Level { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Source { get; set; }
}

public class InteractionAlert
{
    public AlertLevel Level { get; set; }
    public string CurrentDrug { get; set; } = string.Empty;
    public string ProposedDrug { get; set; } = string.Empty;
    public string Effect { get; set; } = string.Empty;
    public string Mechanism { get; set; } = string.Empty;
    public string Management { get; set; } = string.Empty;
}

public enum AlertLevel
{
    Critical,   // Red - must stop
    High,       // Orange - serious concern
    Moderate,   // Yellow - caution
    Low         // Blue - informational
}

public enum SafetyVerdict
{
    DoNotUse,           // Absolute contraindication or black box
    UseWithExtremeCaution,  // Major concerns
    UseWithCaution,     // Moderate concerns
    GenerallyAcceptable // Low or no concerns found
}
