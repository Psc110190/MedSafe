namespace MedSafety.API.Models;

/// <summary>
/// Represents a patient profile submitted for safety screening.
/// </summary>
public class PatientProfile
{
    /// <summary>Patient identifier (optional).</summary>
    public string? PatientId { get; set; }

    /// <summary>Known drug / substance allergies (e.g., "Penicillin", "Sulfa", "NSAIDs").</summary>
    public List<string> Allergies { get; set; } = new();

    /// <summary>Existing medical conditions / comorbidities (e.g., "Myasthenia Gravis", "Renal Failure").</summary>
    public List<string> Comorbidities { get; set; } = new();

    /// <summary>Current chief complaint / diagnosis (e.g., "Heart attack", "Pneumonia").</summary>
    public List<string> CurrentComplaints { get; set; } = new();

    /// <summary>Medications the patient is already taking (generic names).</summary>
    public List<string> CurrentMedications { get; set; } = new();

    /// <summary>Medications being considered for prescription.</summary>
    public List<string> ProposedMedications { get; set; } = new();

    /// <summary>Age of the patient in years (optional, for age-specific warnings).</summary>
    public int? Age { get; set; }

    /// <summary>Pregnancy status.</summary>
    public bool IsPregnant { get; set; }

    /// <summary>Breastfeeding status.</summary>
    public bool IsBreastfeeding { get; set; }

    /// <summary>Current symptoms relevant to medication safety (e.g., dizziness, vomiting, sore throat).</summary>
    public List<string> Symptoms { get; set; } = new();

    /// <summary>Recent labs used for patient-context medication safety checks.</summary>
    public ClinicalLabs Labs { get; set; } = new();

    /// <summary>Recent vital signs used for patient-context medication safety checks.</summary>
    public VitalSigns Vitals { get; set; } = new();

    /// <summary>Structured clinical context flags that are not always represented as diagnoses.</summary>
    public PatientContextFlags ContextFlags { get; set; } = new();
}

public class ClinicalLabs
{
    public decimal? Glucose { get; set; }
    public decimal? EGfr { get; set; }
    public decimal? Potassium { get; set; }
    public decimal? Sodium { get; set; }
}

public class VitalSigns
{
    public int? HeartRate { get; set; }
    public int? SystolicBp { get; set; }
}

public class PatientContextFlags
{
    public bool Npo { get; set; }
    public bool PoorIntake { get; set; }
    public bool AcuteIllness { get; set; }
    public bool RecentSurgery { get; set; }
    public bool Dehydration { get; set; }
    public bool MetabolicAcidosis { get; set; }
    public bool Dka { get; set; }
    public bool AcuteKidneyInjury { get; set; }
    public bool Sepsis { get; set; }
    public bool Hypoxia { get; set; }
    public bool Shock { get; set; }
    public bool Anuria { get; set; }
    public bool BowelObstruction { get; set; }
    public bool UrinaryObstruction { get; set; }
    public bool AsthmaCopd { get; set; }
    public bool HeartBlock { get; set; }
    public bool DecompensatedHeartFailure { get; set; }
    public bool CardiacDisease { get; set; }
    public bool RecentMiOrUnstableAngina { get; set; }
    public bool AdrenalInsufficiency { get; set; }
    public bool ThyroidCancerHistory { get; set; }
    public bool Men2History { get; set; }
    public bool PancreatitisHistory { get; set; }
    public bool AngioedemaAceArbHistory { get; set; }
    public bool Neutropenia { get; set; }
    public bool Gout { get; set; }
    public bool DigoxinUse { get; set; }
    public bool PotassiumSupplement { get; set; }
    public bool PotassiumSparingDiuretic { get; set; }
    public bool AliskirenUse { get; set; }
    public bool LastAceInhibitorWithin36Hours { get; set; }
    public bool HeavyAlcoholUse { get; set; }
    public bool ReducedInsulin { get; set; }
}
