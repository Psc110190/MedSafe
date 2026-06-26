using Microsoft.AspNetCore.Mvc;
using MedSafety.API.Models;
using MedSafety.API.Services;

namespace MedSafety.API.Controllers;

/// <summary>
/// Main endpoint for medication safety screening.
/// Submit a patient profile and get a comprehensive safety report.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SafetyScreeningController : ControllerBase
{
    private readonly MedicationSafetyService _safetyService;
    private readonly ContextualAlertFilterService _contextualService;
    private readonly PatientContextSafetyRuleService _patientContextRuleService;

    public SafetyScreeningController(
        MedicationSafetyService safetyService,
        ContextualAlertFilterService contextualService,
        PatientContextSafetyRuleService patientContextRuleService)
    {
        _safetyService = safetyService;
        _contextualService = contextualService;
        _patientContextRuleService = patientContextRuleService;
    }

    /// <summary>
    /// Screen proposed medications against a patient's profile.
    /// Returns contraindications, black box warnings, drug interactions, allergy alerts, and more.
    /// </summary>
    /// <param name="patient">The patient profile including allergies, comorbidities, current and proposed medications.</param>
    /// <returns>A comprehensive safety screening result.</returns>
    /// <response code="200">Safety screening completed successfully.</response>
    /// <response code="400">Invalid patient profile submitted.</response>
    [HttpPost("screen")]
    [ProducesResponseType(typeof(SafetyScreeningResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SafetyScreeningResult>> ScreenMedications([FromBody] PatientProfile patient)
    {
        if (patient == null)
            return BadRequest("Patient profile is required.");

        if (patient.ProposedMedications == null || patient.ProposedMedications.Count == 0)
            return BadRequest("At least one proposed medication is required.");

        var result = await _safetyService.ScreenMedicationsAsync(patient);
        return Ok(result);
    }

    /// <summary>
    /// Quick safety check for a single drug against a condition.
    /// </summary>
    [HttpGet("quick-check")]
    [ProducesResponseType(typeof(SafetyScreeningResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<SafetyScreeningResult>> QuickCheck(
        [FromQuery] string drug,
        [FromQuery] string? condition = null,
        [FromQuery] string? allergy = null,
        [FromQuery] string? currentMedication = null)
    {
        if (string.IsNullOrWhiteSpace(drug))
            return BadRequest("Drug name is required.");

        var patient = new PatientProfile
        {
            ProposedMedications = new() { drug },
            Comorbidities = string.IsNullOrWhiteSpace(condition) ? new() : new() { condition },
            Allergies = string.IsNullOrWhiteSpace(allergy) ? new() : new() { allergy },
            CurrentMedications = string.IsNullOrWhiteSpace(currentMedication) ? new() : new() { currentMedication }
        };

        var result = await _safetyService.ScreenMedicationsAsync(patient);
        return Ok(result);
    }

    /// <summary>
    /// Context-aware safety screening.
    /// Runs the full safety screen internally, then scores and filters each alert
    /// for relevance against the patient's specific allergies, comorbidities,
    /// age group, pregnancy status, and current medications.
    /// Each alert is returned with a 0–100 relevance score and the context tokens
    /// that drove the score, helping clinicians focus on what matters most.
    /// </summary>
    /// <param name="request">
    /// Wraps the PatientProfile with optional filter settings:
    /// - <c>minimumAlertLevel</c>: suppress alerts below this severity (0=Critical … 3=Low)
    /// - <c>strictContextFilter</c>: when true, only return alerts that directly match patient context
    /// </param>
    /// <returns>A context-filtered and relevance-scored safety result.</returns>
    /// <response code="200">Contextual screening completed successfully.</response>
    /// <response code="400">Invalid request body.</response>
    [HttpPost("contextual-screen")]
    [ProducesResponseType(typeof(ContextualScreeningResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ContextualScreeningResult>> ContextualScreen(
        [FromBody] ContextualScreeningRequest request)
    {
        if (request?.Patient == null)
            return BadRequest("Request body with a valid patient profile is required.");

        if (request.Patient.ProposedMedications == null || request.Patient.ProposedMedications.Count == 0)
            return BadRequest("At least one proposed medication is required.");

        var result = await _contextualService.ScreenWithContextAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Patient-context safety categorization seeded from curated disease-medication knowledge.
    /// Returns warnings, alerts, safe medication candidates, must-avoid medications,
    /// use-with-caution medications, and missing context needed for classification.
    /// Proposed medications are optional; when omitted, candidates are inferred from the
    /// patient's comorbidities and current complaints.
    /// </summary>
    [HttpPost("patient-context")]
    [ProducesResponseType(typeof(PatientContextSafetyResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PatientContextSafetyResult>> PatientContextSafety([FromBody] PatientProfile patient)
    {
        if (patient == null)
            return BadRequest("Patient profile is required.");

        patient.Allergies = CleanList(patient.Allergies);
        patient.Comorbidities = CleanList(patient.Comorbidities);
        patient.CurrentComplaints = CleanList(patient.CurrentComplaints);
        patient.CurrentMedications = CleanList(patient.CurrentMedications);
        patient.ProposedMedications = CleanList(patient.ProposedMedications);
        patient.Symptoms = CleanList(patient.Symptoms);

        if (patient.Comorbidities.Count == 0 &&
            patient.CurrentComplaints.Count == 0 &&
            patient.CurrentMedications.Count == 0 &&
            patient.ProposedMedications.Count == 0)
        {
            return BadRequest("Provide at least one condition, current medication, or proposed medication.");
        }

        return Ok(await _patientContextRuleService.ScreenAsync(patient));
    }

    /// <summary>
    /// Options used by the patient-context UI for condition and medication suggestions.
    /// </summary>
    [HttpGet("patient-context/options")]
    public ActionResult<object> PatientContextOptions()
    {
        return Ok(new
        {
            conditionMedicationMap = _patientContextRuleService.GetPatientContextOptions(),
            symptomOptions = new[]
            {
                "hypoglycemia", "dizziness", "vomiting", "abdominal pain", "fever",
                "sore throat", "dyspnea", "UTI symptoms", "infection"
            },
            riskFlagOptions = new[]
            {
                "NPO", "poor intake", "acute illness", "recent surgery", "dehydration",
                "metabolic acidosis", "DKA", "acute kidney injury", "sepsis", "hypoxia",
                "shock", "anuria", "bowel obstruction", "urinary obstruction", "asthma/COPD",
                "heart block", "decompensated heart failure", "cardiac disease",
                "recent MI or unstable angina", "adrenal insufficiency",
                "medullary thyroid carcinoma history", "MEN2 history", "pancreatitis history",
                "ACE/ARB angioedema history", "neutropenia", "gout", "digoxin use",
                "potassium supplement", "potassium-sparing diuretic", "aliskiren use",
                "ACE inhibitor within 36 hours", "heavy alcohol use", "recent reduced insulin"
            }
        });
    }

    private static List<string> CleanList(List<string>? values)
    {
        return (values ?? new List<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
