namespace MedSafety.API.Models;

/// <summary>
/// Patient-context medication safety response shaped for the UI.
/// </summary>
public class PatientContextSafetyResult
{
    public DateTime ScreenedAt { get; set; } = DateTime.UtcNow;
    public string? PatientId { get; set; }
    public string ReviewStatus { get; set; } = "full_kb_and_patient_context_rules_clinical_review_required";
    public PatientContextSnapshot PatientContext { get; set; } = new();
    public List<PatientMedicationAlert> Alerts { get; set; } = new();
    public List<PatientMedicationAlert> Warnings => Alerts
        .Where(a => a.Level != AlertLevel.Critical)
        .ToList();
    public List<MedicationClassification> SafeMedications { get; set; } = new();
    public List<MedicationClassification> MustAvoidMedications { get; set; } = new();
    public List<MedicationClassification> UseWithCautionMedications { get; set; } = new();
    public List<MissingContextItem> MissingContext { get; set; } = new();
    public List<UnrecognizedMedicationItem> UnrecognizedMedications { get; set; } = new();
    public List<string> DataSources { get; set; } = new()
    {
        "Curated disease-medication knowledge base",
        "MedSafety Static Knowledge Base",
        "Editable custom safety rules",
        "Patient-context rule engine"
    };
    public int TotalAlerts => Alerts.Count;
    public bool HasCriticalAlerts => Alerts.Any(a => a.Level == AlertLevel.Critical);
}

public class PatientContextSnapshot
{
    public List<string> Conditions { get; set; } = new();
    public List<string> CurrentMedications { get; set; } = new();
    public List<string> ProposedMedications { get; set; } = new();
    public List<string> Symptoms { get; set; } = new();
    public List<string> ActiveRiskFlags { get; set; } = new();
    public ClinicalLabs Labs { get; set; } = new();
    public VitalSigns Vitals { get; set; } = new();
    public bool IsPregnant { get; set; }
    public bool IsBreastfeeding { get; set; }
    public int? Age { get; set; }
}

public class PatientMedicationAlert
{
    public string RuleId { get; set; } = string.Empty;
    public AlertLevel Level { get; set; }
    public string Category { get; set; } = string.Empty;
    public string MedicationName { get; set; } = string.Empty;
    public string RxCui { get; set; } = string.Empty;
    public string DrugClass { get; set; } = string.Empty;
    public string ConditionName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public List<string> MatchedPatientFacts { get; set; } = new();
}

public class MedicationClassification
{
    public string MedicationName { get; set; } = string.Empty;
    public string RxCui { get; set; } = string.Empty;
    public string DrugClass { get; set; } = string.Empty;
    public string ConditionName { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public List<string> Reasons { get; set; } = new();
    public List<string> RuleIds { get; set; } = new();
    public string SafetyLabel { get; set; } = string.Empty;
}

public class MissingContextItem
{
    public string Field { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string MedicationName { get; set; } = string.Empty;
    public string RxCui { get; set; } = string.Empty;
}

public class UnrecognizedMedicationItem
{
    public string MedicationName { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
