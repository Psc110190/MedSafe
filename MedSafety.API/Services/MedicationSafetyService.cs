using MedSafety.API.Data;
using MedSafety.API.Models;

namespace MedSafety.API.Services;

/// <summary>
/// Core service that performs medication safety screening against a patient profile.
/// Checks allergies, comorbidity contraindications, black box warnings, drug interactions,
/// and generates a comprehensive safety report.
/// Supplements static knowledge base with live data from OpenFDA and NIH RxNorm APIs.
/// </summary>
public class MedicationSafetyService
{
    private readonly ExternalDrugDataService _externalService;
    private readonly ILogger<MedicationSafetyService> _logger;

    public MedicationSafetyService(ExternalDrugDataService externalService, ILogger<MedicationSafetyService> logger)
    {
        _externalService = externalService;
        _logger = logger;
    }

    /// <summary>
    /// Perform a full safety screening (synchronous – static data only).
    /// </summary>
    public SafetyScreeningResult ScreenMedications(PatientProfile patient)
    {
        var result = new SafetyScreeningResult
        {
            PatientId = patient.PatientId,
            ScreenedAt = DateTime.UtcNow
        };

        foreach (var proposed in patient.ProposedMedications)
        {
            var drug = MedicationKnowledgeBase.FindDrug(proposed);
            if (drug == null)
            {
                result.DrugReports.Add(new DrugSafetyReport
                {
                    DrugName = proposed,
                    DrugId = proposed.ToLowerInvariant(),
                    Warnings = new() { new SafetyAlert
                    {
                        Level = AlertLevel.Low,
                        Category = "Unknown Drug",
                        Message = $"'{proposed}' was not found in the knowledge base. Please verify the drug name."
                    }},
                    OverallVerdict = SafetyVerdict.GenerallyAcceptable
                });
                continue;
            }

            var report = new DrugSafetyReport
            {
                DrugName = drug.GenericName,
                DrugId = drug.DrugId,
                DrugClass = drug.DrugClass
            };

            // 1. Check Allergy Alerts
            CheckAllergies(patient, drug, report);

            // 2. Check Comorbidity Contraindications
            CheckComorbidityContraindications(patient, drug, report);

            // 3. Check Black Box Warnings
            CheckBlackBoxWarnings(patient, drug, report);

            // 4. Check General Warnings  
            CheckWarnings(patient, drug, report);

            // 5. Check Use With Caution
            CheckUseWithCaution(patient, drug, report);

            // 6. Check Drug-Drug Interactions with current medications
            CheckDrugInteractions(patient, drug, report);

            // 7. Check Pregnancy / Breastfeeding
            CheckPregnancyBreastfeeding(patient, drug, report);

            // 8. Check Age-specific warnings
            CheckAgeWarnings(patient, drug, report);

            // 9. Determine overall verdict
            report.OverallVerdict = DetermineVerdict(report);

            result.DrugReports.Add(report);
        }

        return result;
    }

    /// <summary>
    /// Perform a full safety screening (async – static + external API data).
    /// Enriches results with live data from OpenFDA and NIH RxNorm when available.
    /// For drugs NOT in the static knowledge base, falls back entirely to external APIs.
    /// </summary>
    public async Task<SafetyScreeningResult> ScreenMedicationsAsync(PatientProfile patient)
    {
        var result = new SafetyScreeningResult
        {
            PatientId = patient.PatientId,
            ScreenedAt = DateTime.UtcNow
        };

        foreach (var proposed in patient.ProposedMedications)
        {
            var drug = MedicationKnowledgeBase.FindDrug(proposed);

            if (drug != null)
            {
                // ── Drug found in static KB → run static checks + enrich ──
                var report = BuildStaticReport(patient, drug);

                if (_externalService.IsEnabled)
                {
                    try
                    {
                        await _externalService.EnrichReportAsync(report, patient);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "External enrichment failed for '{Drug}'. Static results still valid.", proposed);
                    }
                }

                report.OverallVerdict = DetermineVerdict(report);
                result.DrugReports.Add(report);
            }
            else if (_externalService.IsEnabled)
            {
                // ── Drug NOT in static KB → try external APIs ──
                _logger.LogInformation("'{Drug}' not in static KB. Looking up via OpenFDA/RxNorm ...", proposed);

                try
                {
                    var externalReport = await _externalService.BuildReportFromExternalAsync(proposed, patient);

                    if (externalReport != null)
                    {
                        externalReport.OverallVerdict = DetermineVerdict(externalReport);
                        result.DrugReports.Add(externalReport);
                    }
                    else
                    {
                        // Not found in external APIs either
                        result.DrugReports.Add(new DrugSafetyReport
                        {
                            DrugName = proposed,
                            DrugId = proposed.ToLowerInvariant(),
                            Warnings = new() { new SafetyAlert
                            {
                                Level = AlertLevel.Moderate,
                                Category = "Drug Not Found",
                                Message = $"'{proposed}' was not found in the static knowledge base or external sources (OpenFDA, RxNorm). Please verify the drug name or spelling.",
                                Source = "MedSafety"
                            }},
                            OverallVerdict = SafetyVerdict.UseWithCaution
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "External lookup failed for '{Drug}'", proposed);
                    result.DrugReports.Add(new DrugSafetyReport
                    {
                        DrugName = proposed,
                        DrugId = proposed.ToLowerInvariant(),
                        Warnings = new() { new SafetyAlert
                        {
                            Level = AlertLevel.Low,
                            Category = "Lookup Failed",
                            Message = $"'{proposed}' was not found in the static knowledge base, and external API lookup failed. Please verify the drug name.",
                            Source = "MedSafety"
                        }},
                        OverallVerdict = SafetyVerdict.UseWithCaution
                    });
                }
            }
            else
            {
                // External APIs disabled and not in static KB
                result.DrugReports.Add(new DrugSafetyReport
                {
                    DrugName = proposed,
                    DrugId = proposed.ToLowerInvariant(),
                    Warnings = new() { new SafetyAlert
                    {
                        Level = AlertLevel.Low,
                        Category = "Unknown Drug",
                        Message = $"'{proposed}' was not found in the knowledge base. Please verify the drug name."
                    }},
                    OverallVerdict = SafetyVerdict.GenerallyAcceptable
                });
            }
        }

        result.DataSources = _externalService.IsEnabled
            ? new() { "MedSafety Static Knowledge Base", "OpenFDA Drug Label API (api.fda.gov)", "NIH RxNorm Interaction API (rxnav.nlm.nih.gov)", "NLM DailyMed SPL API (dailymed.nlm.nih.gov)" }
            : new() { "MedSafety Static Knowledge Base" };

        return result;
    }

    /// <summary>
    /// Build a DrugSafetyReport from the static knowledge base checks.
    /// </summary>
    private DrugSafetyReport BuildStaticReport(PatientProfile patient, Drug drug)
    {
        var report = new DrugSafetyReport
        {
            DrugName = drug.GenericName,
            DrugId = drug.DrugId,
            DrugClass = drug.DrugClass
        };

        CheckAllergies(patient, drug, report);
        CheckComorbidityContraindications(patient, drug, report);
        CheckBlackBoxWarnings(patient, drug, report);
        CheckWarnings(patient, drug, report);
        CheckUseWithCaution(patient, drug, report);
        CheckDrugInteractions(patient, drug, report);
        CheckPregnancyBreastfeeding(patient, drug, report);
        CheckAgeWarnings(patient, drug, report);
        report.OverallVerdict = DetermineVerdict(report);

        return report;
    }

    /// <summary>
    /// Get all available drugs in the knowledge base.
    /// </summary>
    public IReadOnlyList<Drug> GetAllDrugs() => MedicationKnowledgeBase.GetAllDrugs();

    /// <summary>
    /// Search for drugs by name, class, or category.
    /// </summary>
    public List<Drug> SearchDrugs(string query) => MedicationKnowledgeBase.SearchDrugs(query);

    /// <summary>
    /// Get detailed info for a specific drug.
    /// </summary>
    public Drug? GetDrugInfo(string name) => MedicationKnowledgeBase.FindDrug(name);

    // ──────────────────────────────────────────────────────
    // PRIVATE SCREENING METHODS
    // ──────────────────────────────────────────────────────

    private void CheckAllergies(PatientProfile patient, Drug drug, DrugSafetyReport report)
    {
        foreach (var allergy in patient.Allergies)
        {
            // Direct match
            if (drug.AllergyGroups.Any(ag => ag.Equals(allergy, StringComparison.OrdinalIgnoreCase)) ||
                drug.GenericName.Equals(allergy, StringComparison.OrdinalIgnoreCase))
            {
                report.AllergyAlerts.Add(new SafetyAlert
                {
                    Level = AlertLevel.Critical,
                    Category = "ALLERGY - Direct Match",
                    Message = $"Patient has a known allergy to '{allergy}'. {drug.GenericName} belongs to the same group ({string.Join(", ", drug.AllergyGroups)}). CONTRAINDICATED.",
                    Source = "Allergy Screening"
                });
                report.MustAvoidReasons.Add(new SafetyAlert
                {
                    Level = AlertLevel.Critical,
                    Category = "Allergy Contraindication",
                    Message = $"MUST AVOID: Patient is allergic to '{allergy}'. {drug.GenericName} is in the same drug class/group.",
                    Source = "Allergy Screening"
                });
                continue;
            }

            // Cross-reactivity check via allergy group map
            if (MedicationKnowledgeBase.AllergyGroupMap.TryGetValue(allergy, out var relatedGroups))
            {
                var crossMatch = drug.AllergyGroups.Intersect(relatedGroups, StringComparer.OrdinalIgnoreCase).ToList();
                if (crossMatch.Any())
                {
                    report.AllergyAlerts.Add(new SafetyAlert
                    {
                        Level = AlertLevel.High,
                        Category = "ALLERGY - Cross-Reactivity",
                        Message = $"Patient is allergic to '{allergy}'. {drug.GenericName} has potential cross-reactivity via: {string.Join(", ", crossMatch)}. Use with extreme caution or AVOID.",
                        Source = "Cross-Reactivity Screening"
                    });
                }
            }
        }
    }

    private void CheckComorbidityContraindications(PatientProfile patient, Drug drug, DrugSafetyReport report)
    {
        foreach (var comorbidity in patient.Comorbidities)
        {
            foreach (var ci in drug.Contraindications)
            {
                if (ConditionMatches(comorbidity, ci.Condition))
                {
                    var alert = new SafetyAlert
                    {
                        Level = ci.Severity == SeverityLevel.Absolute ? AlertLevel.Critical :
                                ci.Severity == SeverityLevel.Relative ? AlertLevel.High : AlertLevel.Moderate,
                        Category = ci.Severity == SeverityLevel.Absolute ? "ABSOLUTE CONTRAINDICATION" :
                                   ci.Severity == SeverityLevel.Relative ? "RELATIVE CONTRAINDICATION" : "CONDITIONAL CONTRAINDICATION",
                        Message = $"{drug.GenericName} + {ci.Condition}: {ci.Description}",
                        Source = ci.Source
                    };

                    if (ci.Severity == SeverityLevel.Absolute)
                    {
                        report.MustAvoidReasons.Add(alert);
                    }
                    else if (ci.Severity == SeverityLevel.Relative)
                    {
                        report.Warnings.Add(alert);
                    }
                    else
                    {
                        report.UseWithCaution.Add(alert);
                    }
                }
            }
        }
    }

    private void CheckBlackBoxWarnings(PatientProfile patient, Drug drug, DrugSafetyReport report)
    {
        foreach (var bbw in drug.BlackBoxWarnings)
        {
            // Check if any patient comorbidity is specifically mentioned in black box
            bool isRelevant = patient.Comorbidities.Any(c =>
                bbw.Contains(c, StringComparison.OrdinalIgnoreCase) ||
                ConditionSynonymsMatch(c, bbw));

            // Also relevant if pregnancy is mentioned
            if (patient.IsPregnant && bbw.Contains("pregnan", StringComparison.OrdinalIgnoreCase))
                isRelevant = true;

            report.BlackBoxWarnings.Add(new SafetyAlert
            {
                Level = isRelevant ? AlertLevel.Critical : AlertLevel.High,
                Category = isRelevant ? "BLACK BOX WARNING - DIRECTLY RELEVANT" : "BLACK BOX WARNING",
                Message = $"[{drug.GenericName}] FDA BLACK BOX: {bbw}",
                Source = "FDA Black Box Warning"
            });

            // If directly relevant, also add to must-avoid
            if (isRelevant)
            {
                report.MustAvoidReasons.Add(new SafetyAlert
                {
                    Level = AlertLevel.Critical,
                    Category = "Black Box - Patient Specific",
                    Message = $"MUST AVOID: {drug.GenericName} has a BLACK BOX WARNING directly relevant to this patient: {bbw}",
                    Source = "FDA Black Box Warning"
                });
            }
        }
    }

    private void CheckWarnings(PatientProfile patient, Drug drug, DrugSafetyReport report)
    {
        foreach (var warning in drug.Warnings)
        {
            bool isRelevant = patient.Comorbidities.Any(c =>
                warning.Contains(c, StringComparison.OrdinalIgnoreCase) ||
                ConditionSynonymsMatch(c, warning));

            if (isRelevant)
            {
                report.Warnings.Add(new SafetyAlert
                {
                    Level = AlertLevel.High,
                    Category = "WARNING - Patient Relevant",
                    Message = $"[{drug.GenericName}] {warning}",
                    Source = "FDA Label"
                });
            }
            else
            {
                report.Warnings.Add(new SafetyAlert
                {
                    Level = AlertLevel.Moderate,
                    Category = "General Warning",
                    Message = $"[{drug.GenericName}] {warning}",
                    Source = "FDA Label"
                });
            }
        }
    }

    private void CheckUseWithCaution(PatientProfile patient, Drug drug, DrugSafetyReport report)
    {
        foreach (var caution in drug.UseWithCaution)
        {
            bool isRelevant = patient.Comorbidities.Any(c =>
                caution.Contains(c, StringComparison.OrdinalIgnoreCase) ||
                ConditionSynonymsMatch(c, caution));

            if (isRelevant)
            {
                report.UseWithCaution.Add(new SafetyAlert
                {
                    Level = AlertLevel.Moderate,
                    Category = "USE WITH CAUTION - Patient Relevant",
                    Message = $"[{drug.GenericName}] {caution}",
                    Source = "FDA Label"
                });
            }
            else
            {
                report.UseWithCaution.Add(new SafetyAlert
                {
                    Level = AlertLevel.Low,
                    Category = "Use With Caution",
                    Message = $"[{drug.GenericName}] {caution}",
                    Source = "FDA Label"
                });
            }
        }
    }

    private void CheckDrugInteractions(PatientProfile patient, Drug drug, DrugSafetyReport report)
    {
        foreach (var currentMed in patient.CurrentMedications)
        {
            foreach (var interaction in drug.Interactions)
            {
                if (interaction.InteractingDrugName.Equals(currentMed, StringComparison.OrdinalIgnoreCase) ||
                    interaction.InteractingDrugId.Equals(currentMed, StringComparison.OrdinalIgnoreCase))
                {
                    report.DrugInteractions.Add(new InteractionAlert
                    {
                        Level = interaction.Severity == InteractionSeverity.Major ? AlertLevel.Critical :
                                interaction.Severity == InteractionSeverity.Moderate ? AlertLevel.High : AlertLevel.Moderate,
                        CurrentDrug = currentMed,
                        ProposedDrug = drug.GenericName,
                        Effect = interaction.Effect,
                        Mechanism = interaction.Mechanism,
                        Management = interaction.ClinicalManagement
                    });

                    // Major interactions → must avoid
                    if (interaction.Severity == InteractionSeverity.Major)
                    {
                        report.MustAvoidReasons.Add(new SafetyAlert
                        {
                            Level = AlertLevel.Critical,
                            Category = "MAJOR Drug Interaction",
                            Message = $"MAJOR INTERACTION: {drug.GenericName} + {currentMed} → {interaction.Effect}. {interaction.ClinicalManagement}",
                            Source = "Drug Interaction Database"
                        });
                    }
                }
            }

            // Also check reverse: does the current med's interaction list include the proposed drug?
            var currentDrugInfo = MedicationKnowledgeBase.FindDrug(currentMed);
            if (currentDrugInfo != null)
            {
                foreach (var interaction in currentDrugInfo.Interactions)
                {
                    if ((interaction.InteractingDrugName.Equals(drug.GenericName, StringComparison.OrdinalIgnoreCase) ||
                         interaction.InteractingDrugId.Equals(drug.DrugId, StringComparison.OrdinalIgnoreCase)) &&
                        !report.DrugInteractions.Any(di =>
                            di.CurrentDrug.Equals(currentMed, StringComparison.OrdinalIgnoreCase) &&
                            di.ProposedDrug.Equals(drug.GenericName, StringComparison.OrdinalIgnoreCase)))
                    {
                        report.DrugInteractions.Add(new InteractionAlert
                        {
                            Level = interaction.Severity == InteractionSeverity.Major ? AlertLevel.Critical :
                                    interaction.Severity == InteractionSeverity.Moderate ? AlertLevel.High : AlertLevel.Moderate,
                            CurrentDrug = currentMed,
                            ProposedDrug = drug.GenericName,
                            Effect = interaction.Effect,
                            Mechanism = interaction.Mechanism,
                            Management = interaction.ClinicalManagement
                        });

                        if (interaction.Severity == InteractionSeverity.Major)
                        {
                            report.MustAvoidReasons.Add(new SafetyAlert
                            {
                                Level = AlertLevel.Critical,
                                Category = "MAJOR Drug Interaction (Reverse)",
                                Message = $"MAJOR INTERACTION: {currentMed} + {drug.GenericName} → {interaction.Effect}. {interaction.ClinicalManagement}",
                                Source = "Drug Interaction Database"
                            });
                        }
                    }
                }
            }
        }
    }

    private void CheckPregnancyBreastfeeding(PatientProfile patient, Drug drug, DrugSafetyReport report)
    {
        if (patient.IsPregnant)
        {
            var pregnancyCI = drug.Contraindications.FirstOrDefault(c =>
                ConditionMatches("Pregnancy", c.Condition));

            if (pregnancyCI != null)
            {
                report.MustAvoidReasons.Add(new SafetyAlert
                {
                    Level = AlertLevel.Critical,
                    Category = "PREGNANCY CONTRAINDICATION",
                    Message = $"MUST AVOID in pregnancy: {drug.GenericName} – {pregnancyCI.Description}",
                    Source = pregnancyCI.Source
                });
            }
            else
            {
                report.Warnings.Add(new SafetyAlert
                {
                    Level = AlertLevel.High,
                    Category = "Pregnancy Warning",
                    Message = $"Patient is pregnant. Verify {drug.GenericName} pregnancy safety category before prescribing.",
                    Source = "Clinical"
                });
            }
        }

        if (patient.IsBreastfeeding)
        {
            report.Warnings.Add(new SafetyAlert
            {
                Level = AlertLevel.High,
                Category = "Breastfeeding Warning",
                Message = $"Patient is breastfeeding. Verify that {drug.GenericName} is safe during lactation before prescribing.",
                Source = "Clinical"
            });
        }
    }

    private void CheckAgeWarnings(PatientProfile patient, Drug drug, DrugSafetyReport report)
    {
        if (patient.Age.HasValue)
        {
            if (patient.Age < 18)
            {
                // Check for pediatric-specific warnings
                if (drug.DrugId == "aspirin")
                {
                    report.MustAvoidReasons.Add(new SafetyAlert
                    {
                        Level = AlertLevel.Critical,
                        Category = "PEDIATRIC CONTRAINDICATION",
                        Message = "MUST AVOID: Aspirin in children <18 with viral illness – risk of Reye's Syndrome.",
                        Source = "FDA Label"
                    });
                }

                if (drug.Category == "Antidepressant")
                {
                    report.BlackBoxWarnings.Add(new SafetyAlert
                    {
                        Level = AlertLevel.Critical,
                        Category = "PEDIATRIC BLACK BOX",
                        Message = $"Antidepressants increase suicidality risk in pediatric patients. Close monitoring required for {drug.GenericName}.",
                        Source = "FDA Black Box Warning"
                    });
                }

                if (drug.DrugClass.Contains("Fluoroquinolone", StringComparison.OrdinalIgnoreCase))
                {
                    report.Warnings.Add(new SafetyAlert
                    {
                        Level = AlertLevel.High,
                        Category = "Pediatric Warning",
                        Message = $"Fluoroquinolones ({drug.GenericName}) may cause musculoskeletal harm in pediatric patients. Reserve for infections without alternative.",
                        Source = "FDA Label"
                    });
                }
            }

            if (patient.Age >= 65)
            {
                var elderlyWarnings = drug.UseWithCaution
                    .Where(c => c.Contains("elderly", StringComparison.OrdinalIgnoreCase) ||
                                c.Contains("older", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (elderlyWarnings.Any())
                {
                    foreach (var ew in elderlyWarnings)
                    {
                        report.UseWithCaution.Add(new SafetyAlert
                        {
                            Level = AlertLevel.Moderate,
                            Category = "ELDERLY - Age-Specific Caution",
                            Message = $"Patient is {patient.Age} years old. {drug.GenericName}: {ew}",
                            Source = "Geriatric Safety"
                        });
                    }
                }

                // Beers Criteria style check for high-risk elderly meds
                if (drug.DrugClass.Contains("Benzodiazepine", StringComparison.OrdinalIgnoreCase) ||
                    drug.DrugId == "metoclopramide")
                {
                    report.Warnings.Add(new SafetyAlert
                    {
                        Level = AlertLevel.High,
                        Category = "BEERS CRITERIA - Potentially Inappropriate",
                        Message = $"{drug.GenericName} is potentially inappropriate for elderly patients (age {patient.Age}) per Beers Criteria.",
                        Source = "AGS Beers Criteria"
                    });
                }
            }
        }
    }

    private SafetyVerdict DetermineVerdict(DrugSafetyReport report)
    {
        if (report.MustAvoidReasons.Count > 0)
            return SafetyVerdict.DoNotUse;

        if (report.BlackBoxWarnings.Any(b => b.Level == AlertLevel.Critical) ||
            report.DrugInteractions.Any(i => i.Level == AlertLevel.Critical))
            return SafetyVerdict.UseWithExtremeCaution;

        if (report.Warnings.Any(w => w.Level >= AlertLevel.High) ||
            report.UseWithCaution.Any(u => u.Level >= AlertLevel.Moderate) ||
            report.AllergyAlerts.Count > 0)
            return SafetyVerdict.UseWithCaution;

        return SafetyVerdict.GenerallyAcceptable;
    }

    /// <summary>
    /// Check if a patient condition matches a contraindication condition,
    /// including synonyms and partial matches.
    /// </summary>
    private bool ConditionMatches(string patientCondition, string drugCondition)
    {
        if (patientCondition.Equals(drugCondition, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check synonyms
        foreach (var (key, synonyms) in MedicationKnowledgeBase.ConditionSynonyms)
        {
            bool patientMatches = key.Equals(patientCondition, StringComparison.OrdinalIgnoreCase) ||
                                  synonyms.Any(s => s.Equals(patientCondition, StringComparison.OrdinalIgnoreCase));
            bool drugMatches = key.Equals(drugCondition, StringComparison.OrdinalIgnoreCase) ||
                               synonyms.Any(s => s.Equals(drugCondition, StringComparison.OrdinalIgnoreCase));

            if (patientMatches && drugMatches)
                return true;
        }

        // Partial / contains match (case-insensitive)
        if (patientCondition.Contains(drugCondition, StringComparison.OrdinalIgnoreCase) ||
            drugCondition.Contains(patientCondition, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Check if any synonyms of a condition appear in a text (warning, black box, etc.)
    /// </summary>
    private bool ConditionSynonymsMatch(string condition, string text)
    {
        foreach (var (key, synonyms) in MedicationKnowledgeBase.ConditionSynonyms)
        {
            bool conditionInGroup = key.Equals(condition, StringComparison.OrdinalIgnoreCase) ||
                                    synonyms.Any(s => s.Equals(condition, StringComparison.OrdinalIgnoreCase));
            if (conditionInGroup)
            {
                if (text.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                    synonyms.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        return false;
    }
}
