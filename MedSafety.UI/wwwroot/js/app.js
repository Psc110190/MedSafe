// ═══════════════════════════════════════════════════════════
// MedSafety – Frontend Application Logic
// ═══════════════════════════════════════════════════════════

const API_BASE = window.API_BASE_URL || '/api';

// ── Tag Input Manager ───────────────────────────────────
class TagInput {
    constructor(inputId, tagsContainerId) {
        this.input = document.getElementById(inputId);
        this.container = document.getElementById(tagsContainerId);
        this.values = [];
        this.init();
    }

    init() {
        this.input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' || e.key === ',') {
                e.preventDefault();
                this.addTag(this.input.value.trim());
            }
        });
        // Also add on blur if there's text
        this.input.addEventListener('blur', () => {
            if (this.input.value.trim()) {
                this.addTag(this.input.value.trim());
            }
        });
    }

    addTag(value) {
        if (!value || this.values.includes(value)) return;
        this.values.push(value);
        this.render();
        this.input.value = '';
    }

    removeTag(value) {
        this.values = this.values.filter(v => v !== value);
        this.render();
    }

    render() {
        this.container.innerHTML = this.values.map(v => `
            <span class="tag">
                ${escapeHtml(v)}
                <span class="remove-tag" data-value="${escapeHtml(v)}">&times;</span>
            </span>
        `).join('');

        this.container.querySelectorAll('.remove-tag').forEach(btn => {
            btn.addEventListener('click', () => this.removeTag(btn.dataset.value));
        });
    }

    clear() {
        this.values = [];
        this.render();
        this.input.value = '';
    }

    setValues(vals) {
        this.values = [...vals];
        this.render();
    }
}

// ── Utility Functions ────────────────────────────────────
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

async function apiCall(url, options = {}) {
    try {
        const resp = await fetch(url, {
            headers: { 'Content-Type': 'application/json', ...options.headers },
            ...options
        });
        if (!resp.ok) {
            const err = await resp.text();
            throw new Error(err || `HTTP ${resp.status}`);
        }
        if (resp.status === 204) return null;
        const text = await resp.text();
        return text ? JSON.parse(text) : null;
    } catch (e) {
        console.error('API Error:', e);
        throw e;
    }
}

function showLoading(containerId) {
    const el = document.getElementById(containerId);
    el.style.display = 'block';
    el.innerHTML = '<div class="loading"><div class="spinner"></div>Analyzing medications...</div>';
}

// ── Initialize Tag Inputs ────────────────────────────────
const allergyTags = new TagInput('allergy-input', 'allergy-tags');
const comorbidityTags = new TagInput('comorbidity-input', 'comorbidity-tags');
const complaintTags = new TagInput('complaint-input', 'complaint-tags');
const currentMedTags = new TagInput('current-med-input', 'current-med-tags');
const proposedMedTags = new TagInput('proposed-med-input', 'proposed-med-tags');

const COMPLAINT_SYMPTOM_MAP = [
    { value: 'hypoglycemia', terms: ['hypoglycemia', 'low glucose', 'low blood sugar'] },
    { value: 'dizziness', terms: ['dizziness', 'syncope', 'fainting', 'lightheaded'] },
    { value: 'vomiting', terms: ['vomiting', 'emesis'] },
    { value: 'abdominal pain', terms: ['abdominal pain', 'stomach pain', 'belly pain'] },
    { value: 'fever', terms: ['fever', 'febrile'] },
    { value: 'sore throat', terms: ['sore throat', 'throat pain'] },
    { value: 'dyspnea', terms: ['dyspnea', 'shortness of breath', 'sob'] },
    { value: 'UTI symptoms', terms: ['uti symptoms', 'uti', 'dysuria', 'urinary burning'] },
    { value: 'infection', terms: ['infection'] }
];

const COMPLAINT_RISK_FLAG_MAP = {
    flagNpo: ['npo', 'nil per os'],
    flagPoorIntake: ['poor intake', 'poor oral intake', 'not eating'],
    flagAcuteIllness: ['acute illness', 'acutely ill'],
    flagRecentSurgery: ['recent surgery', 'post-op', 'postoperative'],
    flagDehydration: ['dehydration', 'dehydrated'],
    flagMetabolicAcidosis: ['metabolic acidosis', 'acidosis'],
    flagDka: ['dka', 'diabetic ketoacidosis'],
    flagAki: ['acute kidney injury', 'aki'],
    flagSepsis: ['sepsis', 'septic'],
    flagHypoxia: ['hypoxia', 'hypoxic'],
    flagShock: ['shock'],
    flagAnuria: ['anuria'],
    flagBowelObstruction: ['bowel obstruction'],
    flagUrinaryObstruction: ['urinary obstruction'],
    flagAsthmaCopd: ['asthma/copd', 'asthma', 'copd'],
    flagHeartBlock: ['heart block'],
    flagDecompHf: ['decompensated hf', 'decompensated heart failure'],
    flagCardiacDisease: ['cardiac disease', 'significant cardiac disease'],
    flagRecentMi: ['recent mi', 'unstable angina', 'recent mi/unstable angina'],
    flagAdrenal: ['adrenal insufficiency'],
    flagThyroidCancer: ['mtc history', 'medullary thyroid carcinoma'],
    flagMen2: ['men2 history', 'men2'],
    flagPancreatitis: ['pancreatitis history', 'pancreatitis'],
    flagAngioedema: ['ace/arb angioedema', 'angioedema'],
    flagNeutropenia: ['neutropenia'],
    flagGout: ['gout'],
    flagDigoxin: ['digoxin use', 'digoxin'],
    flagPotassiumSupplement: ['potassium supplement'],
    flagPotassiumSparing: ['k-sparing diuretic', 'potassium-sparing diuretic'],
    flagAliskiren: ['aliskiren use', 'aliskiren'],
    flagAceWithin36: ['ace inhibitor within 36h', 'ace inhibitor within 36 hours'],
    flagHeavyAlcohol: ['heavy alcohol use'],
    flagReducedInsulin: ['reduced insulin', 'recent reduced insulin']
};

const DEMO_PATIENT_SCENARIOS = [
    {
        id: 'hf-arni-transition',
        title: 'HF ACE-to-ARNI Transition',
        patientId: 'DEMO-HF-001',
        focus: 'ACE washout, angioedema, renal and potassium safety',
        badges: ['Must avoid', 'External label', 'Current meds'],
        data: {
            age: 72,
            isPregnant: false,
            isBreastfeeding: false,
            allergies: [],
            comorbidities: ['Heart Failure', 'Hypertension', 'Type 2 diabetes'],
            complaints: ['Decompensated HF', 'ACE inhibitor within 36h', 'Dizziness', 'Potassium supplement'],
            currentMedications: ['Lisinopril'],
            proposedMedications: ['Sacubitril-valsartan'],
            labs: { glucose: 142, eGfr: 38, potassium: 5.4, sodium: 134 },
            vitals: { heartRate: 92, systolicBp: 94 }
        }
    },
    {
        id: 'diabetes-aki-sick-day',
        title: 'Diabetes Sick-Day AKI',
        patientId: 'DEMO-DM-002',
        focus: 'Hypoglycemia, AKI, acidosis and SGLT2 ketoacidosis risk',
        badges: ['Critical hold', 'Sick day', 'Multiple meds'],
        data: {
            age: 67,
            isPregnant: false,
            isBreastfeeding: false,
            allergies: [],
            comorbidities: ['Type 2 diabetes', 'Renal Failure'],
            complaints: ['Acute kidney injury', 'Sepsis', 'Vomiting', 'NPO', 'Poor intake', 'Dehydration', 'DKA'],
            currentMedications: ['Metformin', 'Insulin glargine'],
            proposedMedications: ['Empagliflozin', 'Insulin lispro', 'Glipizide'],
            labs: { glucose: 58, eGfr: 24, potassium: 5.1, sodium: 130 },
            vitals: { heartRate: 118, systolicBp: 88 }
        }
    },
    {
        id: 'thyroid-infection-beta-blocker',
        title: 'Hyperthyroid With Infection',
        patientId: 'DEMO-THY-003',
        focus: 'Methimazole warning plus beta-blocker respiratory/cardiac risk',
        badges: ['Review needed', 'Current med', 'Contra screen'],
        data: {
            age: 44,
            isPregnant: false,
            isBreastfeeding: false,
            allergies: [],
            comorbidities: ['Hyperthyroidism', 'Asthma', 'COPD'],
            complaints: ['Sore throat', 'Fever', 'Asthma/COPD', 'Heart block'],
            currentMedications: ['Methimazole'],
            proposedMedications: ['Propranolol'],
            labs: { glucose: 96, eGfr: 84, potassium: 4.2, sodium: 138 },
            vitals: { heartRate: 54, systolicBp: 108 }
        }
    },
    {
        id: 'glp1-pancreatitis-mtc',
        title: 'GLP-1 Red Flags',
        patientId: 'DEMO-GLP-004',
        focus: 'MTC/MEN2 contraindication and pancreatitis symptom context',
        badges: ['Must avoid', 'Label warning', 'History flags'],
        data: {
            age: 52,
            isPregnant: false,
            isBreastfeeding: false,
            allergies: [],
            comorbidities: ['Type 2 diabetes'],
            complaints: ['Abdominal pain', 'Vomiting', 'Pancreatitis history', 'MTC history', 'MEN2 history'],
            currentMedications: ['Metformin'],
            proposedMedications: ['Semaglutide', 'Sitagliptin'],
            labs: { glucose: 168, eGfr: 62, potassium: 4.0, sodium: 137 },
            vitals: { heartRate: 96, systolicBp: 126 }
        }
    },
    {
        id: 'pregnancy-raas-statin',
        title: 'Pregnancy Medication Review',
        patientId: 'DEMO-OB-005',
        focus: 'Pregnancy-specific RAAS and statin safety checks',
        badges: ['Pregnancy', 'Current meds', 'Must avoid'],
        data: {
            age: 31,
            isPregnant: true,
            isBreastfeeding: false,
            allergies: [],
            comorbidities: ['Hypertension'],
            complaints: ['Pregnancy medication review'],
            currentMedications: ['Lisinopril', 'Atorvastatin'],
            proposedMedications: ['Lisinopril', 'Atorvastatin'],
            labs: { glucose: 92, eGfr: 88, potassium: 4.1, sodium: 136 },
            vitals: { heartRate: 86, systolicBp: 146 }
        }
    }
];

renderDemoScenarios();

// ── Tab Navigation ───────────────────────────────────────
document.querySelectorAll('.tab').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
        document.querySelectorAll('.tab-content').forEach(tc => tc.classList.remove('active'));
        tab.classList.add('active');
        document.getElementById(`${tab.dataset.tab}-tab`).classList.add('active');

        // Load drug list when switching to drug-lookup
        if (tab.dataset.tab === 'drug-lookup') loadDrugList();
        if (tab.dataset.tab === 'rules') loadRules();
    });
});

// ══════════════════════════════════════════════════════════
// SAFETY SCREENING
// ══════════════════════════════════════════════════════════

document.getElementById('screening-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    if (
        proposedMedTags.values.length === 0 &&
        currentMedTags.values.length === 0 &&
        comorbidityTags.values.length === 0 &&
        complaintTags.values.length === 0
    ) {
        alert('Please add at least one condition, current medication, or proposed medication.');
        return;
    }

    const patient = {
        patientId: document.getElementById('patientId').value || null,
        age: document.getElementById('age').value ? parseInt(document.getElementById('age').value) : null,
        isPregnant: document.getElementById('isPregnant').checked,
        isBreastfeeding: document.getElementById('isBreastfeeding').checked,
        allergies: allergyTags.values,
        comorbidities: comorbidityTags.values,
        currentComplaints: complaintTags.values,
        currentMedications: currentMedTags.values,
        proposedMedications: proposedMedTags.values,
        symptoms: getSymptomsFromComplaints(),
        labs: {
            glucose: readNullableNumber('glucose'),
            eGfr: readNullableNumber('egfr'),
            potassium: readNullableNumber('potassium'),
            sodium: readNullableNumber('sodium')
        },
        vitals: {
            heartRate: readNullableNumber('heartRate'),
            systolicBp: readNullableNumber('systolicBp')
        },
        contextFlags: readContextFlags()
    };

    showLoading('screening-results');
    try {
        const result = await apiCall(`${API_BASE}/SafetyScreening/patient-context`, {
            method: 'POST',
            body: JSON.stringify(patient)
        });
        renderPatientContextResults(result);
    } catch (err) {
        document.getElementById('screening-results').innerHTML =
            `<div class="card"><p style="color:var(--danger)">Error: ${escapeHtml(err.message)}</p></div>`;
    }
});

function readNullableNumber(id) {
    const value = document.getElementById(id)?.value;
    return value === '' || value == null ? null : Number(value);
}

function getSymptomsFromComplaints() {
    const complaintText = complaintTags.values.join(' | ').toLowerCase();
    if (!complaintText) return [];

    return COMPLAINT_SYMPTOM_MAP
        .filter(symptom => symptom.terms.some(term => complaintText.includes(term)))
        .map(symptom => symptom.value);
}

function hasComplaintContextTerm(terms = []) {
    const complaintText = complaintTags.values.join(' | ').toLowerCase();
    return terms.some(term => complaintText.includes(term));
}

function readContextFlags() {
    return {
        npo: isChecked('flagNpo'),
        poorIntake: isChecked('flagPoorIntake'),
        acuteIllness: isChecked('flagAcuteIllness'),
        recentSurgery: isChecked('flagRecentSurgery'),
        dehydration: isChecked('flagDehydration'),
        metabolicAcidosis: isChecked('flagMetabolicAcidosis'),
        dka: isChecked('flagDka'),
        acuteKidneyInjury: isChecked('flagAki'),
        sepsis: isChecked('flagSepsis'),
        hypoxia: isChecked('flagHypoxia'),
        shock: isChecked('flagShock'),
        anuria: isChecked('flagAnuria'),
        bowelObstruction: isChecked('flagBowelObstruction'),
        urinaryObstruction: isChecked('flagUrinaryObstruction'),
        asthmaCopd: isChecked('flagAsthmaCopd'),
        heartBlock: isChecked('flagHeartBlock'),
        decompensatedHeartFailure: isChecked('flagDecompHf'),
        cardiacDisease: isChecked('flagCardiacDisease'),
        recentMiOrUnstableAngina: isChecked('flagRecentMi'),
        adrenalInsufficiency: isChecked('flagAdrenal'),
        thyroidCancerHistory: isChecked('flagThyroidCancer'),
        men2History: isChecked('flagMen2'),
        pancreatitisHistory: isChecked('flagPancreatitis'),
        angioedemaAceArbHistory: isChecked('flagAngioedema'),
        neutropenia: isChecked('flagNeutropenia'),
        gout: isChecked('flagGout'),
        digoxinUse: isChecked('flagDigoxin'),
        potassiumSupplement: isChecked('flagPotassiumSupplement'),
        potassiumSparingDiuretic: isChecked('flagPotassiumSparing'),
        aliskirenUse: isChecked('flagAliskiren'),
        lastAceInhibitorWithin36Hours: isChecked('flagAceWithin36'),
        heavyAlcoholUse: isChecked('flagHeavyAlcohol'),
        reducedInsulin: isChecked('flagReducedInsulin')
    };
}

function isChecked(id) {
    return Boolean(document.getElementById(id)?.checked) || hasComplaintContextTerm(COMPLAINT_RISK_FLAG_MAP[id] || []);
}

function renderScreeningResults(result) {
    const container = document.getElementById('screening-results');
    container.style.display = 'block';

    const hasBlackBox = result.hasBlackBoxWarnings;
    const hasAbsolute = result.hasAbsoluteContraindications;
    const icon = hasAbsolute ? '&#9888;' : hasBlackBox ? '&#9888;' : result.totalAlerts > 0 ? '&#9888;' : '&#10004;';
    const summaryClass = hasAbsolute ? 'danger' : hasBlackBox ? 'warning' : result.totalAlerts > 0 ? 'caution' : 'success';

    let html = `
    <div class="result-summary" style="border-left: 5px solid var(--${summaryClass});">
        <div class="summary-icon">${icon}</div>
        <div class="summary-stats">
            <h3>Screening Complete – ${result.drugReports.length} Drug(s) Analyzed</h3>
            <p>Screened at: ${new Date(result.screenedAt).toLocaleString()}
               ${result.patientId ? ` | Patient: ${escapeHtml(result.patientId)}` : ''}</p>
            <div class="stat-row">
                <span class="stat badge-danger">${result.totalAlerts} Total Alerts</span>
                ${hasAbsolute ? '<span class="stat badge-danger">ABSOLUTE CONTRAINDICATIONS FOUND</span>' : ''}
                ${hasBlackBox ? '<span class="stat badge-warning">BLACK BOX WARNINGS</span>' : ''}
            </div>
        </div>
    </div>`;

    for (const report of result.drugReports) {
        html += renderDrugReport(report);
    }

    container.innerHTML = html;
}

function renderPatientContextResults(result) {
    const container = document.getElementById('screening-results');
    container.style.display = 'block';

    const mustAvoidCount = result.mustAvoidMedications?.length || 0;
    const cautionCount = result.useWithCautionMedications?.length || 0;
    const safeCount = result.safeMedications?.length || 0;
    const missingCount = result.missingContext?.length || 0;
    const unrecognizedCount = result.unrecognizedMedications?.length || 0;
    const alertCount = result.alerts?.length || 0;
    const summaryClass = mustAvoidCount > 0 ? 'danger' :
        (cautionCount > 0 || unrecognizedCount > 0 || missingCount > 0) ? 'caution' : 'success';
    const defaultPanel = mustAvoidCount > 0 ? 'avoid' :
        unrecognizedCount > 0 ? 'unrecognized' :
        cautionCount > 0 ? 'caution' :
        alertCount > 0 ? 'alerts' : 'missing';

    let html = `
    <div class="patient-context-result" data-context-result>
    <div class="result-summary context-result-summary" style="border-left: 5px solid var(--${summaryClass});">
        <div class="summary-icon">${mustAvoidCount > 0 ? '&#9888;' : (cautionCount > 0 || unrecognizedCount > 0 || missingCount > 0) ? '&#9432;' : '&#10004;'}</div>
        <div class="summary-stats">
            <h3>Patient Context Safety Check</h3>
            <p>Screened at: ${new Date(result.screenedAt).toLocaleString()}
               ${result.patientId ? ` | Patient: ${escapeHtml(result.patientId)}` : ''}</p>
            <div class="stat-row">
                <span class="stat badge-danger">${mustAvoidCount} Must Avoid</span>
                <span class="stat badge-warning">${cautionCount} Review Needed</span>
                ${unrecognizedCount ? `<span class="stat badge-warning">${unrecognizedCount} Unrecognized</span>` : ''}
                ${missingCount ? `<span class="stat badge-warning">${missingCount} Missing Context</span>` : ''}
            </div>
        </div>
        <button type="button" class="context-collapse-toggle" data-context-collapse-toggle
                aria-expanded="true" aria-controls="patient-context-detail-body">
            <span class="collapse-label">Collapse</span>
            <span class="collapse-icon">&#8722;</span>
        </button>
    </div>`;

    html += '<div class="patient-context-collapse-body" id="patient-context-detail-body" data-context-collapse-body>';

    html += `
    <section class="detailed-results-section" data-detailed-results>
        <div class="detailed-results-header">
            <div>
                <h3>Detailed Results</h3>
                <p>Category tiles, rule details, context gaps, and verification notes.</p>
            </div>
            <button type="button" class="context-collapse-toggle detailed-results-collapse-toggle"
                    data-detailed-results-collapse-toggle aria-expanded="true"
                    aria-controls="detailed-results-body">
                <span class="collapse-label">Collapse</span>
                <span class="collapse-icon">&#8722;</span>
            </button>
        </div>
        <div class="detailed-results-body" id="detailed-results-body" data-detailed-results-collapse-body>
            ${buildNoRuleHitNote(result.safeMedications)}
    <div class="context-tile-dashboard" data-active-panel="${defaultPanel}">
        ${buildCategoryTile('avoid', 'Must Avoid', mustAvoidCount, 'Critical conflicts', 'tile-danger', '&#128683;', defaultPanel)}
        ${buildCategoryTile('caution', 'Review Needed', cautionCount, 'Caution or monitoring', 'tile-caution', '&#9432;', defaultPanel)}
        ${buildCategoryTile('alerts', 'All Alerts', alertCount, 'Rule details', 'tile-warning', '&#9888;', defaultPanel)}
        ${buildCategoryTile('unrecognized', 'Unrecognized', unrecognizedCount, 'Verify names', unrecognizedCount ? 'tile-caution' : 'tile-safe', unrecognizedCount ? '&#10067;' : '&#10004;', defaultPanel)}
        ${buildCategoryTile(
            'missing',
            missingCount > 0 ? 'Missing Context' : 'Context Complete',
            missingCount,
            missingCount > 0 ? 'Needed data' : 'Required data present',
            missingCount > 0 ? 'tile-info' : 'tile-safe',
            missingCount > 0 ? '&#8943;' : '&#10004;',
            defaultPanel
        )}
    </div>

    <div class="context-detail-shell">
        <section class="context-detail-panel ${defaultPanel === 'avoid' ? 'active' : ''}" data-panel="avoid">
            ${buildMedicationPanelContent('Must Avoid Medications', result.mustAvoidMedications, 'tile-danger', 'badge-danger', result.alerts)}
        </section>
        <section class="context-detail-panel ${defaultPanel === 'caution' ? 'active' : ''}" data-panel="caution">
            ${buildMedicationPanelContent('Review Needed Medications', result.useWithCautionMedications, 'tile-caution', 'badge-warning', result.alerts)}
        </section>
        <section class="context-detail-panel ${defaultPanel === 'alerts' ? 'active' : ''}" data-panel="alerts">
            ${buildAlertPanelContent('Warnings and Alerts', result.alerts)}
        </section>
        <section class="context-detail-panel ${defaultPanel === 'unrecognized' ? 'active' : ''}" data-panel="unrecognized">
            ${buildUnrecognizedPanelContent(result.unrecognizedMedications)}
        </section>
        <section class="context-detail-panel ${defaultPanel === 'missing' ? 'active' : ''}" data-panel="missing">
            ${buildMissingPanelContent(result.missingContext)}
        </section>
    </div></div></section>`;

    html += buildClinicianActionPlan(result);
    html += '</div></div>';

    container.innerHTML = html;
    setupPatientContextCollapse(container);
    setupActionPlanCollapse(container);
    setupPriorityActionPlanTiles(container);
    setupDetailedResultsCollapse(container);
    setupContextTiles(container);
    container.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

function setupPatientContextCollapse(container) {
    const resultShell = container.querySelector('[data-context-result]');
    const toggle = container.querySelector('[data-context-collapse-toggle]');
    const body = container.querySelector('[data-context-collapse-body]');
    if (!resultShell || !toggle || !body) return;

    const label = toggle.querySelector('.collapse-label');
    const icon = toggle.querySelector('.collapse-icon');

    const setCollapsed = (collapsed) => {
        resultShell.classList.toggle('is-collapsed', collapsed);
        body.hidden = collapsed;
        toggle.setAttribute('aria-expanded', String(!collapsed));
        if (label) label.textContent = collapsed ? 'Expand' : 'Collapse';
        if (icon) icon.innerHTML = collapsed ? '&#43;' : '&#8722;';
    };

    toggle.addEventListener('click', () => {
        setCollapsed(!resultShell.classList.contains('is-collapsed'));
    });
}

function setupActionPlanCollapse(container) {
    const actionPlan = container.querySelector('[data-action-plan]');
    const toggle = container.querySelector('[data-action-plan-collapse-toggle]');
    const body = container.querySelector('[data-action-plan-collapse-body]');
    if (!actionPlan || !toggle || !body) return;

    const label = toggle.querySelector('.collapse-label');
    const icon = toggle.querySelector('.collapse-icon');

    const setCollapsed = (collapsed) => {
        actionPlan.classList.toggle('is-collapsed', collapsed);
        body.hidden = collapsed;
        toggle.setAttribute('aria-expanded', String(!collapsed));
        if (label) label.textContent = collapsed ? 'Expand' : 'Collapse';
        if (icon) icon.innerHTML = collapsed ? '&#43;' : '&#8722;';
    };

    toggle.addEventListener('click', () => {
        setCollapsed(!actionPlan.classList.contains('is-collapsed'));
    });
}

function setupPriorityActionPlanTiles(container) {
    const actionPlan = container.querySelector('[data-action-plan]');
    const tiles = container.querySelectorAll('[data-action-plan-panel-target]');
    const panels = container.querySelectorAll('[data-action-plan-panel]');
    if (!actionPlan || !tiles.length || !panels.length) return;

    tiles.forEach(tile => {
        tile.addEventListener('click', () => {
            const target = tile.dataset.actionPlanPanelTarget;
            actionPlan.dataset.activeActionPanel = target;
            tiles.forEach(item => item.classList.toggle('active', item === tile));
            panels.forEach(panel => {
                panel.classList.toggle('active', panel.dataset.actionPlanPanel === target);
            });
        });
    });
}

function setupDetailedResultsCollapse(container) {
    const details = container.querySelector('[data-detailed-results]');
    const toggle = container.querySelector('[data-detailed-results-collapse-toggle]');
    const body = container.querySelector('[data-detailed-results-collapse-body]');
    if (!details || !toggle || !body) return;

    const label = toggle.querySelector('.collapse-label');
    const icon = toggle.querySelector('.collapse-icon');

    const setCollapsed = (collapsed) => {
        details.classList.toggle('is-collapsed', collapsed);
        body.hidden = collapsed;
        toggle.setAttribute('aria-expanded', String(!collapsed));
        if (label) label.textContent = collapsed ? 'Expand' : 'Collapse';
        if (icon) icon.innerHTML = collapsed ? '&#43;' : '&#8722;';
    };

    toggle.addEventListener('click', () => {
        setCollapsed(!details.classList.contains('is-collapsed'));
    });
}

function buildCategoryTile(id, title, count, subtitle, tileClass, icon, activePanel) {
    const active = id === activePanel ? 'active' : '';
    return `
    <button type="button" class="context-category-tile ${tileClass} ${active}" data-panel-target="${escapeHtml(id)}">
        <span class="tile-icon">${icon}</span>
        <span class="tile-count">${count}</span>
        <span class="tile-title">${escapeHtml(title)}</span>
        <span class="tile-subtitle">${escapeHtml(subtitle)}</span>
    </button>`;
}

function buildClinicianActionPlan(result) {
    const alerts = [...(result.alerts || [])]
        .filter(alert => !isUnrecognizedAlert(alert))
        .sort((a, b) => getAlertPriority(a) - getAlertPriority(b));
    const actionAlerts = buildPriorityActionGroups(alerts).slice(0, 4);
    const criticalActionGroups = actionAlerts.filter(group => group.alerts.some(alert => alert.level === 'Critical'));
    const highActionGroups = actionAlerts.filter(group => !group.alerts.some(alert => alert.level === 'Critical'));
    const missingItems = (result.missingContext || []).slice(0, 3);
    const unrecognizedItems = (result.unrecognizedMedications || []).slice(0, 3);
    const totalActions = criticalActionGroups.length + highActionGroups.length + missingItems.length + unrecognizedItems.length;
    const defaultActionPanel = criticalActionGroups.length ? 'critical' :
        highActionGroups.length ? 'review' :
        missingItems.length ? 'missing' : 'verify';

    let html = `
    <section class="clinician-action-plan" data-action-plan>
        <div class="action-plan-header">
            <div>
                <h3>Priority Action Plan</h3>
                <p>Patient-specific items to review before ordering or administering medication.</p>
            </div>
            <div class="action-plan-tools">
                <span class="action-plan-count">${totalActions}</span>
                <button type="button" class="context-collapse-toggle action-plan-collapse-toggle"
                        data-action-plan-collapse-toggle aria-expanded="true"
                        aria-controls="priority-action-plan-body">
                    <span class="collapse-label">Collapse</span>
                    <span class="collapse-icon">&#8722;</span>
                </button>
            </div>
        </div>`;

    if (!actionAlerts.length && !missingItems.length && !unrecognizedItems.length) {
        return html + `
        <div class="action-plan-collapse-body" id="priority-action-plan-body" data-action-plan-collapse-body>
            <div class="action-plan-empty">
                No immediate high-priority action found from the configured rules. Continue normal clinical review.
            </div>
        </div>
    </section>`;
    }

    html += `
        <div class="action-plan-collapse-body" id="priority-action-plan-body" data-action-plan-collapse-body>
            <div class="priority-action-tile-dashboard" data-priority-action-dashboard="${defaultActionPanel}">
                ${buildPriorityActionTile('critical', 'Must Review Now', criticalActionGroups.length, 'Potential stop/hold items', 'action-tile-critical', '&#128683;', defaultActionPanel)}
                ${buildPriorityActionTile('review', 'Review Before Use', highActionGroups.length, 'High-priority cautions', 'action-tile-high', '&#9432;', defaultActionPanel)}
                ${buildPriorityActionTile('missing', 'Need Context', missingItems.length, 'Data needed to decide', 'action-tile-context', '&#8943;', defaultActionPanel)}
                ${buildPriorityActionTile('verify', 'Verify Medication', unrecognizedItems.length, 'Mapping or spelling check', 'action-tile-verify', '&#10067;', defaultActionPanel)}
            </div>
            <div class="priority-action-detail-shell">
                ${buildPriorityActionPanel('critical', 'Must Review Now', criticalActionGroups.map(buildActionPlanAlert).join(''), defaultActionPanel)}
                ${buildPriorityActionPanel('review', 'Review Before Use', highActionGroups.map(buildActionPlanAlert).join(''), defaultActionPanel)}
                ${buildPriorityActionPanel('missing', 'Need More Context', missingItems.map(buildActionPlanMissingContext).join(''), defaultActionPanel)}
                ${buildPriorityActionPanel('verify', 'Verify Medication Mapping', unrecognizedItems.map(buildActionPlanUnrecognized).join(''), defaultActionPanel)}
            </div>
        </div>
    </section>`;
    return html;
}

function isUnrecognizedAlert(alert = {}) {
    return normalizeUiText(alert.category).includes('unrecognized') ||
        normalizeUiText(alert.rxCui).startsWith('unknown') ||
        normalizeUiText(alert.drugClass).includes('unrecognized');
}

function buildPriorityActionTile(id, title, count, subtitle, tileClass, icon, activePanel) {
    const active = id === activePanel ? 'active' : '';
    return `
    <button type="button" class="priority-action-tile ${tileClass} ${active}" data-action-plan-panel-target="${escapeHtml(id)}">
        <span class="priority-action-icon">${icon}</span>
        <span class="priority-action-count">${count}</span>
        <span class="priority-action-title">${escapeHtml(title)}</span>
        <span class="priority-action-subtitle">${escapeHtml(subtitle)}</span>
    </button>`;
}

function buildPriorityActionPanel(id, title, content, activePanel) {
    const active = id === activePanel ? 'active' : '';
    return `
    <section class="priority-action-detail-panel ${active}" data-action-plan-panel="${escapeHtml(id)}">
        <h4>${escapeHtml(title)}</h4>
        ${content || '<div class="action-plan-empty muted-empty">No items in this category.</div>'}
    </section>`;
}

function buildPriorityActionGroups(alerts = []) {
    const groups = new Map();
    const priorityAlerts = alerts.filter(alert => alert.level === 'Critical' || alert.level === 'High');

    for (const alert of priorityAlerts) {
        const key = normalizeUiText(alert.rxCui || alert.medicationName || alert.category);
        if (!groups.has(key)) {
            groups.set(key, []);
        }
        groups.get(key).push(alert);
    }

    return [...groups.values()]
        .map(groupAlerts => ({
            alerts: groupAlerts.sort((a, b) => getAlertPriority(a) - getAlertPriority(b))
        }))
        .sort((a, b) => {
            const priorityDiff = getAlertPriority(a.alerts[0]) - getAlertPriority(b.alerts[0]);
            if (priorityDiff !== 0) return priorityDiff;
            return (a.alerts[0].medicationName || '').localeCompare(b.alerts[0].medicationName || '');
        });
}

function buildNoRuleHitNote(items = []) {
    if (!items.length) return '';

    const names = items
        .slice(0, 4)
        .map(item => item.medicationName)
        .filter(Boolean);
    const more = items.length > names.length ? ` +${items.length - names.length} more` : '';
    const nameText = names.length ? `: ${escapeHtml(names.join(', '))}${more}` : '';

    return `
    <div class="no-rule-note">
        <strong>${items.length} medication candidate${items.length === 1 ? '' : 's'} had no matching configured rule${nameText}.</strong>
        <span>This is not proof of safety; it only means no current rule matched the available patient context.</span>
    </div>`;
}

function buildActionPlanAlert(actionGroup) {
    const alerts = actionGroup.alerts || [actionGroup];
    const alert = alerts[0];
    const hasCritical = alerts.some(item => item.level === 'Critical');
    const levelClass = hasCritical ? 'action-critical' : 'action-high';
    const label = hasCritical ? 'Must review now' : 'Review before order';
    const reason = buildActionPlanReasonSummary(alerts);
    const facts = getPriorityPatientFacts(alerts.flatMap(item => item.matchedPatientFacts || []));
    return `
    <article class="action-plan-item ${levelClass}">
        <div class="action-plan-title">
            <strong>${escapeHtml(buildActionPlanHeadline(alert, hasCritical))}</strong>
            <span class="badge ${hasCritical ? 'badge-danger' : 'badge-warning'}">${escapeHtml(label)}</span>
        </div>
        <div class="action-plan-medication">
            <span>${escapeHtml(alert.medicationName || 'Medication')}</span>
            <small>${escapeHtml(reason)}</small>
        </div>
        <div class="action-plan-explanation">
            <strong>Why it matters</strong>
            ${buildActionPlanReasonList(alerts)}
        </div>
        <div class="action-plan-next-step">
            <strong>Next step</strong>
            <p>${escapeHtml(alert.suggestedAction || 'Review with clinician or pharmacist before use.')}</p>
        </div>
        ${facts.length ? `
            <div class="action-plan-facts">
                <strong>Matched patient context</strong>
                <div>${facts.map(fact => `<span>${escapeHtml(fact)}</span>`).join('')}</div>
            </div>` : ''}
        <div class="context-med-meta">${alerts.length} safety check${alerts.length === 1 ? '' : 's'} matched for this medication.</div>
    </article>`;
}

function buildActionPlanMissingContext(item) {
    return `
    <article class="action-plan-item action-context">
        <div class="action-plan-title">
            <strong>${escapeHtml(toSentenceCase(item.medicationName || 'Medication'))} needs more context</strong>
            <span class="badge badge-info">Need ${escapeHtml(item.field)}</span>
        </div>
        <div class="action-plan-explanation">
            <strong>Why it matters</strong>
            <p>${escapeHtml(item.reason)}</p>
        </div>
        <div class="action-plan-next-step">
            <strong>Next step</strong>
            <p>Collect or verify ${escapeHtml(item.field)} before final safety classification.</p>
        </div>
    </article>`;
}

function buildActionPlanUnrecognized(item) {
    return `
    <article class="action-plan-item action-context">
        <div class="action-plan-title">
            <strong>Verify ${escapeHtml(item.medicationName)} before relying on screening</strong>
            <span class="badge badge-warning">Verify medication</span>
        </div>
        <div class="action-plan-explanation">
            <strong>Why it matters</strong>
            <p>${escapeHtml(item.reason || 'Medication could not be mapped to the knowledge base.')}</p>
        </div>
        <div class="action-plan-next-step">
            <strong>Next step</strong>
            <p>Verify spelling, generic name, brand name, and formulary mapping.</p>
        </div>
    </article>`;
}

function buildActionPlanHeadline(alert, hasCritical = alert.level === 'Critical') {
    const medName = toSentenceCase(alert.medicationName || 'Medication');

    if (hasCritical) {
        return `${medName}: do not proceed until reviewed`;
    }

    return `${medName}: review before use`;
}

function buildActionPlanReasonSummary(alerts = []) {
    const reasons = [...new Set(alerts.map(alert => friendlyRuleCategory(alert.category)).filter(Boolean))];
    if (!reasons.length) return 'Patient-specific safety match';
    if (reasons.length === 1) return reasons[0];
    return `${reasons[0]} + ${reasons.length - 1} more`;
}

function buildActionPlanReasonList(alerts = []) {
    const rows = alerts.slice(0, 4).map(alert => {
        const title = friendlyRuleCategory(alert.category);
        const detail = cleanAlertMessage(alert.message) || friendlyRuleExplanation(alert.category);
        return `
            <li>
                <span>${escapeHtml(title)}</span>
                ${detail ? `<small>${escapeHtml(detail)}</small>` : ''}
            </li>`;
    }).join('');

    if (!rows) {
        return '<p>A patient-specific safety rule matched this medication.</p>';
    }

    return `<ul class="action-plan-reason-list">${rows}</ul>`;
}

function getPriorityPatientFacts(facts = []) {
    const priorityTerms = [
        'glucose',
        'egfr',
        'potassium',
        'sodium',
        'systolic bp',
        'heart rate',
        'pregnant',
        'breastfeeding',
        'context:',
        'current complaint:',
        'symptom:',
        'current medication:'
    ];

    const clinicalFacts = [];
    const contextFacts = [];
    const medicationFacts = [];
    for (const fact of facts || []) {
        const normalized = normalizeUiText(fact);
        if (!priorityTerms.some(term => normalized.includes(term))) continue;
        const cleaned = cleanPatientFact(fact);

        if (normalized.includes('glucose') ||
            normalized.includes('egfr') ||
            normalized.includes('potassium') ||
            normalized.includes('sodium') ||
            normalized.includes('systolic bp') ||
            normalized.includes('heart rate') ||
            normalized.includes('pregnant') ||
            normalized.includes('breastfeeding')) {
            clinicalFacts.push(cleaned);
        } else if (normalized.includes('current medication')) {
            medicationFacts.push(cleaned);
        } else {
            contextFacts.push(cleaned);
        }
    }

    return [...new Set([...clinicalFacts, ...contextFacts, ...medicationFacts])].slice(0, 5);
}

function cleanPatientFact(fact = '') {
    return String(fact || '')
        .replace(/^Current complaint:\s*/i, '')
        .replace(/^Current medication:\s*/i, 'Current med: ')
        .replace(/^Proposed medication:\s*/i, 'Proposed med: ')
        .replace(/^Symptom:\s*/i, '')
        .replace(/^Context:\s*/i, '')
        .trim();
}

function getAlertPriority(alert) {
    if (alert.level === 'Critical') return 0;
    if (alert.level === 'High') return 1;
    if (alert.level === 'Moderate') return 2;
    return 3;
}

function setupContextTiles(container) {
    const tiles = container.querySelectorAll('.context-category-tile');
    const panels = container.querySelectorAll('.context-detail-panel');
    tiles.forEach(tile => {
        tile.addEventListener('click', () => {
            const target = tile.dataset.panelTarget;
            tiles.forEach(t => t.classList.toggle('active', t === tile));
            panels.forEach(panel => {
                panel.classList.toggle('active', panel.dataset.panel === target);
            });
        });
    });
}

function buildMedicationPanelContent(title, items = [], tileClass, badgeClass, alerts = []) {
    let html = `<h3>${escapeHtml(title)} <span class="count">${items.length}</span></h3>`;
    if (!items.length) {
        return html + `<div class="empty-category">None.</div>`;
    }

    html += '<div class="context-med-list compact-detail-list">';
    for (const item of items) {
        const itemAlerts = getAlertsForMedication(item, alerts);
        const meta = [item.drugClass, item.conditionName].filter(Boolean).join(' | ');
        html += `
        <article class="context-med-item ${tileClass}">
            <div class="context-med-title">
                <strong>${escapeHtml(item.medicationName)}</strong>
                <span class="badge ${badgeClass}">${escapeHtml(formatMedicationSeverity(item.severity))}</span>
            </div>
            <div class="context-med-readable-summary">${escapeHtml(buildReadableMedicationSummary(item, itemAlerts))}</div>
            ${meta ? `
                <div class="context-med-summary-grid">
                    ${item.drugClass ? `<div><span>Medication type</span><strong>${escapeHtml(toSentenceCase(item.drugClass))}</strong></div>` : ''}
                    ${item.conditionName ? `<div><span>Patient context</span><strong>${escapeHtml(item.conditionName)}</strong></div>` : ''}
                </div>` : ''}
            ${buildReadableRuleReasons(item, itemAlerts)}
            ${buildRuleCountText(item, itemAlerts)}
        </article>`;
    }
    html += '</div>';
    return html;
}

function getAlertsForMedication(item, alerts = []) {
    const itemName = normalizeUiText(item.medicationName);
    const itemId = normalizeUiText(item.rxCui);
    return (alerts || []).filter(alert => {
        const alertName = normalizeUiText(alert.medicationName);
        const alertId = normalizeUiText(alert.rxCui);
        return (itemId && alertId && itemId === alertId) ||
            (itemName && alertName && itemName === alertName);
    });
}

function buildReadableMedicationSummary(item, alerts = []) {
    const medName = toSentenceCase(item.medicationName || 'this medication');
    if (item.severity === 'must_avoid') {
        return `${medName} should be avoided or held until the safety concern is reviewed.`;
    }

    if (item.severity === 'needs_context') {
        return `${medName} needs more patient context before the app can classify it.`;
    }

    const primaryReason = alerts[0]?.category ? friendlyRuleCategory(alerts[0].category) : '';
    return primaryReason
        ? `Review ${medName} before use because ${primaryReason.toLowerCase()} was detected for this patient.`
        : `Review ${medName} before use because one or more patient-specific safety checks matched.`;
}

function buildReadableRuleReasons(item, alerts = []) {
    const reasons = alerts.length
        ? alerts.slice(0, 4).map(alert => ({
            title: friendlyRuleCategory(alert.category),
            text: cleanAlertMessage(alert.message)
        }))
        : (item.reasons || []).slice(0, 4).map(reason => ({
            title: friendlyRuleCategory(reason),
            text: friendlyRuleExplanation(reason)
        }));

    if (!reasons.length) return '';

    return `
    <div class="context-med-reason-block">
        <strong>Why this needs review</strong>
        <ul>
            ${reasons.map(reason => `
                <li>
                    <span>${escapeHtml(reason.title)}</span>
                    ${reason.text ? `<small>${escapeHtml(reason.text)}</small>` : ''}
                </li>`).join('')}
        </ul>
    </div>`;
}

function buildRuleCountText(item, alerts = []) {
    const count = alerts.length || item.reasons?.length || 0;
    if (!count) return '';
    return `<div class="context-med-meta">${count} safety check${count === 1 ? '' : 's'} matched.</div>`;
}

function formatMedicationSeverity(severity = '') {
    switch (severity) {
        case 'must_avoid': return 'Must avoid';
        case 'use_with_caution': return 'Review needed';
        case 'needs_context': return 'Needs context';
        case 'candidate': return 'Candidate';
        default: return severity ? toSentenceCase(severity.replaceAll('_', ' ')) : 'Candidate';
    }
}

function friendlyRuleCategory(category = '') {
    const normalized = normalizeUiText(category);
    const labels = {
        'hypotension context': 'Low blood pressure risk',
        'use with caution patient relevant': 'Patient-specific caution',
        'hypoglycemia risk': 'Low blood sugar risk',
        'meal time insulin risk': 'Meal-time insulin risk',
        'renal contraindication': 'Kidney function risk',
        'acidosis acute illness': 'Acute illness or acidosis risk',
        'renal dose review': 'Kidney dose review',
        'thyroid cancer contraindication': 'Thyroid cancer contraindication',
        'pancreatitis warning': 'Pancreatitis risk',
        'type 1 diabetes sglt2 risk': 'Type 1 diabetes SGLT2 risk',
        'sglt2 ketoacidosis risk': 'Ketoacidosis risk',
        'fda label contraindication relevant': 'Relevant label contraindication',
        'fda label contraindication': 'Label contraindication',
        'fda label black box': 'Black box warning',
        'patient context warning': 'Patient-context warning'
    };

    return labels[normalized] || toSentenceCase(category.replace(/-/g, ' ').replace(/\s+/g, ' ').trim());
}

function friendlyRuleExplanation(category = '') {
    const normalized = normalizeUiText(category);
    const explanations = {
        'hypotension context': 'The patient has low-BP risk such as hypotension, shock, or dizziness.',
        'use with caution patient relevant': 'A label caution matched this patient context.',
        'hypoglycemia risk': 'The patient has low glucose risk or factors that can worsen hypoglycemia.',
        'renal contraindication': 'Kidney function may make this medication unsafe or require holding.',
        'acidosis acute illness': 'Acute illness can increase serious adverse-event risk.',
        'sglt2 ketoacidosis risk': 'Sick-day, fasting, dehydration, or DKA risk factors are present.'
    };

    return explanations[normalized] || '';
}

function cleanAlertMessage(message = '') {
    return String(message || '').replace(/^\[[^\]]+\]\s*/, '').trim();
}

function toSentenceCase(value = '') {
    const text = String(value || '').trim();
    return text ? text.charAt(0).toUpperCase() + text.slice(1) : '';
}

function normalizeUiText(value = '') {
    return String(value || '')
        .trim()
        .replace(/[_/()-]+/g, ' ')
        .replace(/\s+/g, ' ')
        .toLowerCase();
}

function buildAlertPanelContent(title, alerts = []) {
    let html = `<h3>${escapeHtml(title)} <span class="count">${alerts.length}</span></h3>`;
    if (!alerts.length) {
        return html + `<div class="empty-category">No configured alert triggered.</div>`;
    }

    html += '<div class="context-med-list compact-detail-list">';
    for (const alert of alerts) {
        const badgeClass = alert.level === 'Critical' ? 'badge-danger' : alert.level === 'High' ? 'badge-warning' : 'badge-info';
        const tileClass = alert.level === 'Critical' ? 'tile-danger' : 'tile-caution';
        html += `
        <article class="context-med-item ${tileClass}">
            <div class="context-med-title">
                <strong>${escapeHtml(alert.category)}</strong>
                <span class="badge ${badgeClass}">${escapeHtml(alert.level)}</span>
            </div>
            <div class="context-med-meta">${escapeHtml(alert.medicationName)}</div>
            <p>${escapeHtml(alert.message)}</p>
            <p><strong>Action:</strong> ${escapeHtml(alert.suggestedAction)}</p>
            ${buildWhyFired(alert.matchedPatientFacts)}
            <div class="context-med-meta">Source: ${escapeHtml(alert.source || 'Clinical rule')}</div>
        </article>`;
    }
    html += '</div>';
    return html;
}

function buildUnrecognizedPanelContent(items = []) {
    let html = `<h3>Unrecognized Medications <span class="count">${items.length}</span></h3>`;
    if (!items.length) {
        return html + `<div class="empty-category">All entered medications were matched to the configured knowledge base.</div>`;
    }

    html += '<div class="context-med-list compact-detail-list">';
    for (const item of items) {
        html += `
        <article class="context-med-item tile-caution">
            <div class="context-med-title">
                <strong>${escapeHtml(item.medicationName)}</strong>
                <span class="badge badge-warning">${escapeHtml(item.source || 'Unrecognized')}</span>
            </div>
            <p>${escapeHtml(item.reason || 'Medication could not be verified.')}</p>
            <div class="context-med-meta">Verify spelling, generic name, brand name, and formulary mapping before relying on this screening.</div>
        </article>`;
    }
    html += '</div>';
    return html;
}

function buildMissingPanelContent(items = []) {
    let html = `<h3>${items.length ? 'Missing Context' : 'Context Complete'} <span class="count">${items.length}</span></h3>`;
    if (!items.length) {
        return html + `<div class="empty-category">No missing required labs or vitals were detected for the medications being evaluated. The current rule engine checks for missing glucose, eGFR, potassium, sodium, heart rate, and blood pressure when those values are needed by a medication rule.</div>`;
    }

    html += '<div class="context-med-list compact-detail-list">';
    for (const item of items) {
        html += `
        <article class="context-med-item tile-caution">
            <div class="context-med-title">
                <strong>${escapeHtml(item.medicationName)}</strong>
                <span class="badge badge-warning">${escapeHtml(item.field)}</span>
            </div>
            <p>${escapeHtml(item.reason)}</p>
            <p><strong>Next step:</strong> collect or verify ${escapeHtml(item.field)} before relying on final classification.</p>
        </article>`;
    }
    html += '</div>';
    return html;
}

function buildMedicationBucket(title, items = [], tileClass, badgeClass) {
    let html = `<div class="context-result-section"><h3>${escapeHtml(title)} <span class="count">${items.length}</span></h3>`;
    if (!items.length) {
        html += `<div class="empty-category">None.</div></div>`;
        return html;
    }

    html += '<div class="context-med-list">';
    for (const item of items) {
        const meta = [item.drugClass, item.conditionName].filter(Boolean).join(' | ');
        html += `
        <article class="context-med-item ${tileClass}">
            <div class="context-med-title">
                <strong>${escapeHtml(item.medicationName)}</strong>
                <span class="badge ${badgeClass}">${escapeHtml(item.severity || 'candidate')}</span>
            </div>
            ${meta ? `<div class="context-med-meta">${escapeHtml(meta)}</div>` : ''}
            ${item.reasons?.length ? `<ul>${item.reasons.map(r => `<li>${escapeHtml(r)}</li>`).join('')}</ul>` : ''}
            ${item.safetyLabel ? `<p>${escapeHtml(item.safetyLabel)}</p>` : ''}
        </article>`;
    }
    html += '</div></div>';
    return html;
}

function buildAlertBucket(title, alerts = []) {
    let html = `<div class="context-result-section"><h3>${escapeHtml(title)} <span class="count">${alerts.length}</span></h3>`;
    if (!alerts.length) {
        html += `<div class="empty-category">No configured alert triggered.</div></div>`;
        return html;
    }

    html += '<div class="context-med-list">';
    for (const alert of alerts) {
        const badgeClass = alert.level === 'Critical' ? 'badge-danger' : alert.level === 'High' ? 'badge-warning' : 'badge-info';
        const tileClass = alert.level === 'Critical' ? 'tile-danger' : 'tile-caution';
        html += `
        <article class="context-med-item ${tileClass}">
            <div class="context-med-title">
                <strong>${escapeHtml(alert.category)}</strong>
                <span class="badge ${badgeClass}">${escapeHtml(alert.level)}</span>
            </div>
            <div class="context-med-meta">${escapeHtml(alert.medicationName)}</div>
            <p>${escapeHtml(alert.message)}</p>
            <p><strong>Action:</strong> ${escapeHtml(alert.suggestedAction)}</p>
            ${buildWhyFired(alert.matchedPatientFacts)}
            <div class="context-med-meta">Source: ${escapeHtml(alert.source || 'Clinical rule')}</div>
        </article>`;
    }
    html += '</div></div>';
    return html;
}

function buildWhyFired(facts = []) {
    if (!facts.length) return '';
    return `
    <div class="why-fired">
        <strong>Why this fired</strong>
        <ul>
            ${facts.slice(0, 6).map(fact => `<li>${escapeHtml(fact)}</li>`).join('')}
        </ul>
    </div>`;
}

function buildMissingBucket(items = []) {
    let html = `<div class="context-result-section"><h3>Missing Context <span class="count">${items.length}</span></h3>`;
    if (!items.length) {
        html += `<div class="empty-category">No missing context detected.</div></div>`;
        return html;
    }

    html += '<div class="context-med-list">';
    for (const item of items) {
        html += `
        <article class="context-med-item tile-caution">
            <div class="context-med-title">
                <strong>${escapeHtml(item.medicationName)}</strong>
                <span class="badge badge-warning">${escapeHtml(item.field)}</span>
            </div>
            <p>${escapeHtml(item.reason)}</p>
        </article>`;
    }
    html += '</div></div>';
    return html;
}

function renderDrugReport(report) {
    const verdict = report.overallVerdict;
    let verdictText = verdict.replace(/([A-Z])/g, ' $1').trim();

    let html = `
    <div class="drug-report">
        <div class="drug-report-header header-${verdict}" onclick="toggleDrugReport(this)">
            <h3>
                <span>${getVerdictIcon(verdict)}</span>
                ${escapeHtml(report.drugName)}
                <small style="font-weight:400;color:var(--gray-600)">(${escapeHtml(report.drugClass)})</small>
            </h3>
            <div style="display:flex;align-items:center;gap:8px;">
                <span class="verdict-badge verdict-${verdict}">${verdictText}</span>
                <span class=\"collapse-chevron chevron-collapsed\">&#9662;</span>
            </div>
        </div>
        <div class="drug-report-body collapsed">`;

    // Build category tiles grid
    const categories = [];

    if (report.mustAvoidReasons.length > 0) {
        categories.push({ id: 'must-avoid', icon: '&#128683;', title: 'Must Avoid', count: report.mustAvoidReasons.length, colorClass: 'tile-danger', alerts: report.mustAvoidReasons });
    }
    if (report.blackBoxWarnings.length > 0) {
        categories.push({ id: 'blackbox', icon: '&#9760;', title: 'Black Box', count: report.blackBoxWarnings.length, colorClass: 'tile-blackbox', alerts: report.blackBoxWarnings });
    }
    if (report.allergyAlerts.length > 0) {
        categories.push({ id: 'allergy', icon: '&#129514;', title: 'Allergy', count: report.allergyAlerts.length, colorClass: 'tile-allergy', alerts: report.allergyAlerts });
    }
    if (report.warnings.length > 0) {
        categories.push({ id: 'warning', icon: '&#9888;', title: 'Warnings', count: report.warnings.length, colorClass: 'tile-warning', alerts: report.warnings });
    }
    if (report.useWithCaution.length > 0) {
        categories.push({ id: 'caution', icon: '&#9432;', title: 'Use With Caution', count: report.useWithCaution.length, colorClass: 'tile-caution', alerts: report.useWithCaution });
    }
    if (report.drugInteractions.length > 0) {
        categories.push({ id: 'interaction', icon: '&#128260;', title: 'Drug Interactions', count: report.drugInteractions.length, colorClass: 'tile-interaction', interactions: report.drugInteractions });
    }
    if (report.totalAlerts === 0) {
        categories.push({ id: 'safe', icon: '&#10004;', title: 'Safe', count: '', colorClass: 'tile-safe', safe: true });
    }

    if (categories.length > 0) {
        const tileGridId = `tiles-${report.drugId}-${Math.random().toString(36).slice(2, 8)}`;
        html += `<div class="category-tiles-grid" id="${tileGridId}">`;
        categories.forEach((cat, idx) => {
            html += `
            <div class="category-tile ${cat.colorClass}" data-tile-index="${idx}" onclick="toggleTileDetail(this)">
                <div class="tile-icon">${cat.icon}</div>
                <div class="tile-count">${cat.count}</div>
                <div class="tile-title">${cat.title}</div>
                <div class="tile-chevron">&#9662;</div>
            </div>`;
        });
        html += `</div>`;

        // Detail panels (hidden by default, shown on tile click)
        categories.forEach((cat, idx) => {
            html += `<div class="tile-detail-panel" data-panel-index="${idx}" style="display:none;">`;
            html += `<div class="tile-detail-header ${cat.colorClass}-header">
                <span>${cat.icon} ${cat.title} (${cat.count})</span>
                <span class="tile-detail-close" onclick="closeTileDetail(this)">&#10005;</span>
            </div>`;

            if (cat.safe) {
                html += `<div class="alert-item alert-low" style="margin:12px;">
                    <span class="alert-category">&#10004; No Issues Found</span>
                    No contraindications, warnings, or interactions found in the knowledge base for this drug with the given patient profile.
                </div>`;
            } else if (cat.interactions) {
                html += renderInteractionItems(cat.interactions);
            } else {
                html += renderAlertItems(cat.alerts);
            }
            html += `</div>`;
        });
    }



    html += `</div></div>`;
    return html;
}

function toggleDrugReport(headerEl) {
    const body = headerEl.parentElement.querySelector('.drug-report-body');
    const chevron = headerEl.querySelector('.collapse-chevron');
    body.classList.toggle('collapsed');
    if (chevron) chevron.classList.toggle('chevron-collapsed');
}

function toggleTileDetail(tileEl) {
    const idx = tileEl.dataset.tileIndex;
    const reportBody = tileEl.closest('.drug-report-body');
    const panels = reportBody.querySelectorAll('.tile-detail-panel');
    const tiles = reportBody.querySelectorAll('.category-tile');
    const targetPanel = reportBody.querySelector(`.tile-detail-panel[data-panel-index="${idx}"]`);

    // Close all other panels and deselect tiles
    panels.forEach(p => {
        if (p !== targetPanel) p.style.display = 'none';
    });
    tiles.forEach(t => {
        if (t !== tileEl) t.classList.remove('tile-active');
    });

    // Toggle selected panel
    if (targetPanel.style.display === 'none') {
        targetPanel.style.display = 'block';
        tileEl.classList.add('tile-active');
        targetPanel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    } else {
        targetPanel.style.display = 'none';
        tileEl.classList.remove('tile-active');
    }
}

function closeTileDetail(closeBtn) {
    const panel = closeBtn.closest('.tile-detail-panel');
    const reportBody = panel.closest('.drug-report-body');
    const idx = panel.dataset.panelIndex;
    panel.style.display = 'none';
    const tile = reportBody.querySelector(`.category-tile[data-tile-index="${idx}"]`);
    if (tile) tile.classList.remove('tile-active');
}

function renderAlertItems(alerts) {
    let html = '';
    for (const alert of alerts) {
        const levelClass = alert.level === 'Critical' ? 'critical' :
                          alert.level === 'High' ? 'high' :
                          alert.level === 'Moderate' ? 'moderate' : 'low';
        html += `<div class="alert-item alert-${levelClass}">
            <span class="alert-category">${escapeHtml(alert.category)}</span>
            ${escapeHtml(alert.message)}
            ${alert.source ? `<span class="alert-source">Source: ${escapeHtml(alert.source)}</span>` : ''}
        </div>`;
    }
    return html;
}

function renderInteractionItems(interactions) {
    let html = '';
    for (const ix of interactions) {
        const levelClass = ix.level === 'Critical' ? 'critical' :
                          ix.level === 'High' ? 'high' : 'moderate';
        html += `<div class="interaction-item">
            <div class="interaction-drugs">
                <span class="badge badge-${levelClass === 'critical' ? 'danger' : levelClass === 'high' ? 'warning' : 'info'}">${ix.level}</span>
                ${escapeHtml(ix.currentDrug)} &#8646; ${escapeHtml(ix.proposedDrug)}
            </div>
            <div><strong>Effect:</strong> ${escapeHtml(ix.effect)}</div>
            <div class="interaction-detail">
                <div><strong>Mechanism:</strong> ${escapeHtml(ix.mechanism)}</div>
                <div><strong>Management:</strong> ${escapeHtml(ix.management)}</div>
            </div>
        </div>`;
    }
    return html;
}

function getVerdictIcon(verdict) {
    switch (verdict) {
        case 'DoNotUse': return '&#128683;';
        case 'UseWithExtremeCaution': return '&#9888;';
        case 'UseWithCaution': return '&#9888;';
        case 'GenerallyAcceptable': return '&#10004;';
        default: return '&#8226;';
    }
}

// ── Clear & Demo ─────────────────────────────────────────

function renderDemoScenarios() {
    const select = document.getElementById('demo-scenario-select');
    if (!select) return;

    select.innerHTML = '<option value="">Select scenario</option>' + DEMO_PATIENT_SCENARIOS
        .map((scenario, index) => `<option value="${escapeHtml(scenario.id)}">${index + 1}. ${escapeHtml(scenario.title)}</option>`)
        .join('');

    select.addEventListener('change', () => {
        const scenario = DEMO_PATIENT_SCENARIOS.find(item => item.id === select.value);
        if (scenario) loadDemoScenario(scenario);
    });
}

function clearScreeningForm(options = {}) {
    document.getElementById('screening-form').reset();
    allergyTags.clear();
    comorbidityTags.clear();
    complaintTags.clear();
    currentMedTags.clear();
    proposedMedTags.clear();
    document.getElementById('screening-results').style.display = 'none';
    if (options.resetDemoSelect !== false) {
        const select = document.getElementById('demo-scenario-select');
        if (select) select.value = '';
    }
}

function loadDemoScenario(scenario) {
    clearScreeningForm({ resetDemoSelect: false });

    const data = scenario.data;
    document.getElementById('patientId').value = scenario.patientId;
    document.getElementById('age').value = data.age ?? '';
    document.getElementById('isPregnant').checked = Boolean(data.isPregnant);
    document.getElementById('isBreastfeeding').checked = Boolean(data.isBreastfeeding);

    setNumericValue('glucose', data.labs?.glucose);
    setNumericValue('egfr', data.labs?.eGfr);
    setNumericValue('potassium', data.labs?.potassium);
    setNumericValue('sodium', data.labs?.sodium);
    setNumericValue('heartRate', data.vitals?.heartRate);
    setNumericValue('systolicBp', data.vitals?.systolicBp);

    allergyTags.setValues(data.allergies || []);
    comorbidityTags.setValues(data.comorbidities || []);
    complaintTags.setValues(data.complaints || []);
    currentMedTags.setValues(data.currentMedications || []);
    proposedMedTags.setValues(data.proposedMedications || []);

    const select = document.getElementById('demo-scenario-select');
    if (select) select.value = scenario.id;
}

function setNumericValue(id, value) {
    document.getElementById(id).value = value ?? '';
}

document.getElementById('clear-btn').addEventListener('click', () => {
    clearScreeningForm();
});

// ══════════════════════════════════════════════════════════
// DRUG LOOKUP
// ══════════════════════════════════════════════════════════

async function loadDrugList() {
    const list = document.getElementById('drug-list');
    list.innerHTML = '<div class="loading"><div class="spinner"></div>Loading drug database...</div>';

    try {
        const drugs = await apiCall(`${API_BASE}/Drugs`);
        renderDrugList(drugs);
    } catch (err) {
        list.innerHTML = `<p style="color:var(--danger)">Error loading drugs: ${escapeHtml(err.message)}</p>`;
    }
}

function renderDrugList(drugs) {
    const list = document.getElementById('drug-list');
    list.innerHTML = drugs.map(d => `
        <div class="drug-card" onclick="loadDrugDetail('${escapeHtml(d.drugId)}')">
            <h3>${escapeHtml(d.genericName)}</h3>
            <div class="drug-class">${escapeHtml(d.drugClass)} | ${escapeHtml(d.category)}</div>
            <div style="font-size:0.82rem;color:var(--gray-600);margin-bottom:6px">
                ${d.brandNames.map(b => escapeHtml(b)).join(', ')}
            </div>
            <div class="drug-meta">
                ${d.blackBoxWarningCount > 0 ? `<span class="badge badge-danger">${d.blackBoxWarningCount} Black Box</span>` : ''}
                ${d.contraindicationCount > 0 ? `<span class="badge badge-warning">${d.contraindicationCount} Contraindications</span>` : ''}
                ${d.interactionCount > 0 ? `<span class="badge badge-info">${d.interactionCount} Interactions</span>` : ''}
            </div>
        </div>
    `).join('');
}

async function loadDrugDetail(drugId) {
    const detail = document.getElementById('drug-detail');
    detail.style.display = 'block';
    detail.innerHTML = '<div class="loading"><div class="spinner"></div>Loading drug details...</div>';
    detail.scrollIntoView({ behavior: 'smooth' });

    try {
        const drug = await apiCall(`${API_BASE}/Drugs/${drugId}`);
        renderDrugDetail(drug);
    } catch (err) {
        detail.innerHTML = `<p style="color:var(--danger)">Error: ${escapeHtml(err.message)}</p>`;
    }
}

function renderDrugDetail(drug) {
    const detail = document.getElementById('drug-detail');
    let html = `<div class="drug-detail-view">
        <h3>${escapeHtml(drug.genericName)}</h3>
        <div class="detail-meta">
            <strong>Brand Names:</strong> ${drug.brandNames.map(b => escapeHtml(b)).join(', ')} |
            <strong>Class:</strong> ${escapeHtml(drug.drugClass)} |
            <strong>Category:</strong> ${escapeHtml(drug.category)}
        </div>
        <div class="detail-meta">
            <strong>Indications:</strong> ${drug.indications.map(i => escapeHtml(i)).join(', ')}
        </div>`;

    // Black Box Warnings
    if (drug.blackBoxWarnings.length > 0) {
        html += `<div class="detail-section blackbox-section">
            <h4>&#9760; BLACK BOX WARNINGS</h4>
            <ul>${drug.blackBoxWarnings.map(w => `<li>${escapeHtml(w)}</li>`).join('')}</ul>
        </div>`;
    }

    // Contraindications
    if (drug.contraindications.length > 0) {
        html += `<div class="detail-section contraindication-section">
            <h4>&#128683; CONTRAINDICATIONS</h4>
            <ul>${drug.contraindications.map(c => `<li>
                <strong>[${c.severity}]</strong> ${escapeHtml(c.condition)}: ${escapeHtml(c.description)}
                <span style="font-size:0.8rem;opacity:0.7"> (${escapeHtml(c.source)})</span>
            </li>`).join('')}</ul>
        </div>`;
    }

    // Warnings
    if (drug.warnings.length > 0) {
        html += `<div class="detail-section warning-section">
            <h4>&#9888; WARNINGS</h4>
            <ul>${drug.warnings.map(w => `<li>${escapeHtml(w)}</li>`).join('')}</ul>
        </div>`;
    }

    // Use With Caution
    if (drug.useWithCaution.length > 0) {
        html += `<div class="detail-section caution-section">
            <h4>&#9432; USE WITH CAUTION</h4>
            <ul>${drug.useWithCaution.map(c => `<li>${escapeHtml(c)}</li>`).join('')}</ul>
        </div>`;
    }

    // Drug Interactions
    if (drug.interactions.length > 0) {
        html += `<div class="detail-section interaction-section">
            <h4>&#128260; DRUG INTERACTIONS</h4>
            <ul>${drug.interactions.map(i => `<li>
                <strong>[${i.severity}]</strong>
                <strong>${escapeHtml(i.interactingDrugName)}:</strong>
                ${escapeHtml(i.effect)} — <em>${escapeHtml(i.mechanism)}</em>
                <br><span style="color:var(--primary)">Management: ${escapeHtml(i.clinicalManagement)}</span>
            </li>`).join('')}</ul>
        </div>`;
    }

    // Side Effects
    if (drug.sideEffects.length > 0) {
        html += `<div class="detail-section">
            <h4>Side Effects</h4>
            <div style="display:flex;flex-wrap:wrap;gap:6px;padding:0 12px">
                ${drug.sideEffects.map(s => `<span class="badge badge-info">${escapeHtml(s)}</span>`).join('')}
            </div>
        </div>`;
    }

    // Allergy Groups
    if (drug.allergyGroups.length > 0) {
        html += `<div class="detail-section">
            <h4>Allergy Groups / Cross-Reactivity</h4>
            <div style="display:flex;flex-wrap:wrap;gap:6px;padding:0 12px">
                ${drug.allergyGroups.map(a => `<span class="badge badge-warning">${escapeHtml(a)}</span>`).join('')}
            </div>
        </div>`;
    }

    html += `</div>`;
    detail.innerHTML = html;
}

// Drug search
document.getElementById('search-drug-btn').addEventListener('click', async () => {
    const q = document.getElementById('drug-search').value.trim();
    if (!q) { loadDrugList(); return; }

    const list = document.getElementById('drug-list');
    list.innerHTML = '<div class="loading"><div class="spinner"></div>Searching...</div>';

    try {
        const drugs = await apiCall(`${API_BASE}/Drugs/search?q=${encodeURIComponent(q)}`);
        if (drugs.length === 0) {
            list.innerHTML = '<p>No drugs found matching your search.</p>';
        } else {
            // Map search results to same shape as list
            renderDrugList(drugs.map(d => ({
                drugId: d.drugId,
                genericName: d.genericName,
                brandNames: d.brandNames,
                drugClass: d.drugClass,
                category: d.category,
                blackBoxWarningCount: d.blackBoxWarnings?.length || 0,
                contraindicationCount: d.contraindications?.length || 0,
                interactionCount: d.interactions?.length || 0
            })));
        }
    } catch (err) {
        list.innerHTML = `<p style="color:var(--danger)">Error: ${escapeHtml(err.message)}</p>`;
    }
});

document.getElementById('drug-search').addEventListener('keydown', (e) => {
    if (e.key === 'Enter') document.getElementById('search-drug-btn').click();
});

// ══════════════════════════════════════════════════════════
// QUICK CHECK
// ══════════════════════════════════════════════════════════

document.getElementById('quick-check-btn').addEventListener('click', async () => {
    const drug = document.getElementById('qc-drug').value.trim();
    if (!drug) { alert('Please enter a drug name.'); return; }

    const condition = document.getElementById('qc-condition').value.trim();
    const allergy = document.getElementById('qc-allergy').value.trim();
    const currentMed = document.getElementById('qc-current-med').value.trim();

    let url = `${API_BASE}/SafetyScreening/quick-check?drug=${encodeURIComponent(drug)}`;
    if (condition) url += `&condition=${encodeURIComponent(condition)}`;
    if (allergy) url += `&allergy=${encodeURIComponent(allergy)}`;
    if (currentMed) url += `&currentMedication=${encodeURIComponent(currentMed)}`;

    showLoading('quick-check-results');
    try {
        const result = await apiCall(url);
        renderQuickCheckResults(result);
    } catch (err) {
        document.getElementById('quick-check-results').innerHTML =
            `<div class="card"><p style="color:var(--danger)">Error: ${escapeHtml(err.message)}</p></div>`;
    }
});

function renderQuickCheckResults(result) {
    // Reuse the full screening results renderer
    const container = document.getElementById('quick-check-results');
    container.style.display = 'block';

    let html = '';
    for (const report of result.drugReports) {
        html += renderDrugReport(report);
    }
    container.innerHTML = html;
}

// ═══════════════════════════════════════════════════════════════════
// RULE MANAGER
// ═══════════════════════════════════════════════════════════════════

let rulesCache = [];
let pendingDeleteRuleId = '';
let rulesHaveLoaded = false;
let rulesLoading = false;
let selectedRuleId = '';

async function loadRules(options = {}) {
    const force = Boolean(options.force);
    const list = document.getElementById('rules-list');
    if (!list) return;
    if (rulesLoading || (rulesHaveLoaded && !force)) return;

    const refreshButton = document.getElementById('refresh-rules-btn');
    const listStatus = document.getElementById('rules-list-status');
    const showFullLoader = !rulesHaveLoaded && !rulesCache.length;

    rulesLoading = true;
    if (refreshButton) {
        refreshButton.disabled = true;
        refreshButton.innerHTML = '&#8635; Refreshing';
    }
    if (showFullLoader) {
        list.innerHTML = '<div class="loading"><div class="spinner"></div>Loading rules...</div>';
    } else if (listStatus) {
        listStatus.textContent = 'Refreshing rules...';
        listStatus.className = 'form-status status-info';
    }

    try {
        const loadedRules = await apiCall(`${API_BASE}/Rules`);
        rulesCache = Array.isArray(loadedRules) ? loadedRules : [];
        rulesHaveLoaded = true;
        renderRuleStats();
        renderRulesList();
        if (listStatus) {
            listStatus.textContent = force ? 'Rules refreshed.' : '';
            listStatus.className = `form-status ${force ? 'status-success' : ''}`;
        }
    } catch (err) {
        if (!rulesHaveLoaded) {
            list.innerHTML = `<p style="color:var(--danger)">Error loading rules: ${escapeHtml(err.message)}</p>`;
        }
        setRuleListStatus(`Error loading rules: ${err.message}`, 'error');
    } finally {
        rulesLoading = false;
        if (refreshButton) {
            refreshButton.disabled = false;
            refreshButton.innerHTML = '&#8635; Refresh';
        }
    }
}

function renderRuleStats() {
    const counts = {
        total: rulesCache.length,
        critical: rulesCache.filter(r => r.level === 'Critical').length,
        high: rulesCache.filter(r => r.level === 'High').length,
        disabled: rulesCache.filter(r => !r.enabled).length
    };

    const totalEl = document.getElementById('rules-total-count');
    const criticalEl = document.getElementById('rules-critical-count');
    const highEl = document.getElementById('rules-high-count');
    const disabledEl = document.getElementById('rules-disabled-count');

    if (totalEl) totalEl.textContent = counts.total;
    if (criticalEl) criticalEl.textContent = counts.critical;
    if (highEl) highEl.textContent = counts.high;
    if (disabledEl) disabledEl.textContent = counts.disabled;
}

function getFilteredRules() {
    const search = (document.getElementById('rule-search')?.value || '').trim().toLowerCase();
    const severity = document.getElementById('rule-severity-filter')?.value || '';

    return rulesCache.filter(rule => {
        const severityMatches = !severity || rule.level === severity;
        const searchMatches = !search || getRuleSearchText(rule).includes(search);
        return severityMatches && searchMatches;
    });
}

function getRuleSearchText(rule) {
    return [
        rule.name,
        rule.level,
        rule.category,
        rule.source,
        rule.message,
        rule.suggestedAction,
        ...(rule.medicationTerms || []),
        ...(rule.conditionTerms || []),
        ...(rule.allergyTerms || []),
        ...(rule.symptomTerms || []),
        ...(rule.riskFlagTerms || [])
    ].join(' ').toLowerCase();
}

function renderRulesList() {
    const list = document.getElementById('rules-list');
    if (!list) return;

    if (!rulesCache.length) {
        list.innerHTML = '<div class="empty-category">No custom rules configured.</div>';
        return;
    }

    const rules = getFilteredRules();
    if (!rules.length) {
        list.innerHTML = '<div class="empty-category">No rules match the current filters.</div>';
        return;
    }

    list.innerHTML = rules.map(rule => {
        const isPendingDelete = pendingDeleteRuleId === rule.id;
        return `
        <article class="rule-row ${rule.enabled ? '' : 'rule-disabled'} ${isPendingDelete ? 'rule-pending-delete' : ''}">
            <div class="rule-row-main">
                <div class="rule-title-line">
                    <strong>${escapeHtml(rule.name)}</strong>
                    <span class="badge ${getRuleLevelClass(rule.level)}">${escapeHtml(rule.level)}</span>
                    <span class="badge ${rule.enabled ? 'badge-info' : 'badge-warning'}">${rule.enabled ? 'Enabled' : 'Disabled'}</span>
                </div>
                <div class="rule-meta">${escapeHtml(rule.category)} | ${escapeHtml(rule.source || 'Custom rule')}</div>
                <div class="rule-pill-row">
                    <span class="rule-pill">Med: ${escapeHtml(summarizeTerms(rule.medicationTerms))}</span>
                    <span class="rule-pill">Triggers: ${escapeHtml(summarizeRuleTriggers(rule))}</span>
                </div>
            </div>
            <div class="rule-row-actions">
                ${isPendingDelete ? `
                    <button type="button" class="btn btn-danger btn-small" data-rule-confirm-delete="${escapeHtml(rule.id)}">&#128465; Confirm Delete</button>
                    <button type="button" class="btn btn-secondary btn-small" data-rule-cancel-delete="${escapeHtml(rule.id)}">Cancel</button>
                ` : `
                    <button type="button" class="btn btn-primary btn-small" data-rule-view="${escapeHtml(rule.id)}">View</button>
                    <button type="button" class="btn btn-secondary btn-small" data-rule-edit="${escapeHtml(rule.id)}">&#9998; Edit</button>
                    <button type="button" class="btn btn-danger btn-small" data-rule-delete="${escapeHtml(rule.id)}">&#128465; Delete</button>
                `}
            </div>
        </article>
    `;
    }).join('');

    list.querySelectorAll('[data-rule-view]').forEach(btn => {
        btn.addEventListener('click', () => viewRule(btn.dataset.ruleView));
    });
    list.querySelectorAll('[data-rule-edit]').forEach(btn => {
        btn.addEventListener('click', () => editRule(btn.dataset.ruleEdit));
    });
    list.querySelectorAll('[data-rule-delete]').forEach(btn => {
        btn.addEventListener('click', () => requestRuleDelete(btn.dataset.ruleDelete));
    });
    list.querySelectorAll('[data-rule-confirm-delete]').forEach(btn => {
        btn.addEventListener('click', () => deleteRule(btn.dataset.ruleConfirmDelete));
    });
    list.querySelectorAll('[data-rule-cancel-delete]').forEach(btn => {
        btn.addEventListener('click', () => cancelRuleDelete());
    });
}

function getRuleLevelClass(level) {
    if (level === 'Critical') return 'badge-danger';
    if (level === 'High') return 'badge-warning';
    if (level === 'Low') return 'badge-success';
    return 'badge-info';
}

function summarizeTerms(values = []) {
    if (!values.length) return 'None';
    if (values.length <= 3) return values.join(', ');
    return `${values.slice(0, 3).join(', ')} +${values.length - 3} more`;
}

function summarizeRuleTriggers(rule) {
    const labels = [];
    if ((rule.conditionTerms || []).length) labels.push(`${rule.conditionTerms.length} condition`);
    if ((rule.allergyTerms || []).length) labels.push(`${rule.allergyTerms.length} allergy`);
    if ((rule.symptomTerms || []).length) labels.push(`${rule.symptomTerms.length} symptom`);
    if ((rule.riskFlagTerms || []).length) labels.push(`${rule.riskFlagTerms.length} flag`);
    return labels.length ? labels.join(', ') : 'None';
}

function renderRulePills(label, values = []) {
    if (!values.length) return '';
    return values.map(value => `<span class="rule-pill">${escapeHtml(label)}: ${escapeHtml(value)}</span>`).join('');
}

function setRulesMode(mode) {
    const listView = document.getElementById('rules-list-view');
    const detailView = document.getElementById('rule-detail-view');
    const formView = document.getElementById('rule-form');

    if (listView) listView.hidden = mode !== 'list';
    if (detailView) detailView.hidden = mode !== 'detail';
    if (formView) formView.hidden = mode !== 'form';
}

function showRuleList(message = '', type = '') {
    selectedRuleId = '';
    setRulesMode('list');
    setRuleFormStatus('');
    setRuleListStatus(message, type);
    renderRuleStats();
    renderRulesList();
}

function startNewRule() {
    clearRuleForm();
    pendingDeleteRuleId = '';
    selectedRuleId = '';
    document.getElementById('rule-editor-title').textContent = 'Add Rule';
    setRuleFormStatus('');
    setRulesMode('form');
    document.getElementById('rule-name').focus();
}

function viewRule(id) {
    const rule = rulesCache.find(r => r.id === id);
    if (!rule) return;

    pendingDeleteRuleId = '';
    selectedRuleId = id;
    renderRuleDetail(rule);
    setRulesMode('detail');
}

function renderRuleDetail(rule) {
    const detail = document.getElementById('rule-detail-content');
    if (!detail) return;

    detail.innerHTML = `
        <div class="rule-detail-heading">
            <div>
                <h3>${escapeHtml(rule.name)}</h3>
                <p>${escapeHtml(rule.category)} | ${escapeHtml(rule.source || 'Custom rule')}</p>
            </div>
            <div class="rule-detail-badges">
                <span class="badge ${getRuleLevelClass(rule.level)}">${escapeHtml(rule.level)}</span>
                <span class="badge ${rule.enabled ? 'badge-info' : 'badge-warning'}">${rule.enabled ? 'Enabled' : 'Disabled'}</span>
            </div>
        </div>

        <div class="rule-detail-grid">
            <section>
                <h4>Medication Match</h4>
                <div class="rule-pill-row">${renderRulePills('Med', rule.medicationTerms) || '<span class="muted">None</span>'}</div>
            </section>
            <section>
                <h4>Patient Context Triggers</h4>
                <div class="rule-pill-row">
                    ${renderRulePills('Condition', rule.conditionTerms)}
                    ${renderRulePills('Allergy', rule.allergyTerms)}
                    ${renderRulePills('Symptom', rule.symptomTerms)}
                    ${renderRulePills('Flag', rule.riskFlagTerms)}
                    ${summarizeRuleTriggers(rule) === 'None' ? '<span class="muted">None</span>' : ''}
                </div>
            </section>
            <section>
                <h4>Warning Message</h4>
                <p>${escapeHtml(rule.message || '')}</p>
            </section>
            <section>
                <h4>Suggested Action</h4>
                <p>${escapeHtml(rule.suggestedAction || '')}</p>
            </section>
        </div>
    `;
}

function editRule(id) {
    const rule = rulesCache.find(r => r.id === id);
    if (!rule) return;

    pendingDeleteRuleId = '';
    selectedRuleId = id;
    document.getElementById('rule-editor-title').textContent = 'Edit Rule';
    document.getElementById('rule-id').value = rule.id;
    document.getElementById('rule-name').value = rule.name || '';
    document.getElementById('rule-enabled').checked = Boolean(rule.enabled);
    document.getElementById('rule-level').value = rule.level || 'High';
    document.getElementById('rule-category').value = rule.category || '';
    document.getElementById('rule-source').value = rule.source || '';
    document.getElementById('rule-medications').value = formatTerms(rule.medicationTerms);
    document.getElementById('rule-conditions').value = formatTerms(rule.conditionTerms);
    document.getElementById('rule-allergies').value = formatTerms(rule.allergyTerms);
    document.getElementById('rule-symptoms').value = formatTerms(rule.symptomTerms);
    document.getElementById('rule-risk-flags').value = formatTerms(rule.riskFlagTerms);
    document.getElementById('rule-message').value = rule.message || '';
    document.getElementById('rule-action').value = rule.suggestedAction || '';
    setRuleFormStatus('');
    setRulesMode('form');
    document.getElementById('rule-name').focus();
}

function clearRuleForm() {
    document.getElementById('rule-editor-title').textContent = 'Add Rule';
    document.getElementById('rule-form').reset();
    document.getElementById('rule-id').value = '';
    document.getElementById('rule-enabled').checked = true;
    document.getElementById('rule-level').value = 'High';
    document.getElementById('rule-source').value = 'Custom rule';
    setRuleFormStatus('');
}

function collectRulePayload() {
    return {
        name: document.getElementById('rule-name').value.trim(),
        enabled: document.getElementById('rule-enabled').checked,
        level: document.getElementById('rule-level').value,
        category: document.getElementById('rule-category').value.trim(),
        source: document.getElementById('rule-source').value.trim() || 'Custom rule',
        medicationTerms: parseTerms(document.getElementById('rule-medications').value),
        conditionTerms: parseTerms(document.getElementById('rule-conditions').value),
        allergyTerms: parseTerms(document.getElementById('rule-allergies').value),
        symptomTerms: parseTerms(document.getElementById('rule-symptoms').value),
        riskFlagTerms: parseTerms(document.getElementById('rule-risk-flags').value),
        message: document.getElementById('rule-message').value.trim(),
        suggestedAction: document.getElementById('rule-action').value.trim()
    };
}

function parseTerms(value) {
    return value
        .split(',')
        .map(v => v.trim())
        .filter(Boolean)
        .filter((v, index, values) => values.findIndex(x => x.toLowerCase() === v.toLowerCase()) === index);
}

function formatTerms(values = []) {
    return values.join(', ');
}

async function saveRule(e) {
    e.preventDefault();
    const id = document.getElementById('rule-id').value;
    const payload = collectRulePayload();
    const method = id ? 'PUT' : 'POST';
    const url = id ? `${API_BASE}/Rules/${encodeURIComponent(id)}` : `${API_BASE}/Rules`;

    setRuleFormStatus('Saving...', 'info');
    try {
        pendingDeleteRuleId = '';
        await apiCall(url, {
            method,
            body: JSON.stringify(payload)
        });
        clearRuleForm();
        await loadRules({ force: true });
        showRuleList('Rule saved.', 'success');
    } catch (err) {
        setRuleFormStatus(err.message, 'error');
    }
}

function requestRuleDelete(id) {
    const rule = rulesCache.find(r => r.id === id);
    if (!rule) return;
    pendingDeleteRuleId = id;
    setRulesMode('list');
    renderRulesList();
    setRuleListStatus(`Confirm delete for "${rule.name}".`, 'info');
}

function cancelRuleDelete() {
    pendingDeleteRuleId = '';
    renderRulesList();
    setRuleListStatus('');
}

async function deleteRule(id) {
    try {
        await apiCall(`${API_BASE}/Rules/${encodeURIComponent(id)}`, { method: 'DELETE' });
        pendingDeleteRuleId = '';
        if (document.getElementById('rule-id').value === id) clearRuleForm();
        await loadRules({ force: true });
        showRuleList('Rule deleted.', 'success');
    } catch (err) {
        setRuleListStatus(err.message, 'error');
    }
}

function setRuleListStatus(message, type = '') {
    const status = document.getElementById('rules-list-status');
    if (!status) return;
    status.textContent = message;
    status.className = `form-status ${type ? `status-${type}` : ''}`;
}

function setRuleFormStatus(message, type = '') {
    const status = document.getElementById('rule-form-status');
    if (!status) return;
    status.textContent = message;
    status.className = `form-status ${type ? `status-${type}` : ''}`;
}

document.getElementById('rule-form')?.addEventListener('submit', saveRule);
document.getElementById('new-rule-btn')?.addEventListener('click', startNewRule);
document.getElementById('cancel-rule-btn')?.addEventListener('click', () => showRuleList());
document.getElementById('refresh-rules-btn')?.addEventListener('click', () => loadRules({ force: true }));
document.getElementById('rule-search')?.addEventListener('input', () => renderRulesList());
document.getElementById('rule-severity-filter')?.addEventListener('change', () => renderRulesList());
document.getElementById('rule-detail-back-btn')?.addEventListener('click', () => showRuleList());
document.getElementById('rule-detail-edit-btn')?.addEventListener('click', () => {
    if (selectedRuleId) editRule(selectedRuleId);
});
document.getElementById('rule-detail-delete-btn')?.addEventListener('click', () => {
    if (selectedRuleId) requestRuleDelete(selectedRuleId);
});

// Load drug list if on that tab initially
if (document.querySelector('.tab[data-tab="drug-lookup"]')?.classList.contains('active')) {
    loadDrugList();
}
