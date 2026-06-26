using MedSafety.API.Models;

namespace MedSafety.API.Services;

/// <summary>
/// Calls the core MedicationSafetyService and post-processes the result by:
///   1. Scoring each alert for relevance against the patient's specific context
///      (allergies, comorbidities, age, pregnancy, current medications).
///   2. Filtering out alerts that are not relevant when StrictContextFilter = true.
///   3. Sorting alerts by relevance score (descending) within each category.
/// </summary>
public class ContextualAlertFilterService
{
    private readonly MedicationSafetyService _safetyService;
    private readonly ILogger<ContextualAlertFilterService> _logger;

    public ContextualAlertFilterService(
        MedicationSafetyService safetyService,
        ILogger<ContextualAlertFilterService> logger)
    {
        _safetyService = safetyService;
        _logger = logger;
    }

    public async Task<ContextualScreeningResult> ScreenWithContextAsync(ContextualScreeningRequest request)
    {
        var patient = request.Patient;

        // ── Step 1: Run the full safety screening ──────────────────────────
        _logger.LogInformation("Running full safety screening for contextual filter (patient: {Id})...",
            patient.PatientId ?? "anonymous");

        var fullResult = await _safetyService.ScreenMedicationsAsync(patient);

        // ── Step 2: Build patient context token set ────────────────────────
        var contextTokens = BuildContextTokens(patient);
        var ageGroup = ClassifyAgeGroup(patient.Age);

        // ── Step 3: Map to contextual result ──────────────────────────────
        var result = new ContextualScreeningResult
        {
            PatientId = fullResult.PatientId,
            ScreenedAt = fullResult.ScreenedAt,
            DataSources = fullResult.DataSources,
            TotalAlertsBeforeFilter = fullResult.TotalAlerts,
            PatientContext = new PatientContextSummary
            {
                Allergies = patient.Allergies,
                Comorbidities = patient.Comorbidities,
                CurrentMedications = patient.CurrentMedications,
                IsPregnant = patient.IsPregnant,
                IsBreastfeeding = patient.IsBreastfeeding,
                AgeGroup = ageGroup,
                Age = patient.Age
            }
        };

        foreach (var report in fullResult.DrugReports)
        {
            var contextualReport = BuildContextualReport(
                report, patient, contextTokens, ageGroup,
                request.MinimumAlertLevel, request.StrictContextFilter);

            result.DrugReports.Add(contextualReport);
        }

        _logger.LogInformation(
            "Contextual filter: {Before} total alerts → {After} relevant alerts ({Suppressed} suppressed).",
            result.TotalAlertsBeforeFilter, result.TotalRelevantAlerts, result.AlertsSuppressed);

        return result;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────

    private ContextualDrugReport BuildContextualReport(
        DrugSafetyReport report,
        PatientProfile patient,
        List<string> contextTokens,
        string? ageGroup,
        AlertLevel minLevel,
        bool strict)
    {
        int suppressed = 0;
        var notes = new List<string>();

        var ctx = new ContextualDrugReport
        {
            DrugName = report.DrugName,
            DrugId = report.DrugId,
            DrugClass = report.DrugClass,
            OverallVerdict = report.OverallVerdict
        };

        // Process each alert category
        ctx.MustAvoidReasons = FilterAlerts(report.MustAvoidReasons, contextTokens, patient, ageGroup,
            minLevel, strict, ref suppressed, categoryBonus: 30);   // Always high relevance

        ctx.BlackBoxWarnings = FilterAlerts(report.BlackBoxWarnings, contextTokens, patient, ageGroup,
            minLevel, strict, ref suppressed, categoryBonus: 20);

        ctx.AllergyAlerts = FilterAlerts(report.AllergyAlerts, contextTokens, patient, ageGroup,
            minLevel, strict, ref suppressed, categoryBonus: 25);   // Allergy = always relevant

        ctx.Warnings = FilterAlerts(report.Warnings, contextTokens, patient, ageGroup,
            minLevel, strict, ref suppressed, categoryBonus: 0);

        ctx.UseWithCaution = FilterAlerts(report.UseWithCaution, contextTokens, patient, ageGroup,
            minLevel, strict, ref suppressed, categoryBonus: 0);

        ctx.DrugInteractions = FilterInteractions(report.DrugInteractions, patient,
            minLevel, strict, ref suppressed);

        ctx.AlertsSuppressed = suppressed;

        if (suppressed > 0)
            notes.Add($"{suppressed} alert(s) were filtered out as not directly relevant to this patient's context.");

        if (patient.IsPregnant)
            notes.Add("Pregnancy context active – pregnancy-specific alerts are prioritised.");

        if (ageGroup == "Geriatric")
            notes.Add("Geriatric context active (age ≥ 65) – age-related precautions are prioritised.");

        if (ageGroup == "Pediatric")
            notes.Add("Pediatric context active (age < 18) – pediatric warnings are prioritised.");

        if (patient.Allergies.Count > 0)
            notes.Add($"Allergy context: {string.Join(", ", patient.Allergies)}.");

        ctx.FilteringNotes = notes;
        return ctx;
    }

    private List<ScoredAlert> FilterAlerts(
        List<SafetyAlert> alerts,
        List<string> contextTokens,
        PatientProfile patient,
        string? ageGroup,
        AlertLevel minLevel,
        bool strict,
        ref int suppressed,
        int categoryBonus)
    {
        var result = new List<ScoredAlert>();

        foreach (var alert in alerts)
        {
            // Skip below minimum level
            if (alert.Level > minLevel)
            {
                suppressed++;
                continue;
            }

            var (score, matched) = ScoreAlert(alert, contextTokens, patient, ageGroup, categoryBonus);

            if (strict && score < 30)
            {
                suppressed++;
                continue;
            }

            result.Add(new ScoredAlert
            {
                Level = alert.Level,
                Category = alert.Category,
                Message = alert.Message,
                Source = alert.Source,
                RelevanceScore = score,
                MatchedContextTokens = matched
            });
        }

        // Sort by relevance descending, then by severity ascending
        return result
            .OrderByDescending(a => a.RelevanceScore)
            .ThenBy(a => (int)a.Level)
            .ToList();
    }

    private List<ScoredInteraction> FilterInteractions(
        List<InteractionAlert> interactions,
        PatientProfile patient,
        AlertLevel minLevel,
        bool strict,
        ref int suppressed)
    {
        var result = new List<ScoredInteraction>();

        foreach (var ix in interactions)
        {
            if (ix.Level > minLevel)
            {
                suppressed++;
                continue;
            }

            var matched = new List<string>();
            int score = 0;

            // Interaction is directly relevant if it involves one of the patient's current meds
            bool currentMedMatch = patient.CurrentMedications.Any(m =>
                ix.CurrentDrug.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                m.Contains(ix.CurrentDrug, StringComparison.OrdinalIgnoreCase));

            if (currentMedMatch)
            {
                score += 80;
                var med = patient.CurrentMedications.FirstOrDefault(m =>
                    ix.CurrentDrug.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                    m.Contains(ix.CurrentDrug, StringComparison.OrdinalIgnoreCase));
                if (med != null) matched.Add(med);
            }

            // Boost by severity
            score += ix.Level switch
            {
                AlertLevel.Critical => 20,
                AlertLevel.High => 10,
                AlertLevel.Moderate => 5,
                _ => 0
            };

            score = Math.Min(score, 100);

            if (strict && score < 30)
            {
                suppressed++;
                continue;
            }

            result.Add(new ScoredInteraction
            {
                Level = ix.Level,
                CurrentDrug = ix.CurrentDrug,
                ProposedDrug = ix.ProposedDrug,
                Effect = ix.Effect,
                Mechanism = ix.Mechanism,
                Management = ix.Management,
                RelevanceScore = score,
                MatchedContextTokens = matched
            });
        }

        return result
            .OrderByDescending(i => i.RelevanceScore)
            .ThenBy(i => (int)i.Level)
            .ToList();
    }

    /// <summary>
    /// Score an alert 0-100 for relevance to the patient context.
    /// </summary>
    private (int score, List<string> matched) ScoreAlert(
        SafetyAlert alert,
        List<string> contextTokens,
        PatientProfile patient,
        string? ageGroup,
        int categoryBonus)
    {
        var matched = new List<string>();
        int score = categoryBonus;  // base bonus for the category type

        var text = $"{alert.Category} {alert.Message}".ToUpperInvariant();

        // ── Allergy match ──────────────────────────────────────────────
        foreach (var allergy in patient.Allergies)
        {
            if (text.Contains(allergy.ToUpperInvariant()))
            {
                score += 40;
                matched.Add($"Allergy: {allergy}");
            }
        }

        // ── Comorbidity / condition match ─────────────────────────────
        foreach (var cond in patient.Comorbidities)
        {
            if (text.Contains(cond.ToUpperInvariant()))
            {
                score += 35;
                matched.Add($"Condition: {cond}");
            }
        }

        // ── Current complaint match ───────────────────────────────────
        foreach (var complaint in patient.CurrentComplaints)
        {
            if (text.Contains(complaint.ToUpperInvariant()))
            {
                score += 15;
                matched.Add($"Complaint: {complaint}");
            }
        }

        // ── Current medication mentioned in alert ─────────────────────
        foreach (var med in patient.CurrentMedications)
        {
            if (text.Contains(med.ToUpperInvariant()))
            {
                score += 25;
                matched.Add($"Current med: {med}");
            }
        }

        // ── Age group match ───────────────────────────────────────────
        if (ageGroup == "Geriatric" &&
            (text.Contains("GERIATRIC") || text.Contains("ELDERLY") || text.Contains("OLDER ADULT")))
        {
            score += 30;
            matched.Add("Age group: Geriatric");
        }
        if (ageGroup == "Pediatric" &&
            (text.Contains("PEDIATRIC") || text.Contains("CHILD") || text.Contains("INFANT")))
        {
            score += 30;
            matched.Add("Age group: Pediatric");
        }

        // ── Pregnancy / breastfeeding ────────────────────────────────
        if (patient.IsPregnant &&
            (text.Contains("PREGNAN") || text.Contains("FETAL") || text.Contains("TERATOGEN")))
        {
            score += 35;
            matched.Add("Status: Pregnant");
        }
        if (patient.IsBreastfeeding &&
            (text.Contains("NURSING") || text.Contains("LACTATION") || text.Contains("BREASTFEED")))
        {
            score += 35;
            matched.Add("Status: Breastfeeding");
        }

        // ── Boost by alert severity ────────────────────────────────
        score += alert.Level switch
        {
            AlertLevel.Critical => 20,
            AlertLevel.High => 10,
            AlertLevel.Moderate => 5,
            _ => 0
        };

        // ── Generic/low-relevance indicators ─────────────────────────
        // If nothing matched patient context yet, give a small base score
        if (matched.Count == 0)
            score = Math.Max(score, 20);  // informational floor

        return (Math.Min(score, 100), matched);
    }

    /// <summary>
    /// Build a flat list of all patient context tokens for fast matching.
    /// </summary>
    private static List<string> BuildContextTokens(PatientProfile patient)
    {
        var tokens = new List<string>();
        tokens.AddRange(patient.Allergies);
        tokens.AddRange(patient.Comorbidities);
        tokens.AddRange(patient.CurrentMedications);
        tokens.AddRange(patient.CurrentComplaints);
        if (patient.IsPregnant) tokens.Add("Pregnant");
        if (patient.IsBreastfeeding) tokens.Add("Breastfeeding");
        if (patient.Age.HasValue)
        {
            if (patient.Age < 18) tokens.Add("Pediatric");
            if (patient.Age >= 65) tokens.Add("Geriatric");
        }
        return tokens;
    }

    private static string? ClassifyAgeGroup(int? age) =>
        age switch
        {
            < 18 => "Pediatric",
            >= 65 => "Geriatric",
            _ => "Adult"
        };
}
