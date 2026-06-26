using MedSafety.API.Data;
using MedSafety.API.Models;

namespace MedSafety.API.Services;

/// <summary>
/// Prototype rule engine seeded from the hackathon disease-medication spreadsheet.
/// It evaluates structured patient context and returns UI-ready categories.
/// </summary>
public class PatientContextSafetyRuleService
{
    private const string DailyMed = "DailyMed label reference";
    private const string FdaSglt2 = "FDA SGLT2 inhibitor safety information";
    private const string Mgfa = "Myasthenia Gravis Foundation of America cautionary drugs";

    private static readonly List<MedicationCandidate> MedicationMap = BuildMedicationMap();

    private readonly MedicationSafetyService _medicationSafetyService;
    private readonly CustomSafetyRuleService _customSafetyRuleService;

    public PatientContextSafetyRuleService(
        MedicationSafetyService medicationSafetyService,
        CustomSafetyRuleService customSafetyRuleService)
    {
        _medicationSafetyService = medicationSafetyService;
        _customSafetyRuleService = customSafetyRuleService;
    }

    public PatientContextSafetyResult Screen(PatientProfile patient) =>
        ScreenInternalAsync(patient, includeExternalEvidence: false).GetAwaiter().GetResult();

    public Task<PatientContextSafetyResult> ScreenAsync(PatientProfile patient) =>
        ScreenInternalAsync(patient, includeExternalEvidence: true);

    private async Task<PatientContextSafetyResult> ScreenInternalAsync(
        PatientProfile patient,
        bool includeExternalEvidence)
    {
        var context = BuildContext(patient);
        var targetMeds = GetTargetMedications(context);
        var alerts = new List<PatientMedicationAlert>();

        foreach (var med in targetMeds)
        {
            EvaluateMedication(context, med, alerts);
        }

        alerts.AddRange(ConvertFullSafetyAlerts(context, targetMeds, RunFullSafetyScreen(patient, context, targetMeds)));

        SafetyScreeningResult? externalSafetyResult = null;
        if (includeExternalEvidence)
        {
            externalSafetyResult = await RunExternalSafetyScreenAsync(patient, context);
            alerts.AddRange(ConvertFullSafetyAlerts(context, targetMeds, externalSafetyResult));
        }

        var externallyResolvedMedicationKeys = BuildExternallyResolvedMedicationKeys(externalSafetyResult);
        var unrecognizedMedications = context.UnrecognizedMedications
            .Where(item => !IsExternallyResolved(item, externallyResolvedMedicationKeys))
            .ToList();

        alerts.AddRange(EvaluateCustomRules(context, targetMeds));
        alerts.AddRange(unrecognizedMedications.Select(BuildUnrecognizedAlert));
        alerts = DeduplicateAlerts(alerts);

        var missingContext = BuildMissingContext(context, targetMeds);
        var criticalByMed = alerts
            .Where(a => a.Level == AlertLevel.Critical)
            .GroupBy(a => a.RxCui)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var cautionByMed = alerts
            .Where(a => a.Level != AlertLevel.Critical)
            .GroupBy(a => a.RxCui)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var missingByMed = missingContext
            .GroupBy(m => m.RxCui)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var result = new PatientContextSafetyResult
        {
            PatientId = patient.PatientId,
            ScreenedAt = DateTime.UtcNow,
            PatientContext = new PatientContextSnapshot
            {
                Conditions = context.ConditionLabels,
                CurrentMedications = patient.CurrentMedications,
                ProposedMedications = patient.ProposedMedications,
                Symptoms = patient.Symptoms,
                ActiveRiskFlags = context.ActiveRiskFlags,
                Labs = patient.Labs,
                Vitals = patient.Vitals,
                IsPregnant = patient.IsPregnant,
                IsBreastfeeding = patient.IsBreastfeeding,
                Age = patient.Age
            },
            Alerts = alerts
                .OrderBy(a => a.Level)
                .ThenBy(a => a.MedicationName)
                .ToList(),
            MissingContext = missingContext,
            UnrecognizedMedications = unrecognizedMedications,
            DataSources = BuildDataSources(externalSafetyResult)
        };

        result.MustAvoidMedications = criticalByMed
            .Select(kvp => BuildClassification(kvp.Value.First(), kvp.Value, "must_avoid"))
            .OrderBy(m => m.MedicationName)
            .ToList();

        result.UseWithCautionMedications = cautionByMed
            .Where(kvp => !criticalByMed.ContainsKey(kvp.Key))
            .Where(kvp => !kvp.Key.StartsWith("unknown:", StringComparison.OrdinalIgnoreCase))
            .Select(kvp => BuildClassification(kvp.Value.First(), kvp.Value, "use_with_caution"))
            .Concat(BuildMissingContextClassifications(targetMeds, missingByMed, criticalByMed, cautionByMed))
            .OrderBy(m => m.MedicationName)
            .ToList();

        result.SafeMedications = targetMeds
            .Where(m => !criticalByMed.ContainsKey(m.RxCui))
            .Where(m => !cautionByMed.ContainsKey(m.RxCui))
            .Where(m => !missingByMed.ContainsKey(m.RxCui))
            .Select(m => new MedicationClassification
            {
                MedicationName = m.Name,
                RxCui = m.RxCui,
                DrugClass = m.DrugClass,
                ConditionName = m.ConditionName,
                Severity = "candidate",
                SafetyLabel = "No configured rule triggered in the available knowledge base for the provided patient context; this is not a proof of safety."
            })
            .OrderBy(m => m.MedicationName)
            .ToList();

        return result;
    }

    public IReadOnlyList<object> GetPatientContextOptions()
    {
        return MedicationMap
            .GroupBy(m => m.ConditionKey)
            .Select(g => new
            {
                conditionKey = g.Key,
                conditionName = g.First().ConditionName,
                medications = g.Select(m => new
                {
                    medicationName = m.Name,
                    rxCui = m.RxCui,
                    drugClass = m.DrugClass
                })
            })
            .Cast<object>()
            .ToList();
    }

    private static List<string> BuildDataSources(SafetyScreeningResult? externalSafetyResult)
    {
        var sources = new List<string>
        {
            "Curated disease-medication knowledge base",
            "MedSafety Static Knowledge Base",
            "Editable custom safety rules",
            "Patient-context rule engine"
        };

        if (externalSafetyResult != null)
        {
            sources.AddRange(externalSafetyResult.DataSources
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Where(s => !s.Equals("MedSafety Static Knowledge Base", StringComparison.OrdinalIgnoreCase)));
        }

        return sources
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PatientRuleContext BuildContext(PatientProfile patient)
    {
        var allConditionText = patient.Comorbidities
            .Concat(patient.CurrentComplaints)
            .Select(Normalize)
            .ToList();

        var conditionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in allConditionText)
        {
            if (ContainsAny(value, "type 1 diabetes", "t1dm", "type1 diabetes")) conditionKeys.Add("type_1_diabetes");
            if (ContainsAny(value, "type 2 diabetes", "t2dm", "type2 diabetes")) conditionKeys.Add("type_2_diabetes");
            if (value.Contains("diabetes") && !conditionKeys.Contains("type_1_diabetes")) conditionKeys.Add("type_2_diabetes");
            if (ContainsAny(value, "myasthenia", "myasthenia gravis")) conditionKeys.Add("myasthenia_gravis");
            if (ContainsAny(value, "hypertension", "high blood pressure", "htn")) conditionKeys.Add("hypertension");
            if (ContainsAny(value, "heart failure", "chf", "cardiac failure", "hf")) conditionKeys.Add("heart_failure");
            if (IsHeartAttackOrAcs(value)) conditionKeys.Add("acute_coronary_syndrome");
            if (ContainsAny(value, "hypothyroid", "hypothyroidism", "underactive thyroid")) conditionKeys.Add("hypothyroidism");
            if (ContainsAny(value, "hyperthyroid", "hyperthyroidism", "overactive thyroid")) conditionKeys.Add("hyperthyroidism");
            if (ContainsAny(value, "bradycardia", "heart block")) conditionKeys.Add("bradycardia");
            if (ContainsAny(value, "asthma", "copd", "bronchospasm")) conditionKeys.Add("bronchospasm");
        }

        var currentMedicationCandidates = patient.CurrentMedications
            .Select(value => ResolveMedicationCandidate(value, "Current medication"))
            .Where(m => m != null)
            .Select(m => m!)
            .ToList();

        var proposedMedicationCandidates = patient.ProposedMedications
            .Select(value => ResolveMedicationCandidate(value, "User-proposed medication"))
            .Where(m => m != null)
            .Select(m => m!)
            .ToList();

        var currentRxcuis = currentMedicationCandidates
            .Select(m => m.RxCui)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var proposedRxcuis = proposedMedicationCandidates
            .Select(m => m.RxCui)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new PatientRuleContext
        {
            Patient = patient,
            ConditionKeys = conditionKeys,
            CurrentRxCuis = currentRxcuis,
            ProposedRxCuis = proposedRxcuis,
            CurrentMedicationCandidates = currentMedicationCandidates,
            ProposedMedicationCandidates = proposedMedicationCandidates,
            UnrecognizedMedications = BuildUnrecognizedMedicationItems(patient),
            ConditionLabels = MedicationMap
                .Where(m => conditionKeys.Contains(m.ConditionKey))
                .Select(m => m.ConditionName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ActiveRiskFlags = GetActiveRiskFlags(patient).ToList()
        };
    }

    private static List<MedicationCandidate> GetTargetMedications(PatientRuleContext context)
    {
        var hasExplicitMedicationInput =
            context.Patient.CurrentMedications.Any(m => !string.IsNullOrWhiteSpace(m)) ||
            context.Patient.ProposedMedications.Any(m => !string.IsNullOrWhiteSpace(m));

        var meds = hasExplicitMedicationInput
            ? new List<MedicationCandidate>()
            : MedicationMap
                .Where(m => context.ConditionKeys.Contains(m.ConditionKey))
                .ToList();

        foreach (var med in context.CurrentMedicationCandidates.Concat(context.ProposedMedicationCandidates))
        {
            if (!meds.Any(m => Normalize(m.Name).Equals(Normalize(med.Name), StringComparison.OrdinalIgnoreCase)))
                meds.Add(med);
        }

        return meds
            .GroupBy(m => Normalize(m.Name), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static void EvaluateMedication(PatientRuleContext ctx, MedicationCandidate med, List<PatientMedicationAlert> alerts)
    {
        var p = ctx.Patient;

        switch (med.RxCui)
        {
            case "274783":
                AddIf(alerts, ctx, med, "RULE-001", AlertLevel.Critical, "Hypoglycemia Risk",
                    HasLowGlucose(p) || HasSymptom(p, "hypoglycemia"),
                    "Basal insulin can worsen active or imminent hypoglycemia.",
                    "Review current glucose, recent doses, meal intake, and rescue treatment.",
                    DailyMed);
                break;

            case "86009":
                AddIf(alerts, ctx, med, "RULE-002", AlertLevel.High, "Meal-Time Insulin Risk",
                    HasLowGlucose(p) || p.ContextFlags.Npo || p.ContextFlags.PoorIntake || HasSymptom(p, "vomiting"),
                    "Rapid-acting insulin has high hypoglycemia risk when food intake is absent or uncertain.",
                    "Confirm meal/carbohydrate intake and dose timing before administration.",
                    DailyMed);
                break;

            case "6809":
                AddIf(alerts, ctx, med, "RULE-003", AlertLevel.Critical, "Renal Contraindication",
                    p.Labs.EGfr < 30,
                    "Metformin is generally contraindicated in severe renal impairment because of lactic acidosis risk.",
                    "Do not start or continue without clinician review; consider alternative therapy.",
                    DailyMed);
                AddIf(alerts, ctx, med, "RULE-004", AlertLevel.Critical, "Acidosis / Acute Illness",
                    p.ContextFlags.MetabolicAcidosis || p.ContextFlags.Dka || p.ContextFlags.Sepsis ||
                    p.ContextFlags.Hypoxia || p.ContextFlags.Shock || p.ContextFlags.AcuteKidneyInjury,
                    "Metformin should be reviewed during metabolic acidosis or acute conditions that increase lactic acidosis risk.",
                    "Hold and evaluate acid-base status and renal function per protocol.",
                    DailyMed);
                break;

            case "4821":
                AddIf(alerts, ctx, med, "RULE-005", AlertLevel.High, "Hypoglycemia Risk",
                    HasLowGlucose(p) || (p.Age >= 65 && (p.ContextFlags.PoorIntake || p.ContextFlags.AcuteKidneyInjury)) ||
                    ctx.CurrentRxCuis.Contains("274783") || ctx.CurrentRxCuis.Contains("86009") || p.Labs.EGfr < 45,
                    "Glipizide can cause hypoglycemia, especially with poor intake, insulin, older age, or organ impairment.",
                    "Review dose, glucose monitoring plan, and duplicate hypoglycemia risk.",
                    DailyMed);
                break;

            case "593411":
                AddIf(alerts, ctx, med, "RULE-006", AlertLevel.Moderate, "Renal Dose Review",
                    p.Labs.EGfr < 45,
                    "Sitagliptin dosing should be checked against renal function.",
                    "Calculate renal dose and route to pharmacist if dose does not match kidney function.",
                    DailyMed);
                break;

            case "1991302":
                AddIf(alerts, ctx, med, "RULE-007", AlertLevel.Critical, "Thyroid Cancer Contraindication",
                    p.ContextFlags.ThyroidCancerHistory || p.ContextFlags.Men2History,
                    "Semaglutide products carry a contraindication for personal/family history of medullary thyroid carcinoma or MEN2.",
                    "Block order pending clinician/pharmacist review if this history is present.",
                    DailyMed);
                AddIf(alerts, ctx, med, "RULE-008", AlertLevel.High, "Pancreatitis Warning",
                    p.ContextFlags.PancreatitisHistory || HasSymptom(p, "abdominal pain"),
                    "Review semaglutide when pancreatitis history or concerning symptoms are present.",
                    "Assess symptoms and consider holding or alternative therapy per label/protocol.",
                    DailyMed);
                break;

            case "1545653":
                AddIf(alerts, ctx, med, "RULE-009", AlertLevel.High, "Type 1 Diabetes SGLT2 Risk",
                    ctx.ConditionKeys.Contains("type_1_diabetes"),
                    "FDA notes SGLT2 inhibitor safety and efficacy have not been established in type 1 diabetes and they are not FDA-approved for type 1 diabetes.",
                    "Require clinician/pharmacist review and consider ketoacidosis risk.",
                    FdaSglt2);
                AddIf(alerts, ctx, med, "RULE-010", AlertLevel.High, "SGLT2 Ketoacidosis Risk",
                    p.ContextFlags.Dka || p.ContextFlags.MetabolicAcidosis || p.ContextFlags.RecentSurgery ||
                    p.ContextFlags.Npo || p.ContextFlags.PoorIntake || p.ContextFlags.AcuteIllness ||
                    p.ContextFlags.Dehydration || p.ContextFlags.HeavyAlcoholUse || p.ContextFlags.ReducedInsulin ||
                    HasAnySymptom(p, "abdominal pain", "vomiting", "dyspnea"),
                    "SGLT2 inhibitors have ketoacidosis warnings; ketoacidosis can occur even without marked hyperglycemia.",
                    "Evaluate ketones/anion gap, hold during high-risk illness or perioperative fasting per protocol, and escalate if symptomatic.",
                    FdaSglt2);
                break;

            case "1656339":
                AddIf(alerts, ctx, med, "RULE-011", AlertLevel.Critical, "ARNI + ACE Inhibitor",
                    ctx.CurrentRxCuis.Contains("29046") || p.ContextFlags.LastAceInhibitorWithin36Hours,
                    "Sacubitril/valsartan should not be used with an ACE inhibitor; a 36-hour washout is required when switching.",
                    "Block duplicate therapy until ACE inhibitor washout is verified.",
                    DailyMed);
                AddIf(alerts, ctx, med, "RULE-012", AlertLevel.Critical, "Fetal Toxicity",
                    p.IsPregnant,
                    "Drugs acting on the renin-angiotensin system can cause fetal injury or death; pregnancy requires immediate review.",
                    "Stop/avoid and contact prescriber for pregnancy-safe alternative.",
                    DailyMed);
                AddIf(alerts, ctx, med, "RULE-013", AlertLevel.Critical, "Angioedema History",
                    p.ContextFlags.AngioedemaAceArbHistory,
                    "Sacubitril/valsartan is contraindicated in patients with a history of angioedema related to ACE inhibitor or ARB therapy.",
                    "Avoid and route to clinician/pharmacist for alternative.",
                    DailyMed);
                AddIf(alerts, ctx, med, "RULE-014", AlertLevel.Critical, "Aliskiren + Diabetes",
                    (p.ContextFlags.AliskirenUse || HasCurrentMedication(p, "aliskiren")) &&
                    (ctx.ConditionKeys.Contains("type_1_diabetes") || ctx.ConditionKeys.Contains("type_2_diabetes")),
                    "RAAS combination with aliskiren in diabetes is a contraindication/warning context in labeling.",
                    "Block or route for specialist review.",
                    DailyMed);
                break;

            case "29046":
                AddIf(alerts, ctx, med, "RULE-015", AlertLevel.Critical, "ACE Inhibitor Pregnancy Risk",
                    p.IsPregnant,
                    "ACE inhibitors can cause fetal injury or death when used during pregnancy.",
                    "Avoid/stop and contact prescriber for pregnancy-safe alternative.",
                    DailyMed);
                AddIf(alerts, ctx, med, "RULE-016", AlertLevel.High, "Potassium / Renal Monitoring",
                    p.Labs.Potassium > 5 || p.Labs.EGfr < 30 || ctx.CurrentRxCuis.Contains("1656339") ||
                    p.ContextFlags.PotassiumSupplement || p.ContextFlags.PotassiumSparingDiuretic || p.ContextFlags.AliskirenUse,
                    "Lisinopril can increase potassium and affect renal function, especially with other RAAS or potassium-raising therapies.",
                    "Check potassium and renal function; avoid unsafe duplicate RAAS therapy.",
                    DailyMed);
                break;

            case "5487":
                AddIf(alerts, ctx, med, "RULE-017", AlertLevel.Critical, "Thiazide With Anuria",
                    p.ContextFlags.Anuria,
                    "Hydrochlorothiazide-containing products are contraindicated in anuria.",
                    "Avoid and review volume/renal plan.",
                    DailyMed);
                AddDiureticRule(alerts, ctx, med);
                break;

            case "4603":
                AddIf(alerts, ctx, med, "RULE-018", AlertLevel.High, "Loop Diuretic Renal / Electrolyte Safety",
                    p.ContextFlags.Anuria || p.ContextFlags.Dehydration || p.Vitals.SystolicBp < 90 ||
                    p.Labs.Potassium < 3.5m || p.Labs.Sodium < 135 || p.ContextFlags.AcuteKidneyInjury,
                    "Furosemide requires monitoring for volume depletion, electrolyte abnormalities, renal effects, and anuria.",
                    "Check electrolytes, renal function, blood pressure, and volume status before administration/escalation.",
                    DailyMed);
                AddDiureticRule(alerts, ctx, med);
                break;

            case "17767":
                AddIf(alerts, ctx, med, "RULE-019", AlertLevel.High, "Hypotension Context",
                    p.Vitals.SystolicBp < 90 || p.ContextFlags.Shock || HasSymptom(p, "dizziness"),
                    "Amlodipine can worsen hypotension in vulnerable patients or when combined with other blood-pressure-lowering medicines.",
                    "Review blood pressure trend and hold parameters.",
                    DailyMed);
                break;

            case "6918":
                AddBetaBlockerCriticalRule(alerts, ctx, med, "RULE-020", "Metoprolol requires screening for bradycardia, heart block, shock, decompensated HF, and bronchospastic disease context.");
                AddMyastheniaBetaBlockerRule(alerts, ctx, med);
                break;

            case "8787":
                AddBetaBlockerCriticalRule(alerts, ctx, med, "RULE-021", "Propranolol requires screening for bradycardia, heart block, shock, decompensated HF, bronchospasm, and hypoglycemia masking risk.", includeGlucose: true);
                AddMyastheniaBetaBlockerRule(alerts, ctx, med);
                break;

            case "9000":
                AddIf(alerts, ctx, med, "RULE-023", AlertLevel.Critical, "Pyridostigmine Obstruction Screen",
                    p.ContextFlags.BowelObstruction || p.ContextFlags.UrinaryObstruction,
                    "Pyridostigmine should be avoided when mechanical intestinal or urinary obstruction is present.",
                    "Block and notify prescriber/pharmacist for alternative management.",
                    DailyMed);
                break;

            case "10582":
            case "10814":
                AddIf(alerts, ctx, med, "RULE-024", AlertLevel.Critical, "Thyroid Hormone + Adrenal Insufficiency",
                    p.ContextFlags.AdrenalInsufficiency,
                    "Thyroid hormone therapy can precipitate adrenal crisis if adrenal insufficiency is uncorrected.",
                    "Do not start/escalate until adrenal status is addressed by clinician.",
                    DailyMed);
                AddIf(alerts, ctx, med, "RULE-025", AlertLevel.High, "Thyroid Hormone Cardiac Risk",
                    p.Age >= 65 || p.ContextFlags.CardiacDisease || p.ContextFlags.RecentMiOrUnstableAngina ||
                    HasHeartAttackOrAcsComplaint(p) || p.Vitals.HeartRate > 110,
                    "Thyroid hormone can increase cardiac workload and should be titrated carefully in cardiac disease.",
                    "Check dose, TSH/free T4/T3 context, and cardiac risk before initiation/escalation.",
                    DailyMed);
                break;

            case "1191":
                AddIf(alerts, ctx, med, "RULE-029", AlertLevel.Critical, "Aspirin Allergy",
                    HasAllergy(p, "aspirin", "nsaid", "salicylate"),
                    "Aspirin should be avoided when aspirin, NSAID, or salicylate allergy is documented.",
                    "Block order and route to clinician/pharmacist for antiplatelet alternative review.",
                    DailyMed);
                AddIf(alerts, ctx, med, "RULE-030", AlertLevel.High, "Aspirin Respiratory / Bleeding Caution",
                    p.ContextFlags.AsthmaCopd || ctx.ConditionKeys.Contains("bronchospasm") ||
                    p.ContextFlags.Gout || p.ContextFlags.RecentSurgery,
                    "Aspirin can worsen aspirin-sensitive asthma, increase bleeding risk around surgery, and trigger gout flares.",
                    "Confirm indication, allergy/asthma history, bleeding risk, and gastroprotection plan.",
                    DailyMed);
                break;

            case "32968":
                AddIf(alerts, ctx, med, "RULE-031", AlertLevel.High, "Clopidogrel Bleeding / Interaction Caution",
                    p.ContextFlags.RecentSurgery || HasCurrentMedication(p, "omeprazole"),
                    "Clopidogrel increases bleeding risk around procedures and omeprazole can reduce antiplatelet effect.",
                    "Review procedure timing, bleeding risk, and avoid omeprazole when possible.",
                    DailyMed);
                break;

            case "83367":
                AddIf(alerts, ctx, med, "RULE-032", AlertLevel.Critical, "Statin Pregnancy Review",
                    p.IsPregnant,
                    "Atorvastatin requires pregnancy-specific clinician review because lipid-lowering therapy may be inappropriate or harmful depending on context.",
                    "Hold or route to clinician/pharmacist for pregnancy-specific risk-benefit review.",
                    DailyMed);
                AddIf(alerts, ctx, med, "RULE-033", AlertLevel.High, "Statin Liver / Myopathy Caution",
                    p.ContextFlags.HeavyAlcoholUse || p.Age >= 75,
                    "Atorvastatin needs extra review when liver risk or older age increases myopathy/hepatic monitoring concerns.",
                    "Review liver history, alcohol use, interacting medications, and monitoring plan.",
                    DailyMed);
                break;

            case "6835":
                AddIf(alerts, ctx, med, "RULE-026", AlertLevel.High, "Methimazole Agranulocytosis Warning",
                    HasAnySymptom(p, "fever", "sore throat", "infection") || p.ContextFlags.Neutropenia,
                    "Methimazole is associated with serious blood dyscrasias; fever or sore throat should prompt urgent CBC review.",
                    "Hold/escalate per protocol and obtain CBC when symptoms occur.",
                    DailyMed);
                AddIf(alerts, ctx, med, "RULE-027", AlertLevel.High, "Methimazole Pregnancy Review",
                    p.IsPregnant,
                    "Methimazole use in pregnancy requires specialist review because fetal harm risk and trimester-specific therapy choices may apply.",
                    "Route to endocrinology/obstetric clinician or pharmacist for pregnancy-specific decision.",
                    DailyMed);
                break;
        }
    }

    private static void AddBetaBlockerCriticalRule(
        List<PatientMedicationAlert> alerts,
        PatientRuleContext ctx,
        MedicationCandidate med,
        string ruleId,
        string message,
        bool includeGlucose = false)
    {
        var p = ctx.Patient;
        var triggered = p.Vitals.HeartRate < 50 || p.ContextFlags.HeartBlock || p.ContextFlags.Shock ||
                        p.ContextFlags.DecompensatedHeartFailure || p.ContextFlags.AsthmaCopd ||
                        ctx.ConditionKeys.Contains("bradycardia") || ctx.ConditionKeys.Contains("bronchospasm");

        if (includeGlucose)
            triggered = triggered || HasLowGlucose(p);

        AddIf(alerts, ctx, med, ruleId, AlertLevel.Critical, "Beta Blocker Contraindication Screen",
            triggered,
            message,
            "Hold/block pending clinician review when severe contraindication context is present.",
            DailyMed);
    }

    private static void AddMyastheniaBetaBlockerRule(List<PatientMedicationAlert> alerts, PatientRuleContext ctx, MedicationCandidate med)
    {
        AddIf(alerts, ctx, med, "RULE-022", AlertLevel.High, "Myasthenia Gravis Caution",
            ctx.ConditionKeys.Contains("myasthenia_gravis"),
            "MGFA lists beta blockers as potentially dangerous in MG and advises cautious use because they may worsen symptoms.",
            "Route to clinician/pharmacist; consider alternatives and monitor MG symptoms if therapy is necessary.",
            Mgfa);
    }

    private static void AddDiureticRule(List<PatientMedicationAlert> alerts, PatientRuleContext ctx, MedicationCandidate med)
    {
        var p = ctx.Patient;
        AddIf(alerts, ctx, med, "RULE-028", AlertLevel.High, "Diuretic Electrolyte / Volume Risk",
            p.Labs.Potassium < 3.5m || p.Labs.Sodium < 135 || p.Vitals.SystolicBp < 90 ||
            p.ContextFlags.Dehydration || p.ContextFlags.Gout || p.ContextFlags.DigoxinUse || HasCurrentMedication(p, "digoxin"),
            "Diuretics can worsen electrolyte abnormalities and volume depletion; hypokalemia may also increase digoxin toxicity risk.",
            "Check electrolytes and volume status; add hold parameters or supplementation plan when needed.",
            DailyMed);
    }

    private static void AddIf(
        List<PatientMedicationAlert> alerts,
        PatientRuleContext ctx,
        MedicationCandidate med,
        string ruleId,
        AlertLevel level,
        string category,
        bool condition,
        string message,
        string suggestedAction,
        string source)
    {
        if (!condition) return;

        alerts.Add(new PatientMedicationAlert
        {
            RuleId = ruleId,
            Level = level,
            Category = category,
            MedicationName = med.Name,
            RxCui = med.RxCui,
            DrugClass = med.DrugClass,
            ConditionName = med.ConditionName,
            Message = message,
            SuggestedAction = suggestedAction,
            Source = source,
            MatchedPatientFacts = BuildMatchedFacts(ctx)
        });
    }

    private static List<MissingContextItem> BuildMissingContext(PatientRuleContext ctx, List<MedicationCandidate> meds)
    {
        var result = new List<MissingContextItem>();
        foreach (var med in meds)
        {
            if (IsDiabetesMed(med.RxCui) && ctx.Patient.Labs.Glucose is null)
                result.Add(Missing(med, "glucose", "Glucose is needed to classify hypoglycemia risk."));

            if (IsRenalSensitive(med.RxCui) && ctx.Patient.Labs.EGfr is null)
                result.Add(Missing(med, "eGFR", "Kidney function is needed for renal contraindication or dose checks."));

            if ((IsRenalSensitive(med.RxCui) || IsDiuretic(med.RxCui)) && ctx.Patient.Labs.Potassium is null)
                result.Add(Missing(med, "potassium", "Potassium is needed for electrolyte and RAAS safety checks."));

            if (IsDiuretic(med.RxCui) && ctx.Patient.Labs.Sodium is null)
                result.Add(Missing(med, "sodium", "Sodium is needed for diuretic electrolyte checks."));

            if (IsBetaBlocker(med.RxCui) && ctx.Patient.Vitals.HeartRate is null)
                result.Add(Missing(med, "heartRate", "Heart rate is needed for bradycardia screening."));

            if ((IsBetaBlocker(med.RxCui) || med.RxCui is "17767" or "4603" or "5487") && ctx.Patient.Vitals.SystolicBp is null)
                result.Add(Missing(med, "systolicBp", "Blood pressure is needed for hypotension screening."));
        }

        return result
            .GroupBy(m => $"{m.RxCui}:{m.Field}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(m => m.MedicationName)
            .ThenBy(m => m.Field)
            .ToList();
    }

    private static IEnumerable<MedicationClassification> BuildMissingContextClassifications(
        List<MedicationCandidate> targetMeds,
        Dictionary<string, List<MissingContextItem>> missingByMed,
        Dictionary<string, List<PatientMedicationAlert>> criticalByMed,
        Dictionary<string, List<PatientMedicationAlert>> cautionByMed)
    {
        foreach (var med in targetMeds)
        {
            if (!missingByMed.TryGetValue(med.RxCui, out var missing)) continue;
            if (criticalByMed.ContainsKey(med.RxCui) || cautionByMed.ContainsKey(med.RxCui)) continue;

            yield return new MedicationClassification
            {
                MedicationName = med.Name,
                RxCui = med.RxCui,
                DrugClass = med.DrugClass,
                ConditionName = med.ConditionName,
                Severity = "needs_context",
                Reasons = new() { $"Missing: {string.Join(", ", missing.Select(m => m.Field))}" },
                SafetyLabel = "Cannot classify as safe until required patient context is supplied."
            };
        }
    }

    private static IEnumerable<MedicationClassification> BuildUnrecognizedClassifications(
        List<UnrecognizedMedicationItem> unrecognized,
        Dictionary<string, List<PatientMedicationAlert>> criticalByMed,
        Dictionary<string, List<PatientMedicationAlert>> cautionByMed)
    {
        foreach (var item in unrecognized)
        {
            var id = BuildUnrecognizedId(item.MedicationName);
            if (criticalByMed.ContainsKey(id) || cautionByMed.ContainsKey(id)) continue;

            yield return new MedicationClassification
            {
                MedicationName = item.MedicationName,
                RxCui = id,
                DrugClass = "Unrecognized",
                ConditionName = item.Source,
                Severity = "unrecognized",
                Reasons = new() { item.Reason },
                SafetyLabel = "Medication could not be verified in the configured knowledge base; do not classify as safe."
            };
        }
    }

    private static MedicationClassification BuildClassification(
        PatientMedicationAlert first,
        List<PatientMedicationAlert> alerts,
        string severity)
    {
        return new MedicationClassification
        {
            MedicationName = first.MedicationName,
            RxCui = first.RxCui,
            DrugClass = first.DrugClass,
            ConditionName = first.ConditionName,
            Severity = severity,
            Reasons = alerts.Select(a => a.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RuleIds = alerts.Select(a => a.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SafetyLabel = BuildClassificationSafetyLabel(alerts, severity)
        };
    }

    private static string BuildClassificationSafetyLabel(List<PatientMedicationAlert> alerts, string severity)
    {
        var categories = alerts
            .Select(a => a.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        var categoryText = categories.Count > 0
            ? $" ({string.Join(", ", categories)})"
            : string.Empty;

        if (severity == "must_avoid")
            return alerts.Count == 1
                ? $"Critical safety rule triggered{categoryText}."
                : $"{alerts.Count} critical safety rules triggered{categoryText}.";

        var highestLevel = alerts
            .OrderBy(a => a.Level)
            .Select(a => a.Level.ToString())
            .FirstOrDefault() ?? "Caution";

        return alerts.Count == 1
            ? $"{highestLevel} review needed{categoryText}."
            : $"{highestLevel} review needed: {alerts.Count} safety rules triggered{categoryText}.";
    }

    private SafetyScreeningResult RunFullSafetyScreen(
        PatientProfile originalPatient,
        PatientRuleContext ctx,
        List<MedicationCandidate> targetMeds)
    {
        var fullKnowledgeBaseTargets = targetMeds
            .Where(m => MedicationKnowledgeBase.FindDrug(m.Name) != null)
            .Select(m => m.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (fullKnowledgeBaseTargets.Count == 0)
            return new SafetyScreeningResult { PatientId = originalPatient.PatientId };

        var fullSafetyPatient = BuildFullSafetyPatient(originalPatient, ctx, fullKnowledgeBaseTargets);
        return _medicationSafetyService.ScreenMedications(fullSafetyPatient);
    }

    private async Task<SafetyScreeningResult> RunExternalSafetyScreenAsync(
        PatientProfile originalPatient,
        PatientRuleContext ctx)
    {
        var externalTargets = originalPatient.CurrentMedications
            .Concat(originalPatient.ProposedMedications)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (externalTargets.Count == 0)
            return new SafetyScreeningResult { PatientId = originalPatient.PatientId };

        var externalSafetyPatient = BuildFullSafetyPatient(originalPatient, ctx, externalTargets);
        return await _medicationSafetyService.ScreenMedicationsAsync(externalSafetyPatient);
    }

    private static PatientProfile BuildFullSafetyPatient(
        PatientProfile originalPatient,
        PatientRuleContext ctx,
        List<string> proposedMedications)
    {
        return new PatientProfile
        {
            PatientId = originalPatient.PatientId,
            Allergies = originalPatient.Allergies,
            Comorbidities = originalPatient.Comorbidities
                .Concat(originalPatient.CurrentComplaints)
                .Concat(originalPatient.Symptoms)
                .Concat(ctx.ConditionLabels)
                .Concat(ctx.ActiveRiskFlags)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            CurrentComplaints = originalPatient.CurrentComplaints,
            CurrentMedications = originalPatient.CurrentMedications,
            ProposedMedications = proposedMedications,
            Age = originalPatient.Age,
            IsPregnant = originalPatient.IsPregnant,
            IsBreastfeeding = originalPatient.IsBreastfeeding,
            Symptoms = originalPatient.Symptoms,
            Labs = originalPatient.Labs,
            Vitals = originalPatient.Vitals,
            ContextFlags = originalPatient.ContextFlags
        };
    }

    private static IEnumerable<PatientMedicationAlert> ConvertFullSafetyAlerts(
        PatientRuleContext ctx,
        List<MedicationCandidate> targetMeds,
        SafetyScreeningResult fullSafetyResult)
    {
        foreach (var report in fullSafetyResult.DrugReports)
        {
            var med = targetMeds.FirstOrDefault(m =>
                Normalize(m.Name).Equals(Normalize(report.DrugName), StringComparison.OrdinalIgnoreCase) ||
                Normalize(m.Name).Equals(Normalize(report.DrugId), StringComparison.OrdinalIgnoreCase));
            var includeGeneralExternalWarnings = med == null && IsExternallyResolvedReport(report);

            var medicationName = med?.Name ?? report.DrugName;
            var medicationId = med?.RxCui ?? report.DrugId;
            var conditionName = med?.ConditionName ?? "Medication safety knowledge base";

            foreach (var alert in report.MustAvoidReasons)
                yield return BuildFullSafetyAlert(ctx, report, alert, medicationName, medicationId, conditionName, "FULL-MUST-AVOID");

            foreach (var alert in report.AllergyAlerts)
                yield return BuildFullSafetyAlert(ctx, report, alert, medicationName, medicationId, conditionName, "FULL-ALLERGY");

            foreach (var alert in report.BlackBoxWarnings.Where(ShouldPromoteFullSafetyAlert))
                yield return BuildFullSafetyAlert(ctx, report, alert, medicationName, medicationId, conditionName, "FULL-BLACK-BOX");

            foreach (var alert in report.Warnings.Where(alert => ShouldPromoteFullSafetyAlert(alert, includeGeneralExternalWarnings)))
                yield return BuildFullSafetyAlert(ctx, report, alert, medicationName, medicationId, conditionName, "FULL-WARNING");

            foreach (var alert in report.UseWithCaution.Where(alert => ShouldPromoteFullSafetyAlert(alert, includeGeneralExternalWarnings)))
                yield return BuildFullSafetyAlert(ctx, report, alert, medicationName, medicationId, conditionName, "FULL-CAUTION");

            foreach (var interaction in report.DrugInteractions)
            {
                yield return new PatientMedicationAlert
                {
                    RuleId = $"FULL-INTERACTION-{NormalizeRuleId(report.DrugId)}-{NormalizeRuleId(interaction.CurrentDrug)}",
                    Level = interaction.Level,
                    Category = "Drug Interaction",
                    MedicationName = medicationName,
                    RxCui = medicationId,
                    DrugClass = report.DrugClass,
                    ConditionName = conditionName,
                    Message = $"{interaction.ProposedDrug} + {interaction.CurrentDrug}: {interaction.Effect}",
                    SuggestedAction = interaction.Management,
                    Source = SourceForInteraction(interaction),
                    MatchedPatientFacts = BuildMatchedFacts(ctx)
                };
            }
        }
    }

    private static string SourceForInteraction(InteractionAlert interaction)
    {
        if (ContainsAny(interaction.Mechanism, "RxNorm", "NIH"))
            return "NIH RxNorm Interaction API";

        if (ContainsAny(interaction.Mechanism, "DailyMed"))
            return "DailyMed SPL";

        return "MedSafety Static Knowledge Base";
    }

    private static bool ShouldPromoteFullSafetyAlert(SafetyAlert alert)
        => ShouldPromoteFullSafetyAlert(alert, includeGeneralExternalWarnings: false);

    private static bool ShouldPromoteFullSafetyAlert(
        SafetyAlert alert,
        bool includeGeneralExternalWarnings)
    {
        if (alert.Level is AlertLevel.Critical or AlertLevel.High) return true;

        if (includeGeneralExternalWarnings &&
            IsExternalEvidenceSource(alert.Source) &&
            ContainsAny(alert.Category, "warning"))
        {
            return true;
        }

        return ContainsAny(alert.Category,
            "contraindication",
            "patient relevant",
            "pregnancy",
            "breastfeeding",
            "beers",
            "allergy");
    }

    private static HashSet<string> BuildExternallyResolvedMedicationKeys(SafetyScreeningResult? externalSafetyResult)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (externalSafetyResult == null) return keys;

        foreach (var report in externalSafetyResult.DrugReports.Where(IsExternallyResolvedReport))
        {
            if (!string.IsNullOrWhiteSpace(report.DrugName)) keys.Add(Normalize(report.DrugName));
            if (!string.IsNullOrWhiteSpace(report.DrugId)) keys.Add(Normalize(report.DrugId));
        }

        return keys;
    }

    private static bool IsExternallyResolved(UnrecognizedMedicationItem item, HashSet<string> externallyResolvedMedicationKeys) =>
        externallyResolvedMedicationKeys.Contains(Normalize(item.MedicationName));

    private static bool IsExternallyResolvedReport(DrugSafetyReport report)
    {
        var alerts = report.MustAvoidReasons
            .Concat(report.BlackBoxWarnings)
            .Concat(report.Warnings)
            .Concat(report.UseWithCaution)
            .Concat(report.AllergyAlerts);

        return alerts.Any(alert => IsExternalEvidenceSource(alert.Source)) ||
            report.DrugInteractions.Any(interaction => ContainsAny(interaction.Mechanism, "RxNorm", "NIH"));
    }

    private static bool IsExternalEvidenceSource(string? source) =>
        !string.IsNullOrWhiteSpace(source) &&
        ContainsAny(source, "OpenFDA", "DailyMed", "RxNorm", "NIH");

    private static PatientMedicationAlert BuildFullSafetyAlert(
        PatientRuleContext ctx,
        DrugSafetyReport report,
        SafetyAlert alert,
        string medicationName,
        string medicationId,
        string conditionName,
        string rulePrefix)
    {
        return new PatientMedicationAlert
        {
            RuleId = $"{rulePrefix}-{NormalizeRuleId(report.DrugId)}-{NormalizeRuleId(alert.Category)}",
            Level = alert.Level,
            Category = alert.Category,
            MedicationName = medicationName,
            RxCui = medicationId,
            DrugClass = report.DrugClass,
            ConditionName = conditionName,
            Message = alert.Message,
            SuggestedAction = SuggestedActionFor(alert),
            Source = alert.Source ?? "MedSafety Static Knowledge Base",
            MatchedPatientFacts = BuildMatchedFacts(ctx)
        };
    }

    private static string SuggestedActionFor(SafetyAlert alert)
    {
        return alert.Level switch
        {
            AlertLevel.Critical => "Avoid or block pending clinician/pharmacist review.",
            AlertLevel.High => "Route to clinician/pharmacist review before use.",
            AlertLevel.Moderate => "Use only with documented monitoring and risk-benefit review.",
            _ => "Document review and monitor as clinically appropriate."
        };
    }

    private static PatientMedicationAlert BuildUnrecognizedAlert(UnrecognizedMedicationItem item)
    {
        return new PatientMedicationAlert
        {
            RuleId = $"UNRECOGNIZED-{NormalizeRuleId(item.MedicationName)}",
            Level = item.Source.Equals("Proposed medication", StringComparison.OrdinalIgnoreCase)
                ? AlertLevel.High
                : AlertLevel.Moderate,
            Category = "Unrecognized Medication",
            MedicationName = item.MedicationName,
            RxCui = BuildUnrecognizedId(item.MedicationName),
            DrugClass = "Unrecognized",
            ConditionName = item.Source,
            Message = item.Reason,
            SuggestedAction = "Verify spelling, generic name, and formulary mapping before relying on safety classification.",
            Source = "User input"
        };
    }

    private static List<PatientMedicationAlert> DeduplicateAlerts(List<PatientMedicationAlert> alerts)
    {
        return alerts
            .GroupBy(a => $"{a.RxCui}|{a.Category}|{a.Message}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(a => a.Level).First())
            .ToList();
    }

    private IEnumerable<PatientMedicationAlert> EvaluateCustomRules(
        PatientRuleContext ctx,
        List<MedicationCandidate> targetMeds)
    {
        var medicationFacts = BuildCustomMedicationFacts(ctx, targetMeds);

        foreach (var rule in _customSafetyRuleService.GetAll().Where(r => r.Enabled))
        {
            foreach (var med in medicationFacts.Where(m => MatchesAnyTerm(m.Name, rule.MedicationTerms)))
            {
                var matchedFacts = MatchCustomRuleFacts(ctx, rule);
                if (matchedFacts.Count == 0) continue;

                yield return new PatientMedicationAlert
                {
                    RuleId = $"CUSTOM-{rule.Id}",
                    Level = rule.Level,
                    Category = rule.Category,
                    MedicationName = med.Name,
                    RxCui = med.Id,
                    DrugClass = med.DrugClass,
                    ConditionName = med.ConditionName,
                    Message = rule.Message,
                    SuggestedAction = rule.SuggestedAction,
                    Source = rule.Source,
                    MatchedPatientFacts = matchedFacts
                };
            }
        }
    }

    private static List<CustomMedicationFact> BuildCustomMedicationFacts(PatientRuleContext ctx, List<MedicationCandidate> targetMeds)
    {
        return targetMeds
            .Select(m => new CustomMedicationFact(m.Name, m.RxCui, m.DrugClass, m.ConditionName))
            .Concat(ctx.Patient.ProposedMedications.Select(m =>
            {
                var candidate = ResolveMedicationCandidate(m, "User-proposed medication");
                return candidate == null
                    ? new CustomMedicationFact(m, BuildUnrecognizedId(m), "Unrecognized", "User-proposed medication")
                    : new CustomMedicationFact(candidate.Name, candidate.RxCui, candidate.DrugClass, candidate.ConditionName);
            }))
            .Concat(ctx.Patient.CurrentMedications.Select(m =>
            {
                var candidate = ResolveMedicationCandidate(m, "Current medication");
                return candidate == null
                    ? new CustomMedicationFact(m, BuildUnrecognizedId(m), "Unrecognized", "Current medication")
                    : new CustomMedicationFact(candidate.Name, candidate.RxCui, candidate.DrugClass, candidate.ConditionName);
            }))
            .GroupBy(m => Normalize(m.Name), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static List<string> MatchCustomRuleFacts(PatientRuleContext ctx, CustomSafetyRule rule)
    {
        var facts = new List<string>();
        AddRuleMatches(facts, "Condition", ctx.Patient.Comorbidities.Concat(ctx.Patient.CurrentComplaints).Concat(ctx.ConditionLabels), rule.ConditionTerms);
        AddRuleMatches(facts, "Allergy", ctx.Patient.Allergies, rule.AllergyTerms);
        AddRuleMatches(facts, "Symptom", ctx.Patient.Symptoms, rule.SymptomTerms);
        AddRuleMatches(facts, "Context", ctx.ActiveRiskFlags, rule.RiskFlagTerms);
        return facts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddRuleMatches(List<string> facts, string label, IEnumerable<string> patientFacts, IEnumerable<string> ruleTerms)
    {
        var terms = ruleTerms.ToList();
        if (terms.Count == 0) return;

        foreach (var fact in patientFacts)
        {
            if (MatchesAnyTerm(fact, terms))
                facts.Add($"{label}: {fact}");
        }
    }

    private static bool MatchesAnyTerm(string value, IEnumerable<string> terms)
    {
        var normalizedValue = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalizedValue)) return false;

        return terms
            .Select(Normalize)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Any(term => normalizedValue.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                         term.Contains(normalizedValue, StringComparison.OrdinalIgnoreCase));
    }

    private static MissingContextItem Missing(MedicationCandidate med, string field, string reason) => new()
    {
        Field = field,
        Reason = reason,
        MedicationName = med.Name,
        RxCui = med.RxCui
    };

    private static List<string> BuildMatchedFacts(PatientRuleContext ctx)
    {
        var p = ctx.Patient;
        var facts = new List<string>();
        facts.AddRange(ctx.ConditionLabels.Select(c => $"Condition: {c}"));
        facts.AddRange(p.CurrentComplaints.Select(c => $"Current complaint: {c}"));
        facts.AddRange(p.CurrentMedications.Select(m => $"Current medication: {m}"));
        facts.AddRange(p.ProposedMedications.Select(m => $"Proposed medication: {m}"));
        facts.AddRange(p.Symptoms.Select(s => $"Symptom: {s}"));
        facts.AddRange(ctx.ActiveRiskFlags.Select(f => $"Context: {f}"));
        if (p.Labs.Glucose is not null) facts.Add($"Glucose: {p.Labs.Glucose}");
        if (p.Labs.EGfr is not null) facts.Add($"eGFR: {p.Labs.EGfr}");
        if (p.Labs.Potassium is not null) facts.Add($"Potassium: {p.Labs.Potassium}");
        if (p.Labs.Sodium is not null) facts.Add($"Sodium: {p.Labs.Sodium}");
        if (p.Vitals.HeartRate is not null) facts.Add($"Heart rate: {p.Vitals.HeartRate}");
        if (p.Vitals.SystolicBp is not null) facts.Add($"Systolic BP: {p.Vitals.SystolicBp}");
        if (p.IsPregnant) facts.Add("Pregnant");
        if (p.IsBreastfeeding) facts.Add("Breastfeeding");
        if (p.Age is not null) facts.Add($"Age: {p.Age}");
        return facts;
    }

    private static IEnumerable<string> GetActiveRiskFlags(PatientProfile p)
    {
        if (p.ContextFlags.Npo) yield return "NPO";
        if (p.ContextFlags.PoorIntake) yield return "poor oral intake";
        if (p.ContextFlags.AcuteIllness) yield return "acute illness";
        if (p.ContextFlags.RecentSurgery) yield return "recent surgery";
        if (p.ContextFlags.Dehydration) yield return "dehydration";
        if (p.ContextFlags.MetabolicAcidosis) yield return "metabolic acidosis";
        if (p.ContextFlags.Dka) yield return "diabetic ketoacidosis";
        if (p.ContextFlags.AcuteKidneyInjury) yield return "acute kidney injury";
        if (p.ContextFlags.Sepsis) yield return "sepsis";
        if (p.ContextFlags.Hypoxia) yield return "hypoxia";
        if (p.ContextFlags.Shock) yield return "shock";
        if (p.ContextFlags.Anuria) yield return "anuria";
        if (p.ContextFlags.BowelObstruction) yield return "bowel obstruction";
        if (p.ContextFlags.UrinaryObstruction) yield return "urinary obstruction";
        if (p.ContextFlags.AsthmaCopd) yield return "asthma/COPD";
        if (p.ContextFlags.HeartBlock) yield return "heart block";
        if (p.ContextFlags.DecompensatedHeartFailure) yield return "decompensated heart failure";
        if (p.ContextFlags.CardiacDisease) yield return "significant cardiac disease";
        if (p.ContextFlags.RecentMiOrUnstableAngina) yield return "recent MI or unstable angina";
        if (HasHeartAttackOrAcsComplaint(p)) yield return "heart attack / acute coronary syndrome";
        if (p.ContextFlags.AdrenalInsufficiency) yield return "uncorrected adrenal insufficiency";
        if (p.ContextFlags.ThyroidCancerHistory) yield return "medullary thyroid carcinoma history";
        if (p.ContextFlags.Men2History) yield return "MEN2 history";
        if (p.ContextFlags.PancreatitisHistory) yield return "pancreatitis history";
        if (p.ContextFlags.AngioedemaAceArbHistory) yield return "ACE/ARB angioedema history";
        if (p.ContextFlags.Neutropenia) yield return "neutropenia";
        if (p.ContextFlags.Gout) yield return "gout";
        if (p.ContextFlags.DigoxinUse) yield return "digoxin use";
        if (p.ContextFlags.PotassiumSupplement) yield return "potassium supplement";
        if (p.ContextFlags.PotassiumSparingDiuretic) yield return "potassium-sparing diuretic";
        if (p.ContextFlags.AliskirenUse) yield return "aliskiren use";
        if (p.ContextFlags.LastAceInhibitorWithin36Hours) yield return "ACE inhibitor within 36 hours";
        if (p.ContextFlags.HeavyAlcoholUse) yield return "heavy alcohol use";
        if (p.ContextFlags.ReducedInsulin) yield return "recent reduced insulin";
    }

    private static MedicationCandidate? ResolveMedication(string value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized)) return null;

        return MedicationMap.FirstOrDefault(m =>
            m.RxCui.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            Normalize(m.Name).Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            Normalize(m.Ingredient).Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            m.Aliases.Any(a => Normalize(a).Equals(normalized, StringComparison.OrdinalIgnoreCase)));
    }

    private static MedicationCandidate? ResolveMedicationCandidate(string value, string conditionName)
    {
        var workbookMedication = ResolveMedication(value);
        if (workbookMedication != null) return workbookMedication;

        var drug = MedicationKnowledgeBase.FindDrug(value);
        return drug == null ? null : FromDrug(drug, conditionName);
    }

    private static MedicationCandidate FromDrug(Drug drug, string conditionName) =>
        new(
            "direct_medication",
            conditionName,
            drug.GenericName,
            drug.GenericName,
            drug.DrugId,
            drug.DrugClass,
            drug.BrandNames);

    private static List<UnrecognizedMedicationItem> BuildUnrecognizedMedicationItems(PatientProfile patient)
    {
        var items = new List<UnrecognizedMedicationItem>();

        items.AddRange(patient.ProposedMedications
            .Where(m => ResolveMedicationCandidate(m, "User-proposed medication") == null)
            .Select(m => new UnrecognizedMedicationItem
            {
                MedicationName = m,
                Source = "Proposed medication",
                Reason = "Medication was not found. Verify the medication name, spelling, or try the generic or brand name."
            }));

        items.AddRange(patient.CurrentMedications
            .Where(m => ResolveMedicationCandidate(m, "Current medication") == null)
            .Select(m => new UnrecognizedMedicationItem
            {
                MedicationName = m,
                Source = "Current medication",
                Reason = "Medication was not found. Verify the medication name, spelling, or try the generic or brand name."
            }));

        return items
            .GroupBy(i => $"{i.Source}:{Normalize(i.MedicationName)}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static bool HasCurrentMedication(PatientProfile p, string name) =>
        p.CurrentMedications.Any(m => Normalize(m).Contains(Normalize(name)));

    private static bool HasAllergy(PatientProfile p, params string[] allergies) =>
        p.Allergies.Any(a => allergies.Any(term =>
            Normalize(a).Contains(Normalize(term)) || Normalize(term).Contains(Normalize(a))));

    private static bool HasLowGlucose(PatientProfile p) => p.Labs.Glucose < 70;

    private static bool HasSymptom(PatientProfile p, string symptom) =>
        p.Symptoms.Any(s => Normalize(s).Contains(Normalize(symptom)) || Normalize(symptom).Contains(Normalize(s)));

    private static bool HasAnySymptom(PatientProfile p, params string[] symptoms) =>
        symptoms.Any(s => HasSymptom(p, s));

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string BuildUnrecognizedId(string medicationName) =>
        $"unknown:{NormalizeRuleId(medicationName)}";

    private static string NormalizeRuleId(string value)
    {
        var normalized = Normalize(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? "unknown"
            : new string(normalized.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
    }

    private static bool HasHeartAttackOrAcsComplaint(PatientProfile p) =>
        p.CurrentComplaints.Any(IsHeartAttackOrAcs);

    private static bool IsHeartAttackOrAcs(string value)
    {
        var normalized = Normalize(value);
        return normalized.Equals("mi", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(normalized,
                   "heart attack",
                   "myocardial infarction",
                   "acute coronary syndrome",
                   "unstable angina",
                   "stemi",
                   "nstemi",
                   "acute mi",
                   "recent mi");
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace("-", " ").Replace("/", " ").ToLowerInvariant();

    private static bool IsDiabetesMed(string rxcui) => rxcui is "274783" or "86009" or "6809" or "4821" or "593411" or "1991302" or "1545653";
    private static bool IsRenalSensitive(string rxcui) => rxcui is "6809" or "593411" or "29046" or "1656339" or "4603" or "5487";
    private static bool IsDiuretic(string rxcui) => rxcui is "5487" or "4603";
    private static bool IsBetaBlocker(string rxcui) => rxcui is "6918" or "8787";

    private static List<MedicationCandidate> BuildMedicationMap() => new()
    {
        new("type_1_diabetes", "Type 1 diabetes", "insulin glargine", "insulin glargine", "274783", "long-acting basal insulin"),
        new("type_1_diabetes", "Type 1 diabetes", "insulin lispro", "insulin lispro", "86009", "rapid-acting prandial insulin"),
        new("type_2_diabetes", "Type 2 diabetes", "metformin", "metformin", "6809", "biguanide"),
        new("type_2_diabetes", "Type 2 diabetes", "glipizide", "glipizide", "4821", "sulfonylurea"),
        new("type_2_diabetes", "Type 2 diabetes", "sitagliptin", "sitagliptin", "593411", "DPP-4 inhibitor"),
        new("type_2_diabetes", "Type 2 diabetes", "semaglutide", "semaglutide", "1991302", "GLP-1 receptor agonist"),
        new("myasthenia_gravis", "Myasthenia gravis", "pyridostigmine", "pyridostigmine", "9000", "acetylcholinesterase inhibitor"),
        new("hypertension", "Hypertension", "hydrochlorothiazide", "hydrochlorothiazide", "5487", "thiazide diuretic", new() { "hctz" }),
        new("hypertension", "Hypertension", "lisinopril", "lisinopril", "29046", "ACE inhibitor"),
        new("hypertension", "Hypertension", "amlodipine", "amlodipine", "17767", "dihydropyridine calcium channel blocker"),
        new("acute_coronary_syndrome", "Heart attack / acute coronary syndrome", "aspirin", "aspirin", "1191", "NSAID / antiplatelet", new() { "asa", "baby aspirin" }),
        new("acute_coronary_syndrome", "Heart attack / acute coronary syndrome", "clopidogrel", "clopidogrel", "32968", "P2Y12 antiplatelet", new() { "plavix" }),
        new("acute_coronary_syndrome", "Heart attack / acute coronary syndrome", "atorvastatin", "atorvastatin", "83367", "statin", new() { "lipitor" }),
        new("acute_coronary_syndrome", "Heart attack / acute coronary syndrome", "metoprolol", "metoprolol", "6918", "beta blocker"),
        new("acute_coronary_syndrome", "Heart attack / acute coronary syndrome", "lisinopril", "lisinopril", "29046", "ACE inhibitor"),
        new("heart_failure", "Heart failure / heart condition", "sacubitril-valsartan", "sacubitril / valsartan", "1656339", "angiotensin receptor-neprilysin inhibitor / ARNI", new() { "entresto", "sacubitril valsartan" }),
        new("heart_failure", "Heart failure / heart condition", "metoprolol", "metoprolol", "6918", "beta blocker"),
        new("heart_failure", "Heart failure / heart condition", "empagliflozin", "empagliflozin", "1545653", "SGLT2 inhibitor", new() { "jardiance" }),
        new("heart_failure", "Heart failure / heart condition", "furosemide", "furosemide", "4603", "loop diuretic"),
        new("hypothyroidism", "Hypothyroidism", "levothyroxine", "levothyroxine", "10582", "thyroid hormone T4"),
        new("hypothyroidism", "Hypothyroidism", "liothyronine", "liothyronine", "10814", "thyroid hormone T3"),
        new("hyperthyroidism", "Hyperthyroidism", "methimazole", "methimazole", "6835", "antithyroid thionamide"),
        new("hyperthyroidism", "Hyperthyroidism", "propranolol", "propranolol", "8787", "nonselective beta blocker")
    };

    private sealed class MedicationCandidate
    {
        public MedicationCandidate(
            string conditionKey,
            string conditionName,
            string name,
            string ingredient,
            string rxCui,
            string drugClass,
            List<string>? aliases = null)
        {
            ConditionKey = conditionKey;
            ConditionName = conditionName;
            Name = name;
            Ingredient = ingredient;
            RxCui = rxCui;
            DrugClass = drugClass;
            Aliases = aliases ?? new();
        }

        public string ConditionKey { get; }
        public string ConditionName { get; }
        public string Name { get; }
        public string Ingredient { get; }
        public string RxCui { get; }
        public string DrugClass { get; }
        public List<string> Aliases { get; }
    }

    private sealed record CustomMedicationFact(string Name, string Id, string DrugClass, string ConditionName);

    private sealed class PatientRuleContext
    {
        public PatientProfile Patient { get; init; } = new();
        public HashSet<string> ConditionKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CurrentRxCuis { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ProposedRxCuis { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<MedicationCandidate> CurrentMedicationCandidates { get; init; } = new();
        public List<MedicationCandidate> ProposedMedicationCandidates { get; init; } = new();
        public List<UnrecognizedMedicationItem> UnrecognizedMedications { get; init; } = new();
        public List<string> ConditionLabels { get; init; } = new();
        public List<string> ActiveRiskFlags { get; init; } = new();
    }
}
