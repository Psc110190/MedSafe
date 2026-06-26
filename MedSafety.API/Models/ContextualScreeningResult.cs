namespace MedSafety.API.Models;

/// <summary>
/// Request for a context-aware safety screening.
/// Wraps the standard PatientProfile and adds optional filter preferences.
/// The service will call the full safety screener and then filter/score the
/// results so only alerts relevant to THIS patient's specific context are surfaced.
/// </summary>
public class ContextualScreeningRequest
{
    /// <summary>The full patient profile (same as the standard screen endpoint).</summary>
    public PatientProfile Patient { get; set; } = new();

    /// <summary>
    /// Minimum alert level to include in the filtered output.
    /// Default: Low (include everything, but scored).
    /// Set to Moderate or High to suppress informational noise.
    /// </summary>
    public AlertLevel MinimumAlertLevel { get; set; } = AlertLevel.Low;

    /// <summary>
    /// When true, only alerts that directly mention a patient-specific context
    /// (a comorbidity, allergy, medication, age group, or pregnancy) are returned.
    /// When false, all alerts are returned but with relevance scores attached.
    /// </summary>
    public bool StrictContextFilter { get; set; } = false;
}

// ────────────────────────────────────────────────────────────────────────────
// Response models
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Context-aware screening result – extends the standard result with
/// relevance scoring and filtering metadata.
/// </summary>
public class ContextualScreeningResult
{
    public DateTime ScreenedAt { get; set; } = DateTime.UtcNow;
    public string? PatientId { get; set; }

    /// <summary>Per-drug filtered and scored reports.</summary>
    public List<ContextualDrugReport> DrugReports { get; set; } = new();

    /// <summary>Total relevant alerts across all drugs.</summary>
    public int TotalRelevantAlerts => DrugReports.Sum(r => r.TotalRelevantAlerts);

    /// <summary>Total alerts before filtering (full result count).</summary>
    public int TotalAlertsBeforeFilter { get; set; }

    /// <summary>Number of alerts suppressed by context filtering.</summary>
    public int AlertsSuppressed => TotalAlertsBeforeFilter - TotalRelevantAlerts;

    public bool HasAbsoluteContraindications => DrugReports.Any(r => r.MustAvoidReasons.Count > 0);
    public bool HasBlackBoxWarnings => DrugReports.Any(r => r.BlackBoxWarnings.Count > 0);

    /// <summary>
    /// Drugs that have zero relevant alerts after context filtering — considered safe
    /// for this specific patient given their allergies, comorbidities and current medications.
    /// </summary>
    public List<SafeMedicationSummary> SafeMedications => DrugReports
        .Where(r => r.TotalRelevantAlerts == 0)
        .Select(r => new SafeMedicationSummary
        {
            DrugName = r.DrugName,
            DrugId = r.DrugId,
            DrugClass = r.DrugClass,
            OverallVerdict = r.OverallVerdict,
            AlertsSuppressed = r.AlertsSuppressed,
            Note = r.AlertsSuppressed > 0
                ? $"No alerts relevant to this patient's context. {r.AlertsSuppressed} general alert(s) were screened and found not applicable."
                : "No safety concerns found in the knowledge base for this patient profile."
        })
        .ToList();

    /// <summary>Patient-level context summary used for filtering.</summary>
    public PatientContextSummary PatientContext { get; set; } = new();

    /// <summary>Data sources used.</summary>
    public List<string> DataSources { get; set; } = new();
}

/// <summary>
/// Per-drug safety report enriched with per-alert relevance scores.
/// </summary>
public class ContextualDrugReport
{
    public string DrugName { get; set; } = string.Empty;
    public string DrugId { get; set; } = string.Empty;
    public string DrugClass { get; set; } = string.Empty;
    public SafetyVerdict OverallVerdict { get; set; }

    /// <summary>Must-avoid alerts relevant to this patient.</summary>
    public List<ScoredAlert> MustAvoidReasons { get; set; } = new();

    /// <summary>Black box warnings relevant to this patient.</summary>
    public List<ScoredAlert> BlackBoxWarnings { get; set; } = new();

    /// <summary>General warnings relevant to this patient.</summary>
    public List<ScoredAlert> Warnings { get; set; } = new();

    /// <summary>Use-with-caution alerts relevant to this patient.</summary>
    public List<ScoredAlert> UseWithCaution { get; set; } = new();

    /// <summary>Drug interactions relevant to this patient's current medications.</summary>
    public List<ScoredInteraction> DrugInteractions { get; set; } = new();

    /// <summary>Allergy alerts relevant to this patient.</summary>
    public List<ScoredAlert> AllergyAlerts { get; set; } = new();

    /// <summary>Number of alerts suppressed for this drug.</summary>
    public int AlertsSuppressed { get; set; }

    /// <summary>Human-readable reasons why certain alerts were filtered out.</summary>
    public List<string> FilteringNotes { get; set; } = new();

    public int TotalRelevantAlerts =>
        MustAvoidReasons.Count + BlackBoxWarnings.Count + Warnings.Count +
        UseWithCaution.Count + DrugInteractions.Count + AllergyAlerts.Count;
}

/// <summary>
/// A safety alert enriched with a relevance score and matched context tokens.
/// </summary>
public class ScoredAlert
{
    public AlertLevel Level { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Source { get; set; }

    /// <summary>
    /// Relevance score 0–100.
    /// 90–100 = directly matches patient context (allergy / comorbidity / age group).
    /// 60–89  = partially relevant (drug class or population match).
    /// 30–59  = general warning – may or may not apply.
    /// 0–29   = informational / low patient-specific relevance.
    /// </summary>
    public int RelevanceScore { get; set; }

    /// <summary>Which patient context tokens drove this score.</summary>
    public List<string> MatchedContextTokens { get; set; } = new();
}

/// <summary>
/// An interaction alert enriched with relevance scoring.
/// </summary>
public class ScoredInteraction
{
    public AlertLevel Level { get; set; }
    public string CurrentDrug { get; set; } = string.Empty;
    public string ProposedDrug { get; set; } = string.Empty;
    public string Effect { get; set; } = string.Empty;
    public string Mechanism { get; set; } = string.Empty;
    public string Management { get; set; } = string.Empty;

    /// <summary>Relevance score 0–100.</summary>
    public int RelevanceScore { get; set; }

    /// <summary>Which current medication triggered the interaction match.</summary>
    public List<string> MatchedContextTokens { get; set; } = new();
}

/// <summary>
/// Summary entry for a medication that was screened and found safe
/// for this specific patient's context.
/// </summary>
public class SafeMedicationSummary
{
    public string DrugName { get; set; } = string.Empty;
    public string DrugId { get; set; } = string.Empty;
    public string DrugClass { get; set; } = string.Empty;
    public SafetyVerdict OverallVerdict { get; set; }

    /// <summary>Number of general alerts that existed but were filtered as not patient-relevant.</summary>
    public int AlertsSuppressed { get; set; }

    /// <summary>Human-readable explanation of why this drug is considered safe for this patient.</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// Summary of the patient context tokens used during filtering.
/// </summary>
public class PatientContextSummary
{
    public List<string> Allergies { get; set; } = new();
    public List<string> Comorbidities { get; set; } = new();
    public List<string> CurrentMedications { get; set; } = new();
    public bool IsPregnant { get; set; }
    public bool IsBreastfeeding { get; set; }
    public string? AgeGroup { get; set; }  // "Pediatric", "Adult", "Geriatric"
    public int? Age { get; set; }
}
