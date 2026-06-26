// ═══════════════════════════════════════════════════════════
// MedSafety – Frontend Application Logic
// ═══════════════════════════════════════════════════════════

const API_BASE = '/api';

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
        return await resp.json();
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

// ── Tab Navigation ───────────────────────────────────────
document.querySelectorAll('.tab').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
        document.querySelectorAll('.tab-content').forEach(tc => tc.classList.remove('active'));
        tab.classList.add('active');
        document.getElementById(`${tab.dataset.tab}-tab`).classList.add('active');

        // Load drug list when switching to drug-lookup
        if (tab.dataset.tab === 'drug-lookup') loadDrugList();
    });
});

// ══════════════════════════════════════════════════════════
// SAFETY SCREENING
// ══════════════════════════════════════════════════════════

document.getElementById('screening-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    if (proposedMedTags.values.length === 0) {
        alert('Please add at least one proposed medication.');
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
        proposedMedications: proposedMedTags.values
    };

    showLoading('screening-results');
    try {
        const result = await apiCall(`${API_BASE}/SafetyScreening/screen`, {
            method: 'POST',
            body: JSON.stringify(patient)
        });
        renderScreeningResults(result);
    } catch (err) {
        document.getElementById('screening-results').innerHTML =
            `<div class="card"><p style="color:var(--danger)">Error: ${escapeHtml(err.message)}</p></div>`;
    }
});

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

document.getElementById('clear-btn').addEventListener('click', () => {
    document.getElementById('screening-form').reset();
    allergyTags.clear();
    comorbidityTags.clear();
    complaintTags.clear();
    currentMedTags.clear();
    proposedMedTags.clear();
    document.getElementById('screening-results').style.display = 'none';
});

document.getElementById('demo-btn').addEventListener('click', () => {
    document.getElementById('patientId').value = 'PT-DEMO-001';
    document.getElementById('age').value = '68';
    document.getElementById('isPregnant').checked = false;
    document.getElementById('isBreastfeeding').checked = false;

    allergyTags.setValues(['Penicillin']);
    comorbidityTags.setValues(['Myasthenia Gravis', 'Diabetes', 'Hypertension']);
    complaintTags.setValues(['Community-acquired pneumonia']);
    currentMedTags.setValues(['Warfarin', 'Metformin', 'Lisinopril']);
    proposedMedTags.setValues(['Telithromycin', 'Ciprofloxacin', 'Azithromycin', 'Amoxicillin']);
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

// Load drug list if on that tab initially
if (document.querySelector('.tab[data-tab="drug-lookup"]')?.classList.contains('active')) {
    loadDrugList();
}
