/* Shared dashboard chart logic — Admin & Home areas */
Chart.defaults.color = '#5B5875';

let periodMonth, periodYear, fromPeriodMonth, fromPeriodYear, storeFilter = '', omFilter = '', ocFilter = '', socFilter = '', odFilter = '', jobFilter = [];
let jobTitleChart, tenureChart, genderChart;

/* Small badge shown next to a chart/table title, telling the user whether
   the currently selected multi-period filter is reflected in THAT
   particular chart/table, or whether it always shows just the latest
   period regardless of the range. Only rendered once an actual multi-period
   selection is active — a single-period selection has nothing to clarify.
   `isRangeActive` is passed in explicitly (rather than assumed) because not
   every page uses the continuous From→To filter — the 90-Day page's
   discrete Year+Months picker computes its own "more than one month
   selected" condition and passes it here instead. */
function isPeriodRangeActive() {
    return !!(fromPeriodMonth && fromPeriodYear && periodMonth && periodYear
        && (fromPeriodMonth !== periodMonth || fromPeriodYear !== periodYear));
}

/* Localized UI strings for this file come from data-* attributes on <body>,
   set from the resx in each area's _Layout.cshtml (same handoff pattern as
   notifications.js). Fallbacks keep the file usable if an attribute is absent. */
const DASH_L = document.body.dataset;

function rangeFilterBadge(isRangeActive, affected) {
    if (!isRangeActive) return '';
    return affected
        ? '<span class="range-filter-badge range-filter-badge-on"><i class="bi bi-check-circle-fill"></i>' + (DASH_L.rangeBadgeOn || '') + '</span>'
        : '<span class="range-filter-badge range-filter-badge-off"><i class="bi bi-dash-circle"></i>' + (DASH_L.rangeBadgeOff || '') + '</span>';
}

async function fetchJson(url) {
    const r = await fetch(url);
    return r.ok ? r.json() : [];
}

function monthName(m, y) {
    return new Date(y, m - 1).toLocaleString('default', { month: 'long', year: 'numeric' });
}

function buildQuery() {
    const p = new URLSearchParams();
    if (periodMonth)     p.set('month',     periodMonth);
    if (periodYear)      p.set('year',      periodYear);
    if (fromPeriodMonth) p.set('fromMonth', fromPeriodMonth);
    if (fromPeriodYear)  p.set('fromYear',  fromPeriodYear);
    if (storeFilter)     p.set('store',     storeFilter);
    if (omFilter)        p.set('om',        omFilter);
    if (ocFilter)        p.set('oc',        ocFilter);
    if (socFilter)       p.set('soc',       socFilter);
    if (odFilter)        p.set('od',        odFilter);
    if (jobFilter.length) p.set('jobs', jobFilter.join(','));
    return p.toString();
}

async function loadPeriods() {
    const periods = await fetchJson('/api/dashboard/available-periods');
    const toSel   = document.getElementById('periodSelect');
    const fromSel = document.getElementById('fromPeriodSelect');
    if (!toSel) return;

    [toSel, fromSel].forEach(sel => {
        if (!sel) return;
        sel.innerHTML = '<option value="">Select Period</option>';
        periods.forEach(p => {
            const opt = document.createElement('option');
            opt.value = `${p.month}-${p.year}`;
            opt.textContent = monthName(p.month, p.year);
            sel.appendChild(opt);
        });
    });

    if (periods.length > 0) {
        toSel.value = `${periods[0].month}-${periods[0].year}`;
        periodMonth = periods[0].month;
        periodYear  = periods[0].year;

        if (fromSel) {
            fromSel.value = `${periods[0].month}-${periods[0].year}`;
            fromPeriodMonth = periods[0].month;
            fromPeriodYear  = periods[0].year;
        }

        if (document.getElementById('storeSelect')) await loadStores();
        if (document.getElementById('omSelect'))    await loadOperationManagers();
        if (document.getElementById('ocSelect'))    await loadOperationConsultants();
        if (document.getElementById('socSelect'))   await loadSeniorOperationConsultants();
        if (document.getElementById('odSelect'))    await loadOperationDirectors();
        if (document.getElementById('jobSelectButton')) await loadJobTitles();
        await loadAll();
    }
}

async function loadStores() {
    if (!periodMonth || !periodYear) return;
    const stores = await fetchJson(`/api/dashboard/stores?month=${periodMonth}&year=${periodYear}`);
    const sel = document.getElementById('storeSelect');
    if (!sel) return;
    const cur = sel.value;
    sel.innerHTML = '<option value="">All Stores</option>';
    stores.forEach(s => {
        const opt = document.createElement('option');
        opt.value = s.storeName;
        opt.textContent = s.storeName;
        sel.appendChild(opt);
    });
    if (cur) sel.value = cur;
}

async function loadOperationManagers() {
    if (!periodMonth || !periodYear) return;
    const managers = await fetchJson(`/api/dashboard/operation-managers?month=${periodMonth}&year=${periodYear}`);
    const sel = document.getElementById('omSelect');
    if (!sel) return;
    const cur = sel.value;
    sel.innerHTML = '<option value="">All Operation Managers</option>';
    managers.forEach(name => {
        const opt = document.createElement('option');
        opt.value = name;
        opt.textContent = name;
        sel.appendChild(opt);
    });
    if (cur) sel.value = cur;
}

async function loadOperationConsultants() {
    if (!periodMonth || !periodYear) return;
    const consultants = await fetchJson(`/api/dashboard/operation-consultants?month=${periodMonth}&year=${periodYear}`);
    const sel = document.getElementById('ocSelect');
    if (!sel) return;
    const cur = sel.value;
    sel.innerHTML = '<option value="">All Operation Consultants</option>';
    consultants.forEach(name => {
        const opt = document.createElement('option');
        opt.value = name;
        opt.textContent = name;
        sel.appendChild(opt);
    });
    if (cur) sel.value = cur;
}

async function loadSeniorOperationConsultants() {
    if (!periodMonth || !periodYear) return;
    const socs = await fetchJson(`/api/dashboard/senior-operation-consultants?month=${periodMonth}&year=${periodYear}`);
    const sel = document.getElementById('socSelect');
    if (!sel) return;
    const cur = sel.value;
    sel.innerHTML = '<option value="">All Senior Operation Consultants</option>';
    socs.forEach(name => {
        const opt = document.createElement('option');
        opt.value = name;
        opt.textContent = name;
        sel.appendChild(opt);
    });
    if (cur) sel.value = cur;
}

async function loadOperationDirectors() {
    if (!periodMonth || !periodYear) return;
    const ods = await fetchJson(`/api/dashboard/operation-directors?month=${periodMonth}&year=${periodYear}`);
    const sel = document.getElementById('odSelect');
    if (!sel) return;
    const cur = sel.value;
    sel.innerHTML = '<option value="">All Operation Directors</option>';
    ods.forEach(name => {
        const opt = document.createElement('option');
        opt.value = name;
        opt.textContent = name;
        sel.appendChild(opt);
    });
    if (cur) sel.value = cur;
}

async function loadJobTitles() {
    if (!periodMonth || !periodYear) return;
    const jobs = await fetchJson(`/api/dashboard/job-titles?month=${periodMonth}&year=${periodYear}`);
    const panel = document.getElementById('jobSelectPanel');
    if (!panel) return;
    const selected = new Set(jobFilter);
    panel.innerHTML = jobs.map(job => `
        <label class="job-filter-option">
            <input type="checkbox" value="${escapeHtml(job)}" ${selected.has(job) ? 'checked' : ''}>
            <span>${escapeHtml(job)}</span>
        </label>`).join('') || '<div class="job-filter-empty">No jobs available</div>';
    updateJobFilterLabel();
}

function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

function updateJobFilterLabel() {
    const label = document.getElementById('jobSelectLabel');
    const button = document.getElementById('jobSelectButton');
    if (!label || !button) return;
    const all = document.documentElement.dir === 'rtl' ? 'كل الوظائف' : 'All Jobs';
    label.textContent = jobFilter.length ? `${jobFilter.length} ${document.documentElement.dir === 'rtl' ? 'وظائف مختارة' : 'selected'}` : all;
    button.classList.toggle('has-selection', jobFilter.length > 0);
}

async function loadKpis() {
    const kpiEl = document.getElementById('kpiCards');
    if (!kpiEl) return;
    const data = await fetchJson('/api/dashboard/kpis?' + buildQuery());

    kpiEl.innerHTML = `
        <div class="kpi-card"><div class="kpi-icon"><i class="bi bi-people-fill"></i></div><div class="kpi-value">${data.totalHeadcount||0}</div><div class="kpi-label">${DASH_L.kpiHeadcount || ''}</div></div>
        <div class="kpi-card"><div class="kpi-icon text-success"><i class="bi bi-person-plus-fill"></i></div><div class="kpi-value text-success">${data.newHires||0}</div><div class="kpi-label">${DASH_L.kpiNewHires || ''}</div></div>
        <div class="kpi-card"><div class="kpi-icon text-danger"><i class="bi bi-person-dash-fill"></i></div><div class="kpi-value text-danger">${data.totalResignations||0}</div><div class="kpi-label">${DASH_L.kpiResignations || ''}</div></div>
        <div class="kpi-card"><div class="kpi-icon"><i class="bi bi-graph-up"></i></div><div class="kpi-value">${(data.turnoverRate||0).toFixed(1)}%</div><div class="kpi-label">${DASH_L.kpiTurnoverRate || ''}</div></div>
    `;
}

function mkChart(ref, id, cfg) {
    if (ref) ref.destroy();
    const ctx = document.getElementById(id);
    if (!ctx) return null;
    return new Chart(ctx.getContext('2d'), cfg);
}

async function loadCharts() {
    const q = buildQuery();
    const [jobTitle, tenure, gender] = await Promise.all([
        fetchJson('/api/dashboard/turnover-by-job-title?' + q),
        fetchJson('/api/dashboard/turnover-by-tenure?' + q),
        fetchJson('/api/dashboard/gender-breakdown?' + q),
    ]);

    const jobTitleLabels = jobTitle.map(d=>d.label);
    jobTitleChart = mkChart(jobTitleChart, 'jobTitleChart', {
        type: 'bar',
        data: { labels: jobTitleLabels, datasets: [{ data: jobTitle.map(d=>d.value), backgroundColor: ChartColors.colorsByLabel(jobTitleLabels, null), borderRadius: 4 }] },
        options: { indexAxis:'y', plugins:{legend:{display:false}, tooltip:{enabled:true, callbacks:{label: c => ` ${c.formattedValue} ${DASH_L.resignationsSuffix || ''}`}}, datalabels:{anchor:'center', align:'center', color:'#fff', font:{size:11, weight:'bold'}, formatter: v => v > 0 ? v : ''}}, scales:{x:{grid:{color:'#E4E2F5'},ticks:{color:'#5B5875'}},y:{grid:{display:false},ticks:{color:'#5B5875'}}} }
    });

    const tenureLabels = tenure.map(d=>d.label);
    tenureChart = mkChart(tenureChart, 'tenureChart', {
        type: 'bar',
        data: { labels: tenureLabels, datasets: [{ data: tenure.map(d=>d.value), backgroundColor: ChartColors.colorsByLabel(tenureLabels, null), borderRadius: 4 }] },
        options: { plugins:{legend:{display:false}, tooltip:{enabled:true, callbacks:{label: c => ` ${c.formattedValue} ${DASH_L.resignationsSuffix || ''}`}}, datalabels:{anchor:'center', align:'center', color:'#fff', font:{size:11, weight:'bold'}, formatter: v => v > 0 ? v : ''}}, scales:{x:{grid:{display:false},ticks:{color:'#5B5875'}},y:{grid:{color:'#E4E2F5'},ticks:{color:'#5B5875'}}} }
    });

    const genderLabels = gender.map(d=>d.label);
    genderChart = mkChart(genderChart, 'genderChart', {
        type: 'doughnut',
        data: { labels: genderLabels, datasets: [{ data: gender.map(d=>d.value), backgroundColor: ChartColors.colorsByLabel(genderLabels, ChartColors.GENDER_COLORS), borderWidth: 0 }] },
        options: { plugins:{legend:{position:'bottom', labels:{color:'#5B5875', padding:16}}, tooltip:{enabled:true, callbacks:{label: c => ` ${c.label}: ${c.formattedValue}`}}, datalabels:{color:'#fff', font:{size:11, weight:'700'}, textShadowBlur:4, textShadowColor:'rgba(0,0,0,0.4)', formatter: (v, c) => { const t = c.chart.data.datasets[0].data.reduce((a,b)=>a+b,0); return t > 0 && v > 0 ? Math.round(v / t * 100) + '%' : ''; }}} }
    });
}

async function loadAll() { await Promise.all([loadKpis(), loadCharts()]); }

async function resetFilters() {
    storeFilter = ''; omFilter = ''; ocFilter = ''; socFilter = ''; odFilter = ''; jobFilter = [];
    const stSel = document.getElementById('storeSelect'); if (stSel) stSel.value = '';
    const omSel = document.getElementById('omSelect');    if (omSel) omSel.value = '';
    const ocSel = document.getElementById('ocSelect');    if (ocSel) ocSel.value = '';
    const socSel = document.getElementById('socSelect');  if (socSel) socSel.value = '';
    const odSel = document.getElementById('odSelect');    if (odSel) odSel.value = '';
    document.querySelectorAll('#jobSelectPanel input[type="checkbox"]').forEach(input => input.checked = false);
    updateJobFilterLabel();
    const search = document.getElementById('storeCardSearch'); if (search) search.value = '';
    await loadPeriods();
}

const periodSel = document.getElementById('periodSelect');
if (periodSel) {
    periodSel.addEventListener('change', async function() {
        if (!this.value) return;
        const [m, y] = this.value.split('-');
        periodMonth = parseInt(m); periodYear = parseInt(y);
        // Keep the range valid: "From" can't be after "To".
        if (fromPeriodYear > periodYear || (fromPeriodYear === periodYear && fromPeriodMonth > periodMonth)) {
            fromPeriodMonth = periodMonth; fromPeriodYear = periodYear;
            const fromSel = document.getElementById('fromPeriodSelect');
            if (fromSel) fromSel.value = `${periodMonth}-${periodYear}`;
        }
        const stSel = document.getElementById('storeSelect');
        if (stSel) { storeFilter = ''; stSel.value = ''; await loadStores(); }
        if (document.getElementById('omSelect')) await loadOperationManagers();
        if (document.getElementById('ocSelect')) await loadOperationConsultants();
        if (document.getElementById('socSelect')) await loadSeniorOperationConsultants();
        if (document.getElementById('odSelect')) await loadOperationDirectors();
        if (document.getElementById('jobSelectButton')) {
            jobFilter = [];
            await loadJobTitles();
        }
        await loadAll();
    });
}

const fromPeriodSel = document.getElementById('fromPeriodSelect');
if (fromPeriodSel) {
    fromPeriodSel.addEventListener('change', async function() {
        if (!this.value) return;
        const [m, y] = this.value.split('-');
        fromPeriodMonth = parseInt(m); fromPeriodYear = parseInt(y);
        await loadAll();
    });
}

const storeSel = document.getElementById('storeSelect');
if (storeSel) {
    storeSel.addEventListener('change', async function() {
        storeFilter = this.value || '';
        await loadAll();
    });
}

const omSel = document.getElementById('omSelect');
if (omSel) {
    omSel.addEventListener('change', async function() {
        omFilter = this.value || '';
        await loadAll();
    });
}

const ocSel = document.getElementById('ocSelect');
if (ocSel) {
    ocSel.addEventListener('change', async function() {
        ocFilter = this.value || '';
        await loadAll();
    });
}

const socSel = document.getElementById('socSelect');
if (socSel) {
    socSel.addEventListener('change', async function() {
        socFilter = this.value || '';
        await loadAll();
    });
}

const odSel = document.getElementById('odSelect');
if (odSel) {
    odSel.addEventListener('change', async function() {
        odFilter = this.value || '';
        await loadAll();
    });
}

const jobButton = document.getElementById('jobSelectButton');
const jobPanel = document.getElementById('jobSelectPanel');
if (jobButton && jobPanel) {
    jobButton.addEventListener('click', event => {
        event.stopPropagation();
        jobPanel.hidden = !jobPanel.hidden;
        jobButton.setAttribute('aria-expanded', String(!jobPanel.hidden));
    });
    jobPanel.addEventListener('change', async event => {
        if (!event.target.matches('input[type="checkbox"]')) return;
        jobFilter = [...jobPanel.querySelectorAll('input:checked')].map(input => input.value);
        updateJobFilterLabel();
        await loadAll();
    });
    document.addEventListener('click', event => {
        if (!jobPanel.contains(event.target) && !jobButton.contains(event.target)) {
            jobPanel.hidden = true;
            jobButton.setAttribute('aria-expanded', 'false');
        }
    });
}

loadPeriods();
