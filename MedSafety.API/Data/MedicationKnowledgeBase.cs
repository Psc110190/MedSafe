using MedSafety.API.Models;

namespace MedSafety.API.Data;

/// <summary>
/// In-memory medication knowledge base with clinically accurate safety data.
/// In production, this would be backed by a database (e.g., RxNorm, DailyMed, FDA FAERS).
/// </summary>
public static class MedicationKnowledgeBase
{
    private static readonly List<Drug> _drugs = BuildKnowledgeBase();

    public static IReadOnlyList<Drug> GetAllDrugs() => _drugs.AsReadOnly();

    public static Drug? FindDrug(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return _drugs.FirstOrDefault(d =>
            d.GenericName.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            d.BrandNames.Any(b => b.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ||
            d.DrugId.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static List<Drug> SearchDrugs(string query)
    {
        var normalized = query.Trim().ToLowerInvariant();
        return _drugs.Where(d =>
            d.GenericName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            d.BrandNames.Any(b => b.Contains(normalized, StringComparison.OrdinalIgnoreCase)) ||
            d.DrugClass.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            d.Category.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Maps common allergy groups so cross-reactivity is detected.
    /// E.g., patient allergic to "Penicillin" → flag all beta-lactam antibiotics.
    /// </summary>
    public static readonly Dictionary<string, List<string>> AllergyGroupMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Penicillin"] = new() { "Penicillin", "Beta-Lactam", "Amoxicillin", "Ampicillin", "Piperacillin" },
        ["Amoxicillin"] = new() { "Penicillin", "Beta-Lactam" },
        ["Sulfa"] = new() { "Sulfonamide", "Sulfamethoxazole", "Sulfasalazine" },
        ["NSAIDs"] = new() { "NSAID", "Ibuprofen", "Naproxen", "Aspirin", "Diclofenac", "Ketorolac" },
        ["Aspirin"] = new() { "NSAID", "Aspirin", "Salicylate" },
        ["Cephalosporin"] = new() { "Cephalosporin", "Beta-Lactam" },
        ["Fluoroquinolone"] = new() { "Fluoroquinolone", "Ciprofloxacin", "Levofloxacin", "Moxifloxacin" },
        ["ACE Inhibitor"] = new() { "ACE Inhibitor", "Enalapril", "Lisinopril", "Ramipril" },
        ["Statin"] = new() { "Statin", "Atorvastatin", "Rosuvastatin", "Simvastatin" },
        ["Opioid"] = new() { "Opioid", "Morphine", "Codeine", "Tramadol", "Fentanyl" },
        ["Iodine"] = new() { "Iodine", "Iodinated Contrast" },
        ["Latex"] = new() { "Latex" },
        ["Macrolide"] = new() { "Macrolide", "Azithromycin", "Erythromycin", "Clarithromycin" },
        ["Tetracycline"] = new() { "Tetracycline", "Doxycycline", "Minocycline" },
        ["Benzodiazepine"] = new() { "Benzodiazepine", "Diazepam", "Lorazepam", "Alprazolam" },
    };

    /// <summary>
    /// Maps comorbidity/condition synonyms and related terms for matching.
    /// </summary>
    public static readonly Dictionary<string, List<string>> ConditionSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Myasthenia Gravis"] = new() { "myasthenia gravis", "mg", "myasthenic syndrome" },
        ["Renal Failure"] = new() { "renal failure", "kidney failure", "ckd", "chronic kidney disease", "renal impairment", "renal insufficiency" },
        ["Hepatic Impairment"] = new() { "hepatic impairment", "liver failure", "liver disease", "hepatic failure", "cirrhosis" },
        ["Heart Failure"] = new() { "heart failure", "chf", "congestive heart failure", "hf", "cardiac failure" },
        ["Asthma"] = new() { "asthma", "reactive airway disease", "bronchial asthma" },
        ["COPD"] = new() { "copd", "chronic obstructive pulmonary disease", "emphysema", "chronic bronchitis" },
        ["Diabetes"] = new() { "diabetes", "diabetes mellitus", "dm", "type 2 diabetes", "t2dm", "type 1 diabetes" },
        ["Hypertension"] = new() { "hypertension", "htn", "high blood pressure" },
        ["Epilepsy"] = new() { "epilepsy", "seizure disorder", "seizures" },
        ["Pregnancy"] = new() { "pregnancy", "pregnant" },
        ["Peptic Ulcer"] = new() { "peptic ulcer", "gastric ulcer", "stomach ulcer", "duodenal ulcer", "gerd", "gi bleed" },
        ["Glaucoma"] = new() { "glaucoma", "narrow angle glaucoma", "angle closure glaucoma" },
        ["QT Prolongation"] = new() { "qt prolongation", "long qt", "long qt syndrome", "torsades de pointes" },
        ["Bleeding Disorder"] = new() { "bleeding disorder", "hemophilia", "coagulopathy", "thrombocytopenia" },
        ["Depression"] = new() { "depression", "major depressive disorder", "mdd" },
        ["Bradycardia"] = new() { "bradycardia", "slow heart rate", "heart block", "av block" },
        ["Hypotension"] = new() { "hypotension", "low blood pressure" },
        ["Hyperkalemia"] = new() { "hyperkalemia", "high potassium" },
        ["Gout"] = new() { "gout", "hyperuricemia" },
        ["Pheochromocytoma"] = new() { "pheochromocytoma", "pheo" },
        ["G6PD Deficiency"] = new() { "g6pd deficiency", "glucose-6-phosphate dehydrogenase deficiency", "favism" },
        ["Rhabdomyolysis"] = new() { "rhabdomyolysis", "myopathy" },
        ["Thyroid"] = new() { "hypothyroidism", "hyperthyroidism", "thyroid disease", "thyroid disorder" },
        ["Parkinson"] = new() { "parkinson", "parkinson's disease", "parkinsonian" },
    };

    private static List<Drug> BuildKnowledgeBase()
    {
        return new List<Drug>
        {
            // ============================================================
            // ANTIBIOTICS
            // ============================================================
            new Drug
            {
                DrugId = "telithromycin",
                GenericName = "Telithromycin",
                BrandNames = new() { "Ketek" },
                DrugClass = "Ketolide Antibiotic",
                Category = "Antibiotic",
                Indications = new() { "Community-acquired pneumonia" },
                AllergyGroups = new() { "Macrolide" },
                BlackBoxWarnings = new()
                {
                    "CONTRAINDICATED in patients with Myasthenia Gravis – fatal and life-threatening respiratory failure has occurred.",
                    "Hepatotoxicity: Severe liver injury including hepatic necrosis and hepatic failure reported, some fatal."
                },
                Contraindications = new()
                {
                    new() { Condition = "Myasthenia Gravis", Severity = SeverityLevel.Absolute,
                        Description = "Fatal respiratory failure reported. ABSOLUTELY CONTRAINDICATED.", Source = "FDA Black Box" },
                    new() { Condition = "Hepatic Impairment", Severity = SeverityLevel.Absolute,
                        Description = "Severe hepatotoxicity, including fatal hepatic failure.", Source = "FDA Label" },
                    new() { Condition = "QT Prolongation", Severity = SeverityLevel.Absolute,
                        Description = "Can prolong QT interval; risk of fatal arrhythmias.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Visual disturbances (blurred vision, difficulty focusing) may occur. Avoid driving.",
                    "Loss of consciousness has been reported – may be related to vagal syndrome.",
                    "Monitor liver function tests during therapy."
                },
                UseWithCaution = new()
                {
                    "Patients with coronary artery disease (risk of QT prolongation)",
                    "Concurrent use of CYP3A4 inhibitors or inducers",
                    "Elderly patients – increased risk of hepatotoxicity"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "simvastatin", InteractingDrugName = "Simvastatin", Severity = InteractionSeverity.Major,
                        Effect = "Rhabdomyolysis risk", Mechanism = "CYP3A4 inhibition by telithromycin increases statin levels",
                        ClinicalManagement = "AVOID combination. Discontinue statin during telithromycin therapy." },
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Major,
                        Effect = "Increased anticoagulant effect and bleeding risk", Mechanism = "CYP3A4/2C9 inhibition",
                        ClinicalManagement = "Monitor INR closely if combination is unavoidable." },
                    new() { InteractingDrugId = "metoprolol", InteractingDrugName = "Metoprolol", Severity = InteractionSeverity.Moderate,
                        Effect = "Increased risk of bradycardia", Mechanism = "CYP2D6 inhibition increases metoprolol levels",
                        ClinicalManagement = "Monitor heart rate." },
                },
                SideEffects = new() { "Diarrhea", "Nausea", "Headache", "Dizziness", "Visual disturbances" }
            },

            new Drug
            {
                DrugId = "amoxicillin",
                GenericName = "Amoxicillin",
                BrandNames = new() { "Amoxil", "Moxatag" },
                DrugClass = "Penicillin Antibiotic",
                Category = "Antibiotic",
                Indications = new() { "Otitis media", "Pneumonia", "UTI", "H. pylori", "Pharyngitis" },
                AllergyGroups = new() { "Penicillin", "Beta-Lactam" },
                BlackBoxWarnings = new(),
                Contraindications = new()
                {
                    new() { Condition = "Penicillin Allergy", Severity = SeverityLevel.Absolute,
                        Description = "Anaphylaxis risk. Do NOT administer to patients with known penicillin allergy.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Clostridium difficile-associated diarrhea possible with all antibiotics.",
                    "Cross-reactivity with cephalosporins (~1-2% risk in penicillin-allergic patients).",
                    "May cause maculopapular rash (especially in mononucleosis – avoid in EBV)."
                },
                UseWithCaution = new()
                {
                    "Renal impairment – dose adjustment required",
                    "History of antibiotic-associated colitis",
                    "Patients on oral contraceptives (reduced efficacy)"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "methotrexate", InteractingDrugName = "Methotrexate", Severity = InteractionSeverity.Major,
                        Effect = "Increased methotrexate toxicity", Mechanism = "Reduced renal clearance of methotrexate",
                        ClinicalManagement = "Monitor methotrexate levels; consider dose reduction." },
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Moderate,
                        Effect = "Increased bleeding risk", Mechanism = "Altered gut flora affecting vitamin K synthesis",
                        ClinicalManagement = "Monitor INR more frequently." },
                },
                SideEffects = new() { "Diarrhea", "Nausea", "Rash", "Vomiting" }
            },

            new Drug
            {
                DrugId = "ciprofloxacin",
                GenericName = "Ciprofloxacin",
                BrandNames = new() { "Cipro", "Cipro XR" },
                DrugClass = "Fluoroquinolone Antibiotic",
                Category = "Antibiotic",
                Indications = new() { "UTI", "Respiratory infections", "Anthrax", "Joint/bone infections" },
                AllergyGroups = new() { "Fluoroquinolone" },
                BlackBoxWarnings = new()
                {
                    "Fluoroquinolones are associated with disabling and potentially irreversible serious adverse reactions including tendinitis and tendon rupture, peripheral neuropathy, and CNS effects.",
                    "Fluoroquinolones may exacerbate muscle weakness in persons with Myasthenia Gravis. Avoid in patients with known history of MG.",
                    "Increased risk of aortic dissection and aortic aneurysm."
                },
                Contraindications = new()
                {
                    new() { Condition = "Myasthenia Gravis", Severity = SeverityLevel.Absolute,
                        Description = "May exacerbate muscle weakness. Black box warning.", Source = "FDA Black Box" },
                    new() { Condition = "QT Prolongation", Severity = SeverityLevel.Relative,
                        Description = "Risk of QTc prolongation and torsades de pointes.", Source = "FDA Label" },
                    new() { Condition = "Epilepsy", Severity = SeverityLevel.Relative,
                        Description = "Lowers seizure threshold; increased risk of seizures.", Source = "FDA Label" },
                    new() { Condition = "G6PD Deficiency", Severity = SeverityLevel.Relative,
                        Description = "Risk of hemolytic reactions.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Tendon rupture risk – especially in patients >60, those on corticosteroids, or post-transplant.",
                    "Peripheral neuropathy – may be irreversible. Discontinue at first sign.",
                    "CNS effects: seizures, tremor, dizziness, confusion.",
                    "Photosensitivity – avoid excessive sun exposure.",
                    "Aortic aneurysm/dissection risk – avoid in patients with known aneurysms or Marfan syndrome."
                },
                UseWithCaution = new()
                {
                    "Elderly patients (tendon rupture risk)",
                    "Patients on corticosteroids (tendon rupture risk compounded)",
                    "Renal impairment – dose adjustment required",
                    "Patients with diabetes (dysglycemia risk)"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Major,
                        Effect = "Markedly increased INR and bleeding risk", Mechanism = "CYP1A2 inhibition",
                        ClinicalManagement = "Monitor INR closely; reduce warfarin dose if needed." },
                    new() { InteractingDrugId = "theophylline", InteractingDrugName = "Theophylline", Severity = InteractionSeverity.Major,
                        Effect = "Theophylline toxicity (seizures, arrhythmias)", Mechanism = "CYP1A2 inhibition",
                        ClinicalManagement = "Monitor theophylline levels; reduce dose." },
                    new() { InteractingDrugId = "metformin", InteractingDrugName = "Metformin", Severity = InteractionSeverity.Moderate,
                        Effect = "Dysglycemia (hypo/hyperglycemia)", Mechanism = "Altered glucose homeostasis",
                        ClinicalManagement = "Monitor blood glucose closely." },
                },
                SideEffects = new() { "Nausea", "Diarrhea", "Headache", "Dizziness", "Tendinitis" }
            },

            new Drug
            {
                DrugId = "azithromycin",
                GenericName = "Azithromycin",
                BrandNames = new() { "Zithromax", "Z-Pack", "Zmax" },
                DrugClass = "Macrolide Antibiotic",
                Category = "Antibiotic",
                Indications = new() { "Community-acquired pneumonia", "Pharyngitis", "Otitis media", "Sinusitis", "Chlamydia" },
                AllergyGroups = new() { "Macrolide" },
                BlackBoxWarnings = new(),
                Contraindications = new()
                {
                    new() { Condition = "Hepatic Impairment", Severity = SeverityLevel.Relative,
                        Description = "Cholestatic jaundice/hepatic dysfunction reported; avoid if prior hepatotoxicity with azithromycin.", Source = "FDA Label" },
                    new() { Condition = "QT Prolongation", Severity = SeverityLevel.Relative,
                        Description = "Prolonged cardiac repolarization and QT interval; risk of arrhythmia.", Source = "FDA Label" },
                    new() { Condition = "Myasthenia Gravis", Severity = SeverityLevel.Conditional,
                        Description = "May exacerbate symptoms of myasthenia gravis.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "QT prolongation and torsades de pointes reported – avoid in patients with known risk factors.",
                    "Hepatotoxicity: Abnormal liver function, hepatitis, cholestatic jaundice, hepatic necrosis, and hepatic failure.",
                    "Clostridium difficile-associated diarrhea (CDAD).",
                    "Infantile hypertrophic pyloric stenosis – risk in neonates <42 days."
                },
                UseWithCaution = new()
                {
                    "Patients with renal impairment (GFR <10 mL/min)",
                    "Patients with myasthenia gravis – exacerbation of symptoms reported",
                    "Elderly patients with cardiac risk factors"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Moderate,
                        Effect = "Increased anticoagulant effect", Mechanism = "Altered gut flora",
                        ClinicalManagement = "Monitor INR." },
                    new() { InteractingDrugId = "digoxin", InteractingDrugName = "Digoxin", Severity = InteractionSeverity.Moderate,
                        Effect = "Elevated digoxin levels", Mechanism = "Altered P-glycoprotein transport",
                        ClinicalManagement = "Monitor digoxin levels." },
                },
                SideEffects = new() { "Diarrhea", "Nausea", "Abdominal pain", "Headache" }
            },

            // ============================================================
            // CARDIOVASCULAR
            // ============================================================
            new Drug
            {
                DrugId = "metoprolol",
                GenericName = "Metoprolol",
                BrandNames = new() { "Lopressor", "Toprol-XL" },
                DrugClass = "Beta Blocker",
                Category = "Cardiovascular",
                Indications = new() { "Hypertension", "Angina", "Heart Failure", "Atrial fibrillation", "MI secondary prevention" },
                AllergyGroups = new() { "Beta Blocker" },
                BlackBoxWarnings = new()
                {
                    "Do NOT abruptly discontinue – risk of exacerbation of angina, MI, and ventricular arrhythmias. Taper gradually over 1-2 weeks."
                },
                Contraindications = new()
                {
                    new() { Condition = "Asthma", Severity = SeverityLevel.Absolute,
                        Description = "Beta-blockers can cause severe bronchospasm in asthmatics.", Source = "FDA Label" },
                    new() { Condition = "Bradycardia", Severity = SeverityLevel.Absolute,
                        Description = "Contraindicated in severe sinus bradycardia (HR <45-50).", Source = "FDA Label" },
                    new() { Condition = "Hypotension", Severity = SeverityLevel.Absolute,
                        Description = "Contraindicated in cardiogenic shock and systolic BP <100 mmHg.", Source = "FDA Label" },
                    new() { Condition = "Pheochromocytoma", Severity = SeverityLevel.Absolute,
                        Description = "Do NOT use without prior alpha-blockade – risk of hypertensive crisis.", Source = "FDA Label" },
                    new() { Condition = "COPD", Severity = SeverityLevel.Relative,
                        Description = "Use with extreme caution; may worsen bronchospasm.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "May mask symptoms of hypoglycemia in diabetic patients.",
                    "Abrupt withdrawal can precipitate MI or severe angina.",
                    "Can worsen heart failure initially – titrate slowly.",
                    "May mask thyrotoxicosis symptoms."
                },
                UseWithCaution = new()
                {
                    "Diabetes mellitus (masks hypoglycemia)",
                    "Peripheral vascular disease (may worsen claudication)",
                    "Depression (CNS beta-blocker effects)",
                    "Myasthenia Gravis (may worsen weakness)",
                    "Hepatic impairment (metabolized by liver)"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "verapamil", InteractingDrugName = "Verapamil", Severity = InteractionSeverity.Major,
                        Effect = "Severe bradycardia, heart block, heart failure", Mechanism = "Additive negative chronotropic/inotropic effect",
                        ClinicalManagement = "AVOID IV verapamil with beta-blockers. Oral combination use extreme caution." },
                    new() { InteractingDrugId = "clonidine", InteractingDrugName = "Clonidine", Severity = InteractionSeverity.Major,
                        Effect = "Rebound hypertension if clonidine stopped first", Mechanism = "Unopposed alpha stimulation",
                        ClinicalManagement = "Discontinue beta-blocker first, then taper clonidine." },
                    new() { InteractingDrugId = "digoxin", InteractingDrugName = "Digoxin", Severity = InteractionSeverity.Moderate,
                        Effect = "Additive bradycardia", Mechanism = "Both reduce AV conduction",
                        ClinicalManagement = "Monitor heart rate." },
                },
                SideEffects = new() { "Fatigue", "Bradycardia", "Dizziness", "Cold extremities", "Depression" }
            },

            new Drug
            {
                DrugId = "lisinopril",
                GenericName = "Lisinopril",
                BrandNames = new() { "Zestril", "Prinivil" },
                DrugClass = "ACE Inhibitor",
                Category = "Cardiovascular",
                Indications = new() { "Hypertension", "Heart failure", "Post-MI", "Diabetic nephropathy" },
                AllergyGroups = new() { "ACE Inhibitor" },
                BlackBoxWarnings = new()
                {
                    "PREGNANCY: ACE inhibitors can cause fetal injury and death when used during the 2nd and 3rd trimesters. Discontinue as soon as pregnancy is detected."
                },
                Contraindications = new()
                {
                    new() { Condition = "Pregnancy", Severity = SeverityLevel.Absolute,
                        Description = "Fetal toxicity – oligohydramnios, fetal renal failure, skull hypoplasia, death.", Source = "FDA Black Box" },
                    new() { Condition = "Hyperkalemia", Severity = SeverityLevel.Relative,
                        Description = "ACE inhibitors increase potassium. Avoid if K+ >5.5 mEq/L.", Source = "FDA Label" },
                    new() { Condition = "Renal Failure", Severity = SeverityLevel.Relative,
                        Description = "Risk of acute kidney injury; monitor renal function and potassium closely.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Angioedema – can be fatal. Higher risk in Black patients.",
                    "Hypotension – especially first dose, in volume-depleted patients.",
                    "Cough – dry, persistent, nonproductive cough in ~10% of patients.",
                    "Hyperkalemia – monitor potassium, especially with K-sparing diuretics."
                },
                UseWithCaution = new()
                {
                    "Bilateral renal artery stenosis (risk of renal failure)",
                    "Aortic stenosis (risk of hypotension)",
                    "Concurrent use of potassium supplements or K-sparing diuretics",
                    "Hepatic impairment"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "spironolactone", InteractingDrugName = "Spironolactone", Severity = InteractionSeverity.Major,
                        Effect = "Life-threatening hyperkalemia", Mechanism = "Both increase potassium retention",
                        ClinicalManagement = "If combined, monitor K+ frequently. Avoid if K+ >5.0." },
                    new() { InteractingDrugId = "ibuprofen", InteractingDrugName = "Ibuprofen", Severity = InteractionSeverity.Moderate,
                        Effect = "Reduced antihypertensive effect; increased renal impairment risk", Mechanism = "NSAIDs reduce prostaglandin-mediated renal blood flow",
                        ClinicalManagement = "Use lowest NSAID dose for shortest duration; monitor BP and renal function." },
                    new() { InteractingDrugId = "lithium", InteractingDrugName = "Lithium", Severity = InteractionSeverity.Major,
                        Effect = "Lithium toxicity", Mechanism = "Reduced lithium clearance",
                        ClinicalManagement = "Monitor lithium levels closely." },
                },
                SideEffects = new() { "Cough", "Dizziness", "Hyperkalemia", "Headache", "Fatigue" }
            },

            new Drug
            {
                DrugId = "warfarin",
                GenericName = "Warfarin",
                BrandNames = new() { "Coumadin", "Jantoven" },
                DrugClass = "Vitamin K Antagonist",
                Category = "Anticoagulant",
                Indications = new() { "DVT/PE", "Atrial fibrillation", "Mechanical heart valve", "Stroke prevention" },
                AllergyGroups = new() { "Warfarin", "Coumarin" },
                BlackBoxWarnings = new()
                {
                    "BLEEDING: Warfarin can cause major or fatal bleeding. Regular INR monitoring is essential.",
                    "Not recommended in patients unable to comply with regular INR monitoring."
                },
                Contraindications = new()
                {
                    new() { Condition = "Pregnancy", Severity = SeverityLevel.Absolute,
                        Description = "Teratogenic – warfarin embryopathy, CNS abnormalities, fetal hemorrhage.", Source = "FDA Label" },
                    new() { Condition = "Bleeding Disorder", Severity = SeverityLevel.Absolute,
                        Description = "Active pathological bleeding is an absolute contraindication.", Source = "FDA Label" },
                    new() { Condition = "Hepatic Impairment", Severity = SeverityLevel.Relative,
                        Description = "Impaired synthesis of clotting factors – extreme bleeding risk.", Source = "FDA Label" },
                    new() { Condition = "Peptic Ulcer", Severity = SeverityLevel.Relative,
                        Description = "Risk of GI bleeding.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Tissue necrosis and/or gangrene – skin necrosis risk, especially in protein C or S deficiency.",
                    "Calciphylaxis – painful skin condition reported.",
                    "Numerous drug and food interactions – INR monitoring essential.",
                    "Cranberry juice may enhance warfarin effect."
                },
                UseWithCaution = new()
                {
                    "Elderly patients (increased sensitivity and fall risk)",
                    "Patients with renal impairment",
                    "Patients on multiple interacting medications",
                    "Thyroid disease (hyper/hypothyroidism alters warfarin sensitivity)"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "aspirin", InteractingDrugName = "Aspirin", Severity = InteractionSeverity.Major,
                        Effect = "Markedly increased bleeding risk", Mechanism = "Antiplatelet + anticoagulant synergy; GI erosion",
                        ClinicalManagement = "Avoid unless specifically indicated. Use lowest aspirin dose if combined." },
                    new() { InteractingDrugId = "ibuprofen", InteractingDrugName = "Ibuprofen", Severity = InteractionSeverity.Major,
                        Effect = "Increased GI bleeding and overall bleeding risk", Mechanism = "NSAID GI erosion + anticoagulant",
                        ClinicalManagement = "AVOID combination. Use acetaminophen for pain instead." },
                    new() { InteractingDrugId = "metronidazole", InteractingDrugName = "Metronidazole", Severity = InteractionSeverity.Major,
                        Effect = "Increased INR and bleeding risk", Mechanism = "CYP2C9 inhibition",
                        ClinicalManagement = "Reduce warfarin dose; monitor INR closely." },
                    new() { InteractingDrugId = "amiodarone", InteractingDrugName = "Amiodarone", Severity = InteractionSeverity.Major,
                        Effect = "Markedly increased INR", Mechanism = "CYP2C9 and CYP3A4 inhibition",
                        ClinicalManagement = "Reduce warfarin dose by 30-50% when amiodarone is added." },
                },
                SideEffects = new() { "Bleeding", "Bruising", "Skin necrosis (rare)", "Alopecia" }
            },

            // ============================================================
            // NSAIDs
            // ============================================================
            new Drug
            {
                DrugId = "ibuprofen",
                GenericName = "Ibuprofen",
                BrandNames = new() { "Advil", "Motrin", "Nurofen" },
                DrugClass = "NSAID",
                Category = "Anti-inflammatory / Analgesic",
                Indications = new() { "Pain", "Fever", "Inflammation", "Arthritis", "Dysmenorrhea" },
                AllergyGroups = new() { "NSAID" },
                BlackBoxWarnings = new()
                {
                    "CARDIOVASCULAR THROMBOTIC EVENTS: NSAIDs increase the risk of serious cardiovascular thrombotic events, MI, and stroke. Risk increases with duration of use and in patients with CV disease.",
                    "GI BLEEDING: NSAIDs increase the risk of serious GI adverse events including bleeding, ulceration, and perforation, which can be fatal."
                },
                Contraindications = new()
                {
                    new() { Condition = "Peptic Ulcer", Severity = SeverityLevel.Absolute,
                        Description = "Active GI bleeding or ulceration is an absolute contraindication.", Source = "FDA Black Box" },
                    new() { Condition = "Renal Failure", Severity = SeverityLevel.Relative,
                        Description = "Can cause acute kidney injury; avoid in advanced CKD.", Source = "FDA Label" },
                    new() { Condition = "Heart Failure", Severity = SeverityLevel.Relative,
                        Description = "Fluid retention and edema; can worsen heart failure.", Source = "FDA Label" },
                    new() { Condition = "Pregnancy", Severity = SeverityLevel.Absolute,
                        Description = "Avoid in 3rd trimester – premature closure of ductus arteriosus. Avoid ≥20 weeks – oligohydramnios.", Source = "FDA Label" },
                    new() { Condition = "Bleeding Disorder", Severity = SeverityLevel.Relative,
                        Description = "Inhibits platelet aggregation; increases bleeding risk.", Source = "FDA Label" },
                    new() { Condition = "Asthma", Severity = SeverityLevel.Conditional,
                        Description = "Aspirin-exacerbated respiratory disease (AERD/Samter's Triad). Some asthmatics are NSAID-sensitive.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Serious skin reactions (SJS, TEN) – discontinue at first sign of rash.",
                    "Hepatotoxicity: Elevations of liver enzymes; rare cases of severe hepatic reactions.",
                    "Anemia – may occur due to occult GI blood loss.",
                    "Hypertension – NSAIDs can increase blood pressure."
                },
                UseWithCaution = new()
                {
                    "Elderly (increased GI bleeding risk)",
                    "Concurrent anticoagulant use",
                    "Patients with H. pylori infection",
                    "Dehydrated patients (renal risk)",
                    "Asthma patients with aspirin sensitivity"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Major,
                        Effect = "Markedly increased bleeding risk", Mechanism = "NSAID GI erosion + anticoagulant",
                        ClinicalManagement = "AVOID. Use acetaminophen instead." },
                    new() { InteractingDrugId = "lisinopril", InteractingDrugName = "Lisinopril", Severity = InteractionSeverity.Moderate,
                        Effect = "Reduced antihypertensive effect; renal impairment risk", Mechanism = "Prostaglandin inhibition",
                        ClinicalManagement = "Monitor BP and renal function." },
                    new() { InteractingDrugId = "aspirin", InteractingDrugName = "Aspirin", Severity = InteractionSeverity.Moderate,
                        Effect = "Increased GI bleeding; ibuprofen may block cardioprotective aspirin effect", Mechanism = "COX-1 binding competition",
                        ClinicalManagement = "Take aspirin 30 min before ibuprofen." },
                    new() { InteractingDrugId = "methotrexate", InteractingDrugName = "Methotrexate", Severity = InteractionSeverity.Major,
                        Effect = "Methotrexate toxicity", Mechanism = "Reduced renal clearance",
                        ClinicalManagement = "Avoid in high-dose methotrexate. Monitor levels." },
                    new() { InteractingDrugId = "lithium", InteractingDrugName = "Lithium", Severity = InteractionSeverity.Major,
                        Effect = "Lithium toxicity", Mechanism = "Reduced renal clearance",
                        ClinicalManagement = "Monitor lithium levels closely." },
                },
                SideEffects = new() { "GI upset", "Nausea", "Dizziness", "Edema", "Headache", "GI bleeding" }
            },

            // ============================================================
            // DIABETES
            // ============================================================
            new Drug
            {
                DrugId = "metformin",
                GenericName = "Metformin",
                BrandNames = new() { "Glucophage", "Fortamet", "Riomet" },
                DrugClass = "Biguanide",
                Category = "Antidiabetic",
                Indications = new() { "Type 2 Diabetes Mellitus" },
                AllergyGroups = new() { "Biguanide" },
                BlackBoxWarnings = new()
                {
                    "LACTIC ACIDOSIS: Metformin can cause lactic acidosis, a rare but serious complication. Risk increases with renal impairment, sepsis, dehydration, excess alcohol, hepatic insufficiency, and use of iodinated contrast agents."
                },
                Contraindications = new()
                {
                    new() { Condition = "Renal Failure", Severity = SeverityLevel.Absolute,
                        Description = "Contraindicated if eGFR <30 mL/min. Not recommended if eGFR 30-45. Risk of lactic acidosis.", Source = "FDA Black Box" },
                    new() { Condition = "Hepatic Impairment", Severity = SeverityLevel.Absolute,
                        Description = "Impaired lactate clearance increases lactic acidosis risk.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Hold before and 48 hours after iodinated contrast procedures.",
                    "Monitor vitamin B12 levels – long-term use can cause deficiency.",
                    "Withhold in surgery, sepsis, or conditions causing dehydration.",
                    "Alcohol increases risk of lactic acidosis."
                },
                UseWithCaution = new()
                {
                    "Elderly patients (assess renal function regularly)",
                    "Patients with heart failure",
                    "Concurrent use of nephrotoxic drugs",
                    "Patients who drink alcohol regularly"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "ciprofloxacin", InteractingDrugName = "Ciprofloxacin", Severity = InteractionSeverity.Moderate,
                        Effect = "Altered blood glucose control", Mechanism = "Fluoroquinolones can cause dysglycemia",
                        ClinicalManagement = "Monitor blood glucose closely." },
                },
                SideEffects = new() { "Diarrhea", "Nausea", "Flatulence", "Abdominal discomfort", "Metallic taste" }
            },

            // ============================================================
            // STATINS
            // ============================================================
            new Drug
            {
                DrugId = "simvastatin",
                GenericName = "Simvastatin",
                BrandNames = new() { "Zocor" },
                DrugClass = "HMG-CoA Reductase Inhibitor (Statin)",
                Category = "Lipid Lowering",
                Indications = new() { "Hyperlipidemia", "Cardiovascular risk reduction" },
                AllergyGroups = new() { "Statin" },
                BlackBoxWarnings = new(),
                Contraindications = new()
                {
                    new() { Condition = "Hepatic Impairment", Severity = SeverityLevel.Absolute,
                        Description = "Active liver disease or unexplained persistent elevations of hepatic transaminases.", Source = "FDA Label" },
                    new() { Condition = "Pregnancy", Severity = SeverityLevel.Absolute,
                        Description = "Contraindicated in pregnancy – cholesterol is essential for fetal development.", Source = "FDA Label" },
                    new() { Condition = "Rhabdomyolysis", Severity = SeverityLevel.Absolute,
                        Description = "History of statin-induced rhabdomyolysis.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Myopathy/Rhabdomyolysis – risk increases with higher doses and interacting drugs.",
                    "Do NOT exceed 10 mg/day with amiodarone, verapamil, or diltiazem.",
                    "Do NOT exceed 20 mg/day with amlodipine or ranolazine.",
                    "Monitor LFTs before and during therapy."
                },
                UseWithCaution = new()
                {
                    "Renal impairment (start at lower dose)",
                    "Elderly patients (higher myopathy risk)",
                    "Patients of Chinese descent (higher rosuvastatin/simvastatin levels)",
                    "Hypothyroidism (increased myopathy risk)"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "telithromycin", InteractingDrugName = "Telithromycin", Severity = InteractionSeverity.Major,
                        Effect = "Rhabdomyolysis", Mechanism = "CYP3A4 inhibition massively increases statin levels",
                        ClinicalManagement = "CONTRAINDICATED. Suspend simvastatin during telithromycin therapy." },
                    new() { InteractingDrugId = "amiodarone", InteractingDrugName = "Amiodarone", Severity = InteractionSeverity.Major,
                        Effect = "Increased myopathy/rhabdomyolysis risk", Mechanism = "CYP3A4 inhibition",
                        ClinicalManagement = "Do NOT exceed simvastatin 10 mg/day." },
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Moderate,
                        Effect = "Slightly increased INR", Mechanism = "CYP2C9 interaction",
                        ClinicalManagement = "Monitor INR when starting or changing statin dose." },
                },
                SideEffects = new() { "Myalgia", "Headache", "GI upset", "Elevated LFTs", "Rhabdomyolysis (rare)" }
            },

            new Drug
            {
                DrugId = "atorvastatin",
                GenericName = "Atorvastatin",
                BrandNames = new() { "Lipitor" },
                DrugClass = "HMG-CoA Reductase Inhibitor (Statin)",
                Category = "Lipid Lowering",
                Indications = new() { "Hyperlipidemia", "Cardiovascular risk reduction", "Familial hypercholesterolemia" },
                AllergyGroups = new() { "Statin" },
                BlackBoxWarnings = new(),
                Contraindications = new()
                {
                    new() { Condition = "Hepatic Impairment", Severity = SeverityLevel.Absolute,
                        Description = "Active liver disease or unexplained persistent elevations of hepatic transaminases.", Source = "FDA Label" },
                    new() { Condition = "Pregnancy", Severity = SeverityLevel.Absolute,
                        Description = "Cholesterol synthesis inhibition may harm fetal development.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Myopathy/Rhabdomyolysis risk – especially with CYP3A4 inhibitors.",
                    "Monitor liver function tests.",
                    "Diabetes risk – small increase in HbA1c/fasting glucose."
                },
                UseWithCaution = new()
                {
                    "Concurrent CYP3A4 inhibitors (erythromycin, itraconazole, HIV protease inhibitors)",
                    "Heavy alcohol use",
                    "Renal impairment",
                    "Elderly"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Moderate,
                        Effect = "May slightly increase INR", Mechanism = "CYP interaction",
                        ClinicalManagement = "Monitor INR when initiating or adjusting atorvastatin." },
                    new() { InteractingDrugId = "digoxin", InteractingDrugName = "Digoxin", Severity = InteractionSeverity.Minor,
                        Effect = "Slight increase in digoxin levels", Mechanism = "P-glycoprotein interaction",
                        ClinicalManagement = "Monitor digoxin levels." },
                },
                SideEffects = new() { "Myalgia", "Arthralgia", "Nasopharyngitis", "Diarrhea", "Elevated LFTs" }
            },

            // ============================================================
            // ANALGESICS / OPIOIDS
            // ============================================================
            new Drug
            {
                DrugId = "tramadol",
                GenericName = "Tramadol",
                BrandNames = new() { "Ultram", "ConZip" },
                DrugClass = "Opioid Analgesic",
                Category = "Analgesic",
                Indications = new() { "Moderate to moderately severe pain" },
                AllergyGroups = new() { "Opioid" },
                BlackBoxWarnings = new()
                {
                    "ADDICTION, ABUSE, AND MISUSE: Tramadol exposes users to risks of addiction, abuse, and misuse, which can lead to overdose and death.",
                    "RESPIRATORY DEPRESSION: Serious, life-threatening, or fatal respiratory depression may occur.",
                    "NEONATAL OPIOID WITHDRAWAL SYNDROME if used during pregnancy.",
                    "INTERACTION WITH BENZODIAZEPINES OR CNS DEPRESSANTS: Concurrent use increases risk of respiratory depression, coma, and death.",
                    "Ultra-rapid metabolizers of CYP2D6 may have life-threateningly high tramadol levels."
                },
                Contraindications = new()
                {
                    new() { Condition = "Epilepsy", Severity = SeverityLevel.Relative,
                        Description = "Tramadol lowers seizure threshold significantly.", Source = "FDA Label" },
                    new() { Condition = "Renal Failure", Severity = SeverityLevel.Relative,
                        Description = "Dose adjustment required; accumulation risk with severe renal impairment.", Source = "FDA Label" },
                    new() { Condition = "Hepatic Impairment", Severity = SeverityLevel.Relative,
                        Description = "Reduced metabolism; use lower doses.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "SEIZURE RISK – especially with SSRIs, SNRIs, TCAs, MAOIs, neuroleptics.",
                    "Serotonin syndrome when combined with serotonergic drugs.",
                    "Suicide risk – do not prescribe to suicidal or addiction-prone patients.",
                    "Not recommended for children <12 years or post-tonsillectomy/adenoidectomy in children <18."
                },
                UseWithCaution = new()
                {
                    "Elderly or debilitated patients",
                    "Patients taking serotonergic medications (SSRIs, SNRIs)",
                    "Head injury or increased intracranial pressure",
                    "History of substance abuse"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "sertraline", InteractingDrugName = "Sertraline", Severity = InteractionSeverity.Major,
                        Effect = "Serotonin syndrome; increased seizure risk", Mechanism = "Both increase serotonin; reduced seizure threshold",
                        ClinicalManagement = "Avoid combination if possible. Monitor for serotonin syndrome symptoms." },
                    new() { InteractingDrugId = "carbamazepine", InteractingDrugName = "Carbamazepine", Severity = InteractionSeverity.Major,
                        Effect = "Reduced tramadol efficacy; increased seizure risk", Mechanism = "CYP3A4 induction; additive seizure threshold reduction",
                        ClinicalManagement = "Avoid combination." },
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Moderate,
                        Effect = "Increased INR and bleeding risk", Mechanism = "Unknown",
                        ClinicalManagement = "Monitor INR closely." },
                },
                SideEffects = new() { "Nausea", "Dizziness", "Constipation", "Headache", "Drowsiness", "Seizures (rare)" }
            },

            // ============================================================
            // PSYCHIATRIC
            // ============================================================
            new Drug
            {
                DrugId = "sertraline",
                GenericName = "Sertraline",
                BrandNames = new() { "Zoloft" },
                DrugClass = "SSRI",
                Category = "Antidepressant",
                Indications = new() { "Major Depressive Disorder", "OCD", "PTSD", "Panic disorder", "Social anxiety disorder" },
                AllergyGroups = new() { "SSRI" },
                BlackBoxWarnings = new()
                {
                    "SUICIDALITY: Antidepressants increase the risk of suicidal thinking and behavior in children, adolescents, and young adults (18-24). Monitor closely for clinical worsening and suicidality."
                },
                Contraindications = new()
                {
                    new() { Condition = "MAO Inhibitor Use", Severity = SeverityLevel.Absolute,
                        Description = "Do NOT use within 14 days of MAOIs – risk of fatal serotonin syndrome.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Serotonin syndrome – especially with other serotonergic drugs, MAOIs, tramadol, triptans.",
                    "Increased bleeding risk – especially with NSAIDs, aspirin, anticoagulants.",
                    "Activation of mania/hypomania in bipolar patients.",
                    "Hyponatremia (SIADH) – especially in elderly.",
                    "Abrupt discontinuation may cause withdrawal symptoms."
                },
                UseWithCaution = new()
                {
                    "Hepatic impairment (lower doses)",
                    "Elderly (hyponatremia risk)",
                    "Patients with bleeding disorders",
                    "Patients with bipolar disorder (risk of manic switch)",
                    "Patients with epilepsy (may lower seizure threshold)"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "tramadol", InteractingDrugName = "Tramadol", Severity = InteractionSeverity.Major,
                        Effect = "Serotonin syndrome; seizure risk", Mechanism = "Additive serotonergic effects",
                        ClinicalManagement = "Avoid combination." },
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Moderate,
                        Effect = "Increased bleeding risk", Mechanism = "Serotonin depletion in platelets + CYP2C9 interaction",
                        ClinicalManagement = "Monitor INR closely." },
                    new() { InteractingDrugId = "ibuprofen", InteractingDrugName = "Ibuprofen", Severity = InteractionSeverity.Moderate,
                        Effect = "Increased GI bleeding risk", Mechanism = "Both impair platelet function",
                        ClinicalManagement = "Use gastroprotection (PPI) if combination needed." },
                },
                SideEffects = new() { "Nausea", "Diarrhea", "Insomnia", "Dizziness", "Sexual dysfunction", "Dry mouth" }
            },

            // ============================================================
            // CORTICOSTEROIDS
            // ============================================================
            new Drug
            {
                DrugId = "prednisone",
                GenericName = "Prednisone",
                BrandNames = new() { "Deltasone", "Rayos" },
                DrugClass = "Corticosteroid",
                Category = "Anti-inflammatory / Immunosuppressant",
                Indications = new() { "Asthma exacerbation", "COPD exacerbation", "Autoimmune diseases", "Allergic reactions", "Inflammatory conditions" },
                AllergyGroups = new() { "Corticosteroid" },
                BlackBoxWarnings = new(),
                Contraindications = new()
                {
                    new() { Condition = "Systemic Fungal Infection", Severity = SeverityLevel.Absolute,
                        Description = "Immunosuppression worsens systemic fungal infections.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Adrenal suppression – do NOT stop abruptly after prolonged use. Taper.",
                    "Immunosuppression – increased susceptibility to infections.",
                    "Hyperglycemia – can worsen or unmask diabetes.",
                    "Osteoporosis with long-term use – consider calcium/vitamin D/bisphosphonates.",
                    "Psychiatric effects: euphoria, insomnia, mood swings, psychosis.",
                    "Peptic ulcer risk – especially with concurrent NSAIDs.",
                    "Cataracts and glaucoma with prolonged use.",
                    "Growth suppression in children."
                },
                UseWithCaution = new()
                {
                    "Diabetes mellitus (hyperglycemia risk)",
                    "Hypertension (fluid retention)",
                    "Osteoporosis (bone density loss)",
                    "Peptic ulcer disease",
                    "Glaucoma",
                    "Heart failure (sodium retention)"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "ibuprofen", InteractingDrugName = "Ibuprofen", Severity = InteractionSeverity.Major,
                        Effect = "Significantly increased GI bleeding/ulceration risk", Mechanism = "Additive GI toxicity",
                        ClinicalManagement = "Avoid combination; use PPI if necessary." },
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Moderate,
                        Effect = "Altered anticoagulant effect", Mechanism = "Corticosteroids affect clotting factor synthesis",
                        ClinicalManagement = "Monitor INR closely." },
                    new() { InteractingDrugId = "metformin", InteractingDrugName = "Metformin", Severity = InteractionSeverity.Moderate,
                        Effect = "Hyperglycemia antagonizes metformin", Mechanism = "Glucocorticoid-induced insulin resistance",
                        ClinicalManagement = "Monitor blood glucose; may need increased metformin dose or insulin." },
                },
                SideEffects = new() { "Weight gain", "Insomnia", "Mood changes", "Hyperglycemia", "Increased appetite", "Moon face" }
            },

            // ============================================================
            // ANTIEPILEPTICS
            // ============================================================
            new Drug
            {
                DrugId = "carbamazepine",
                GenericName = "Carbamazepine",
                BrandNames = new() { "Tegretol", "Carbatrol", "Equetro" },
                DrugClass = "Anticonvulsant",
                Category = "Antiepileptic",
                Indications = new() { "Epilepsy", "Trigeminal neuralgia", "Bipolar disorder" },
                AllergyGroups = new() { "Carbamazepine", "Aromatic Anticonvulsant" },
                BlackBoxWarnings = new()
                {
                    "SERIOUS DERMATOLOGIC REACTIONS: Stevens-Johnson Syndrome (SJS) and Toxic Epidermal Necrolysis (TEN). Patients with HLA-B*1502 allele (common in Asian ancestry) are at significantly higher risk. Test BEFORE starting.",
                    "APLASTIC ANEMIA AND AGRANULOCYTOSIS: Rare but potentially fatal blood dyscrasias."
                },
                Contraindications = new()
                {
                    new() { Condition = "Bone Marrow Depression", Severity = SeverityLevel.Absolute,
                        Description = "History of bone marrow depression.", Source = "FDA Label" },
                    new() { Condition = "MAO Inhibitor Use", Severity = SeverityLevel.Absolute,
                        Description = "Do NOT use within 14 days of MAOIs.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "HLA-B*1502 testing recommended in patients of Asian descent before starting.",
                    "CBC monitoring recommended – risk of aplastic anemia, agranulocytosis.",
                    "Hepatotoxicity – monitor LFTs.",
                    "Hyponatremia (SIADH).",
                    "Suicidal behavior/ideation – monitor all anticonvulsant patients.",
                    "Potent CYP3A4 inducer – many drug interactions."
                },
                UseWithCaution = new()
                {
                    "Hepatic impairment",
                    "Renal impairment",
                    "Cardiac conduction disturbances",
                    "Mixed seizure disorders (may worsen absence seizures)"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Major,
                        Effect = "Decreased warfarin effect", Mechanism = "CYP3A4 induction increases warfarin metabolism",
                        ClinicalManagement = "Increase warfarin dose; monitor INR closely." },
                    new() { InteractingDrugId = "simvastatin", InteractingDrugName = "Simvastatin", Severity = InteractionSeverity.Major,
                        Effect = "Reduced statin efficacy", Mechanism = "CYP3A4 induction",
                        ClinicalManagement = "Consider higher statin dose or switch to non-CYP3A4 statin." },
                    new() { InteractingDrugId = "tramadol", InteractingDrugName = "Tramadol", Severity = InteractionSeverity.Major,
                        Effect = "Reduced tramadol efficacy; seizure risk", Mechanism = "CYP3A4 induction + additive seizure threshold lowering",
                        ClinicalManagement = "Avoid combination." },
                },
                SideEffects = new() { "Dizziness", "Drowsiness", "Nausea", "Ataxia", "Diplopia", "Rash" }
            },

            // ============================================================
            // ANTICOAGULANTS (additional)
            // ============================================================
            new Drug
            {
                DrugId = "aspirin",
                GenericName = "Aspirin",
                BrandNames = new() { "Bayer", "Ecotrin", "Bufferin" },
                DrugClass = "NSAID / Antiplatelet",
                Category = "Analgesic / Antiplatelet",
                Indications = new() { "Pain", "Fever", "MI prevention", "Stroke prevention", "Anti-inflammatory" },
                AllergyGroups = new() { "NSAID", "Aspirin", "Salicylate" },
                BlackBoxWarnings = new(),
                Contraindications = new()
                {
                    new() { Condition = "Bleeding Disorder", Severity = SeverityLevel.Absolute,
                        Description = "Active bleeding or bleeding diathesis.", Source = "FDA Label" },
                    new() { Condition = "Peptic Ulcer", Severity = SeverityLevel.Absolute,
                        Description = "Active GI ulceration.", Source = "FDA Label" },
                    new() { Condition = "Asthma", Severity = SeverityLevel.Conditional,
                        Description = "Aspirin-exacerbated respiratory disease in susceptible patients.", Source = "FDA Label" },
                    new() { Condition = "Gout", Severity = SeverityLevel.Conditional,
                        Description = "Low-dose aspirin can increase uric acid levels and trigger gout attacks.", Source = "Clinical" },
                },
                Warnings = new()
                {
                    "Reye's Syndrome – do NOT give to children <19 with viral illness.",
                    "GI bleeding risk – use with gastroprotection in high-risk patients.",
                    "Tinnitus at high doses.",
                    "Increased bleeding risk perioperatively – hold 7-10 days before surgery."
                },
                UseWithCaution = new()
                {
                    "Elderly (GI bleeding)",
                    "Renal impairment",
                    "Hepatic impairment",
                    "Concurrent anticoagulant therapy",
                    "G6PD deficiency (hemolytic anemia risk)"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Major,
                        Effect = "Markedly increased bleeding risk", Mechanism = "Antiplatelet + anticoagulant",
                        ClinicalManagement = "Avoid unless specifically indicated (e.g., mechanical valve). Use lowest dose." },
                    new() { InteractingDrugId = "ibuprofen", InteractingDrugName = "Ibuprofen", Severity = InteractionSeverity.Moderate,
                        Effect = "Ibuprofen may block cardioprotective aspirin effect", Mechanism = "COX-1 binding competition",
                        ClinicalManagement = "Take aspirin ≥30 min before ibuprofen." },
                    new() { InteractingDrugId = "methotrexate", InteractingDrugName = "Methotrexate", Severity = InteractionSeverity.Major,
                        Effect = "Methotrexate toxicity", Mechanism = "Reduced renal clearance of methotrexate",
                        ClinicalManagement = "Avoid with high-dose methotrexate." },
                },
                SideEffects = new() { "GI upset", "GI bleeding", "Tinnitus", "Bruising" }
            },

            // ============================================================
            // GI MEDICATIONS
            // ============================================================
            new Drug
            {
                DrugId = "omeprazole",
                GenericName = "Omeprazole",
                BrandNames = new() { "Prilosec", "Losec" },
                DrugClass = "Proton Pump Inhibitor (PPI)",
                Category = "Gastrointestinal",
                Indications = new() { "GERD", "Peptic ulcer", "Zollinger-Ellison syndrome", "H. pylori eradication", "NSAID prophylaxis" },
                AllergyGroups = new() { "PPI" },
                BlackBoxWarnings = new(),
                Contraindications = new(),
                Warnings = new()
                {
                    "Long-term use: increased risk of fractures (hip, wrist, spine).",
                    "Clostridum difficile infection risk with prolonged use.",
                    "Hypomagnesemia with prolonged use – monitor magnesium.",
                    "Vitamin B12 deficiency with long-term use.",
                    "May mask symptoms of gastric cancer.",
                    "Acute interstitial nephritis (rare)."
                },
                UseWithCaution = new()
                {
                    "Long-term use (evaluate need periodically)",
                    "Osteoporosis risk patients",
                    "Hepatic impairment (dose adjustment may be needed)"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "clopidogrel", InteractingDrugName = "Clopidogrel", Severity = InteractionSeverity.Major,
                        Effect = "Reduced antiplatelet effect of clopidogrel", Mechanism = "CYP2C19 inhibition reduces clopidogrel activation",
                        ClinicalManagement = "AVOID omeprazole with clopidogrel. Use pantoprazole instead." },
                    new() { InteractingDrugId = "methotrexate", InteractingDrugName = "Methotrexate", Severity = InteractionSeverity.Moderate,
                        Effect = "Increased methotrexate levels", Mechanism = "Reduced renal clearance",
                        ClinicalManagement = "Consider temporary PPI discontinuation with high-dose methotrexate." },
                },
                SideEffects = new() { "Headache", "Diarrhea", "Nausea", "Abdominal pain", "Flatulence" }
            },

            new Drug
            {
                DrugId = "metoclopramide",
                GenericName = "Metoclopramide",
                BrandNames = new() { "Reglan" },
                DrugClass = "Dopamine Antagonist / Prokinetic",
                Category = "Gastrointestinal",
                Indications = new() { "Gastroparesis", "Nausea/vomiting", "GERD" },
                AllergyGroups = new() { "Dopamine Antagonist" },
                BlackBoxWarnings = new()
                {
                    "TARDIVE DYSKINESIA: Risk increases with duration of treatment and total cumulative dose. Discontinue if signs/symptoms appear. Treatment may be irreversible. Do NOT use for more than 12 weeks."
                },
                Contraindications = new()
                {
                    new() { Condition = "Epilepsy", Severity = SeverityLevel.Relative,
                        Description = "Increases seizure frequency.", Source = "FDA Label" },
                    new() { Condition = "Pheochromocytoma", Severity = SeverityLevel.Absolute,
                        Description = "May cause hypertensive crisis.", Source = "FDA Label" },
                    new() { Condition = "Parkinson", Severity = SeverityLevel.Absolute,
                        Description = "Dopamine antagonism worsens Parkinson symptoms.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Tardive dyskinesia – irreversible movement disorder.",
                    "Neuroleptic malignant syndrome (NMS) – rare but potentially fatal.",
                    "Depression and suicidality.",
                    "Do NOT use >12 weeks unless benefit clearly outweighs tardive dyskinesia risk."
                },
                UseWithCaution = new()
                {
                    "Elderly (higher tardive dyskinesia risk)",
                    "Renal impairment (dose reduction needed)",
                    "Depression",
                    "Hypertension"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "levodopa", InteractingDrugName = "Levodopa", Severity = InteractionSeverity.Major,
                        Effect = "Mutually antagonistic – metoclopramide reduces levodopa effect, levodopa reduces metoclopramide effect",
                        Mechanism = "Opposing dopamine effects",
                        ClinicalManagement = "AVOID combination in Parkinson patients." },
                },
                SideEffects = new() { "Drowsiness", "Fatigue", "Restlessness", "Diarrhea", "Dystonic reactions" }
            },

            // ============================================================
            // ADDITIONAL COMMONLY USED DRUGS
            // ============================================================
            new Drug
            {
                DrugId = "amlodipine",
                GenericName = "Amlodipine",
                BrandNames = new() { "Norvasc" },
                DrugClass = "Calcium Channel Blocker (Dihydropyridine)",
                Category = "Cardiovascular",
                Indications = new() { "Hypertension", "Angina" },
                AllergyGroups = new() { "Calcium Channel Blocker" },
                BlackBoxWarnings = new(),
                Contraindications = new()
                {
                    new() { Condition = "Hypotension", Severity = SeverityLevel.Relative,
                        Description = "Can worsen hypotension.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Peripheral edema (dose-dependent).",
                    "Worsening angina or MI on initiation or dose increase.",
                    "Hepatic impairment – start at lower dose (2.5 mg)."
                },
                UseWithCaution = new()
                {
                    "Heart failure (negative inotropic effect)",
                    "Hepatic impairment",
                    "Elderly (start low, titrate slowly)",
                    "Aortic stenosis"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "simvastatin", InteractingDrugName = "Simvastatin", Severity = InteractionSeverity.Moderate,
                        Effect = "Increased simvastatin levels – myopathy risk", Mechanism = "CYP3A4 interaction",
                        ClinicalManagement = "Do NOT exceed simvastatin 20 mg/day with amlodipine." },
                    new() { InteractingDrugId = "metoprolol", InteractingDrugName = "Metoprolol", Severity = InteractionSeverity.Moderate,
                        Effect = "Additive hypotension and bradycardia", Mechanism = "Additive cardiovascular effects",
                        ClinicalManagement = "Monitor BP and heart rate." },
                },
                SideEffects = new() { "Peripheral edema", "Dizziness", "Flushing", "Palpitations", "Fatigue" }
            },

            new Drug
            {
                DrugId = "digoxin",
                GenericName = "Digoxin",
                BrandNames = new() { "Lanoxin" },
                DrugClass = "Cardiac Glycoside",
                Category = "Cardiovascular",
                Indications = new() { "Heart failure", "Atrial fibrillation" },
                AllergyGroups = new() { "Cardiac Glycoside" },
                BlackBoxWarnings = new(),
                Contraindications = new()
                {
                    new() { Condition = "Hypokalemia", Severity = SeverityLevel.Absolute,
                        Description = "Hypokalemia sensitizes the heart to digitalis toxicity.", Source = "FDA Label" },
                    new() { Condition = "Ventricular Fibrillation", Severity = SeverityLevel.Absolute,
                        Description = "Contraindicated in ventricular fibrillation.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Narrow therapeutic index – toxicity is common and dangerous.",
                    "Toxicity symptoms: nausea, vomiting, visual disturbances (yellow-green halos), arrhythmias.",
                    "Monitor electrolytes – hypokalemia, hypomagnesemia, and hypercalcemia increase toxicity.",
                    "Renal impairment – dose adjustment required (renally eliminated)."
                },
                UseWithCaution = new()
                {
                    "Renal impairment (reduce dose)",
                    "Elderly (reduced clearance)",
                    "Thyroid disease (hypothyroidism increases sensitivity)",
                    "Electrolyte imbalances"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "amiodarone", InteractingDrugName = "Amiodarone", Severity = InteractionSeverity.Major,
                        Effect = "Increased digoxin levels – toxicity risk", Mechanism = "P-glycoprotein inhibition",
                        ClinicalManagement = "Reduce digoxin dose by 50% when adding amiodarone." },
                    new() { InteractingDrugId = "metoprolol", InteractingDrugName = "Metoprolol", Severity = InteractionSeverity.Moderate,
                        Effect = "Additive bradycardia", Mechanism = "Both slow AV conduction",
                        ClinicalManagement = "Monitor heart rate." },
                },
                SideEffects = new() { "Nausea", "Vomiting", "Visual disturbances", "Arrhythmias", "Dizziness" }
            },

            new Drug
            {
                DrugId = "spironolactone",
                GenericName = "Spironolactone",
                BrandNames = new() { "Aldactone" },
                DrugClass = "Potassium-Sparing Diuretic / Aldosterone Antagonist",
                Category = "Cardiovascular / Diuretic",
                Indications = new() { "Heart failure", "Hypertension", "Edema", "Primary aldosteronism", "Ascites" },
                AllergyGroups = new() { "Potassium-Sparing Diuretic" },
                BlackBoxWarnings = new()
                {
                    "TUMORIGENICITY: Has been shown to be tumorigenic in chronic toxicity studies in rats. Use only for indicated conditions."
                },
                Contraindications = new()
                {
                    new() { Condition = "Hyperkalemia", Severity = SeverityLevel.Absolute,
                        Description = "Potassium-sparing effect can cause life-threatening hyperkalemia.", Source = "FDA Label" },
                    new() { Condition = "Renal Failure", Severity = SeverityLevel.Relative,
                        Description = "Anuria or severe renal impairment – hyperkalemia risk.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "HYPERKALEMIA – potentially fatal. Monitor K+ regularly.",
                    "Gynecomastia, breast tenderness (anti-androgenic effect).",
                    "Hyponatremia.",
                    "Metabolic acidosis in hepatic cirrhosis."
                },
                UseWithCaution = new()
                {
                    "Concurrent ACE inhibitor or ARB use (hyperkalemia)",
                    "Elderly (renal function decline)",
                    "Hepatic impairment",
                    "Diabetes (hyperkalemia risk)"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "lisinopril", InteractingDrugName = "Lisinopril", Severity = InteractionSeverity.Major,
                        Effect = "Life-threatening hyperkalemia", Mechanism = "Both increase potassium retention",
                        ClinicalManagement = "If combined, monitor K+ frequently. Start low dose." },
                    new() { InteractingDrugId = "digoxin", InteractingDrugName = "Digoxin", Severity = InteractionSeverity.Moderate,
                        Effect = "Increased digoxin levels", Mechanism = "Reduced renal clearance",
                        ClinicalManagement = "Monitor digoxin levels." },
                },
                SideEffects = new() { "Hyperkalemia", "Gynecomastia", "Dizziness", "GI upset", "Menstrual irregularities" }
            },

            new Drug
            {
                DrugId = "amiodarone",
                GenericName = "Amiodarone",
                BrandNames = new() { "Cordarone", "Pacerone" },
                DrugClass = "Class III Antiarrhythmic",
                Category = "Cardiovascular",
                Indications = new() { "Ventricular fibrillation", "Ventricular tachycardia", "Atrial fibrillation" },
                AllergyGroups = new() { "Amiodarone", "Iodine" },
                BlackBoxWarnings = new()
                {
                    "PULMONARY TOXICITY: Potentially fatal pulmonary toxicity (hypersensitivity pneumonitis or interstitial/alveolar pneumonitis). Monitor with chest X-ray and pulmonary function tests.",
                    "HEPATOTOXICITY: Can cause fatal hepatic injury. Monitor LFTs at baseline and periodically.",
                    "PROARRHYTHMIA: May worsen existing arrhythmias or cause new arrhythmias, including torsades de pointes."
                },
                Contraindications = new()
                {
                    new() { Condition = "Bradycardia", Severity = SeverityLevel.Absolute,
                        Description = "Severe sinus node dysfunction – risk of fatal bradycardia.", Source = "FDA Label" },
                    new() { Condition = "Thyroid", Severity = SeverityLevel.Relative,
                        Description = "Amiodarone contains iodine; can cause hypo- or hyperthyroidism.", Source = "FDA Label" },
                    new() { Condition = "QT Prolongation", Severity = SeverityLevel.Relative,
                        Description = "Can prolong QT interval; risk of torsades de pointes.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Pulmonary toxicity – baseline and periodic chest X-rays and PFTs.",
                    "Hepatotoxicity – monitor LFTs every 6 months.",
                    "Thyroid dysfunction – monitor TSH every 6 months (contains iodine).",
                    "Photosensitivity – blue-gray skin discoloration with chronic use.",
                    "Corneal microdeposits – nearly universal; usually asymptomatic.",
                    "Peripheral neuropathy with long-term use.",
                    "Extremely long half-life (~40-55 days) – effects persist for weeks/months after discontinuation."
                },
                UseWithCaution = new()
                {
                    "Hepatic impairment",
                    "Thyroid disease",
                    "Pulmonary disease",
                    "Concurrent QT-prolonging drugs",
                    "Elderly"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Major,
                        Effect = "Markedly increased INR", Mechanism = "CYP2C9 and CYP3A4 inhibition",
                        ClinicalManagement = "Reduce warfarin dose by 30-50%." },
                    new() { InteractingDrugId = "digoxin", InteractingDrugName = "Digoxin", Severity = InteractionSeverity.Major,
                        Effect = "Digoxin toxicity", Mechanism = "P-glycoprotein inhibition",
                        ClinicalManagement = "Reduce digoxin dose by 50%." },
                    new() { InteractingDrugId = "simvastatin", InteractingDrugName = "Simvastatin", Severity = InteractionSeverity.Major,
                        Effect = "Rhabdomyolysis risk", Mechanism = "CYP3A4 inhibition",
                        ClinicalManagement = "Do NOT exceed simvastatin 10 mg/day." },
                    new() { InteractingDrugId = "metoprolol", InteractingDrugName = "Metoprolol", Severity = InteractionSeverity.Major,
                        Effect = "Severe bradycardia, sinus arrest", Mechanism = "Additive negative chronotropic effect",
                        ClinicalManagement = "Monitor heart rate closely; consider dose reduction." },
                },
                SideEffects = new() { "Pulmonary toxicity", "Thyroid dysfunction", "Hepatotoxicity", "Photosensitivity", "Corneal deposits", "Neuropathy" }
            },

            new Drug
            {
                DrugId = "clopidogrel",
                GenericName = "Clopidogrel",
                BrandNames = new() { "Plavix" },
                DrugClass = "Antiplatelet (P2Y12 Inhibitor)",
                Category = "Antiplatelet",
                Indications = new() { "ACS", "Stroke prevention", "PAD", "Post-PCI stent" },
                AllergyGroups = new() { "Thienopyridine" },
                BlackBoxWarnings = new()
                {
                    "DIMINISHED EFFECTIVENESS IN CYP2C19 POOR METABOLIZERS: These patients have reduced conversion to active metabolite and less platelet inhibition. Consider alternative antiplatelet therapy."
                },
                Contraindications = new()
                {
                    new() { Condition = "Bleeding Disorder", Severity = SeverityLevel.Absolute,
                        Description = "Active pathological bleeding (intracranial hemorrhage, GI bleed).", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Discontinue 5 days before elective surgery.",
                    "TTP (thrombotic thrombocytopenic purpura) reported rarely.",
                    "CYP2C19 poor metabolizers – reduced efficacy; consider genetic testing.",
                    "Cross-reactivity with other thienopyridines (ticlopidine)."
                },
                UseWithCaution = new()
                {
                    "Hepatic impairment (impaired activation)",
                    "Concurrent anticoagulant use (increased bleeding)",
                    "Recent surgery or trauma"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "omeprazole", InteractingDrugName = "Omeprazole", Severity = InteractionSeverity.Major,
                        Effect = "Reduced clopidogrel antiplatelet effect", Mechanism = "CYP2C19 inhibition reduces activation",
                        ClinicalManagement = "AVOID omeprazole. Use pantoprazole instead." },
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Major,
                        Effect = "Markedly increased bleeding risk", Mechanism = "Antiplatelet + anticoagulant",
                        ClinicalManagement = "Use only when clearly indicated; monitor closely." },
                    new() { InteractingDrugId = "aspirin", InteractingDrugName = "Aspirin", Severity = InteractionSeverity.Moderate,
                        Effect = "Increased bleeding risk but often used together therapeutically (DAPT)", Mechanism = "Dual antiplatelet",
                        ClinicalManagement = "Standard DAPT is acceptable post-PCI; use lowest aspirin dose." },
                },
                SideEffects = new() { "Bleeding", "Bruising", "Diarrhea", "Rash", "GI discomfort" }
            },

            new Drug
            {
                DrugId = "levofloxacin",
                GenericName = "Levofloxacin",
                BrandNames = new() { "Levaquin" },
                DrugClass = "Fluoroquinolone Antibiotic",
                Category = "Antibiotic",
                Indications = new() { "Pneumonia", "UTI", "Sinusitis", "Skin infections", "Anthrax" },
                AllergyGroups = new() { "Fluoroquinolone" },
                BlackBoxWarnings = new()
                {
                    "Fluoroquinolones are associated with disabling and potentially irreversible serious adverse reactions including tendinitis/tendon rupture, peripheral neuropathy, and CNS effects.",
                    "Fluoroquinolones may exacerbate muscle weakness in patients with Myasthenia Gravis. AVOID.",
                    "Increased risk of aortic dissection and aortic aneurysm."
                },
                Contraindications = new()
                {
                    new() { Condition = "Myasthenia Gravis", Severity = SeverityLevel.Absolute,
                        Description = "May exacerbate muscle weakness. Black box warning.", Source = "FDA Black Box" },
                    new() { Condition = "QT Prolongation", Severity = SeverityLevel.Relative,
                        Description = "Risk of QTc prolongation.", Source = "FDA Label" },
                    new() { Condition = "Epilepsy", Severity = SeverityLevel.Relative,
                        Description = "Lowers seizure threshold.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Tendon rupture risk – especially in elderly, those on corticosteroids, or post-transplant.",
                    "Peripheral neuropathy – may be irreversible.",
                    "C. difficile-associated diarrhea.",
                    "Photosensitivity.",
                    "Dysglycemia – monitor blood glucose in diabetic patients."
                },
                UseWithCaution = new()
                {
                    "Elderly (tendon rupture)",
                    "Renal impairment (dose adjustment)",
                    "Diabetes (dysglycemia)",
                    "Concurrent corticosteroid use"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "warfarin", InteractingDrugName = "Warfarin", Severity = InteractionSeverity.Major,
                        Effect = "Increased INR and bleeding risk", Mechanism = "Altered vitamin K metabolism",
                        ClinicalManagement = "Monitor INR closely." },
                },
                SideEffects = new() { "Nausea", "Diarrhea", "Headache", "Insomnia", "Dizziness" }
            },

            new Drug
            {
                DrugId = "methotrexate",
                GenericName = "Methotrexate",
                BrandNames = new() { "Trexall", "Rheumatrex", "Otrexup" },
                DrugClass = "Antimetabolite / DMARD",
                Category = "Immunosuppressant / Antineoplastic",
                Indications = new() { "Rheumatoid arthritis", "Psoriasis", "Cancer (various)", "Ectopic pregnancy" },
                AllergyGroups = new() { "Methotrexate", "Antimetabolite" },
                BlackBoxWarnings = new()
                {
                    "HEPATOTOXICITY: Chronic use can cause hepatic fibrosis and cirrhosis. Monitor LFTs.",
                    "BONE MARROW SUPPRESSION: Myelosuppression, aplastic anemia, leukopenia, thrombocytopenia.",
                    "PULMONARY TOXICITY: Potentially fatal pneumonitis.",
                    "RENAL TOXICITY: Can cause severe nephrotoxicity.",
                    "FETAL DEATH/TERATOGENICITY: Absolutely contraindicated in pregnancy. Women must use contraception.",
                    "SERIOUS INFECTIONS: Immunosuppression increases infection risk, including fatal opportunistic infections."
                },
                Contraindications = new()
                {
                    new() { Condition = "Pregnancy", Severity = SeverityLevel.Absolute,
                        Description = "Teratogenic and abortifacient. Absolutely contraindicated.", Source = "FDA Black Box" },
                    new() { Condition = "Hepatic Impairment", Severity = SeverityLevel.Relative,
                        Description = "Increased hepatotoxicity risk; avoid in alcoholism or chronic liver disease.", Source = "FDA Black Box" },
                    new() { Condition = "Renal Failure", Severity = SeverityLevel.Relative,
                        Description = "Renally eliminated; toxicity risk greatly increased with renal impairment.", Source = "FDA Black Box" },
                    new() { Condition = "Bleeding Disorder", Severity = SeverityLevel.Relative,
                        Description = "Bone marrow suppression can worsen bleeding.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "CBC monitoring required – weekly during initiation.",
                    "LFT monitoring required.",
                    "Ensure adequate hydration and alkalization with high-dose therapy.",
                    "Folic acid supplementation recommended to reduce toxicity.",
                    "NEVER administer daily for rheumatic disease – weekly dosing only."
                },
                UseWithCaution = new()
                {
                    "Peptic ulcer disease",
                    "Ulcerative colitis",
                    "Elderly patients",
                    "Debilitated patients",
                    "Active infection"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "ibuprofen", InteractingDrugName = "Ibuprofen", Severity = InteractionSeverity.Major,
                        Effect = "Methotrexate toxicity (bone marrow suppression)", Mechanism = "NSAIDs reduce renal clearance of methotrexate",
                        ClinicalManagement = "Avoid with high-dose MTX. Monitor levels with low-dose MTX." },
                    new() { InteractingDrugId = "amoxicillin", InteractingDrugName = "Amoxicillin", Severity = InteractionSeverity.Major,
                        Effect = "Increased methotrexate toxicity", Mechanism = "Reduced renal clearance",
                        ClinicalManagement = "Monitor MTX levels; consider dose reduction." },
                    new() { InteractingDrugId = "omeprazole", InteractingDrugName = "Omeprazole", Severity = InteractionSeverity.Moderate,
                        Effect = "Increased methotrexate levels", Mechanism = "Reduced renal clearance",
                        ClinicalManagement = "Consider temporary PPI discontinuation with high-dose MTX." },
                },
                SideEffects = new() { "Nausea", "Fatigue", "Mouth sores", "Hepatotoxicity", "Myelosuppression", "Alopecia" }
            },

            new Drug
            {
                DrugId = "lithium",
                GenericName = "Lithium",
                BrandNames = new() { "Lithobid", "Eskalith" },
                DrugClass = "Mood Stabilizer",
                Category = "Psychiatric",
                Indications = new() { "Bipolar disorder", "Mania" },
                AllergyGroups = new() { "Lithium" },
                BlackBoxWarnings = new()
                {
                    "LITHIUM TOXICITY: Narrow therapeutic index. Toxicity can occur at levels close to therapeutic. Facilities for prompt and accurate serum lithium determinations must be available."
                },
                Contraindications = new()
                {
                    new() { Condition = "Renal Failure", Severity = SeverityLevel.Absolute,
                        Description = "Renally eliminated; severe renal impairment leads to accumulation and toxicity.", Source = "FDA Label" },
                    new() { Condition = "Pregnancy", Severity = SeverityLevel.Relative,
                        Description = "Risk of Ebstein's anomaly (cardiac malformation) in first trimester.", Source = "FDA Label" },
                },
                Warnings = new()
                {
                    "Narrow therapeutic index – serum level monitoring essential (0.6-1.2 mEq/L).",
                    "Toxicity signs: tremor, confusion, vomiting, diarrhea, seizures, coma.",
                    "Hypothyroidism and goiter with long-term use – monitor TSH.",
                    "Nephrogenic diabetes insipidus (polyuria, polydipsia).",
                    "Ensure adequate sodium and fluid intake."
                },
                UseWithCaution = new()
                {
                    "Dehydration or sodium restriction (increases lithium levels)",
                    "Thyroid disease",
                    "Cardiovascular disease (Brugada syndrome risk)",
                    "Elderly"
                },
                Interactions = new()
                {
                    new() { InteractingDrugId = "lisinopril", InteractingDrugName = "Lisinopril", Severity = InteractionSeverity.Major,
                        Effect = "Lithium toxicity", Mechanism = "ACE inhibitors reduce lithium clearance",
                        ClinicalManagement = "Monitor lithium levels closely; reduce dose if needed." },
                    new() { InteractingDrugId = "ibuprofen", InteractingDrugName = "Ibuprofen", Severity = InteractionSeverity.Major,
                        Effect = "Lithium toxicity", Mechanism = "NSAIDs reduce renal lithium clearance",
                        ClinicalManagement = "AVOID NSAIDs. Use acetaminophen instead. Monitor lithium levels." },
                },
                SideEffects = new() { "Tremor", "Weight gain", "Polyuria", "Polydipsia", "Nausea", "Hypothyroidism", "Cognitive dulling" }
            },
        };
    }
}
