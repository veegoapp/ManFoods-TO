/* Shared dashboard chart logic — Admin & Home areas */
Chart.defaults.color = '#5B5875';

let periodMonth, periodYear, fromPeriodMonth, fromPeriodYear, storeFilter = '', omFilter = '', ocFilter = '';
let jobTitleChart, tenureChart, genderChart;

const COLORS = ['#6D5DFB', '#A78BFA', '#1C1C27', '#7EB6FF', '#5B3FE0'];

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

let cachedTargets = null;
async function getTargets() {
    if (cachedTargets) return cachedTargets;
    cachedTargets = await fetchJson('/api/targets');
    return cachedTargets || {};
}

async function loadKpis() {
    const kpiEl = document.getElementById('kpiCards');
    if (!kpiEl) return;
    const [data, targets] = await Promise.all([fetchJson('/api/dashboard/kpis?' + buildQuery()), getTargets()]);

    let turnoverExtra = '';
    if (targets && targets.turnoverRateTarget != null) {
        const diff = (data.turnoverRate || 0) - targets.turnoverRateTarget;
        const color = diff <= 0 ? '#198754' : '#dc3545';
        const sign = diff > 0 ? '+' : '';
        turnoverExtra = `<div style="font-size:11px;color:${color};margin-top:2px">${sign}${diff.toFixed(1)}pt vs target ${targets.turnoverRateTarget.toFixed(1)}%</div>`;
    }

    kpiEl.innerHTML = `
        <div class="kpi-card"><div class="kpi-icon"><i class="bi bi-people-fill"></i></div><div class="kpi-value">${data.totalHeadcount||0}</div><div class="kpi-label">Total Headcount</div></div>
        <div class="kpi-card"><div class="kpi-icon text-success"><i class="bi bi-person-plus-fill"></i></div><div class="kpi-value text-success">${data.newHires||0}</div><div class="kpi-label">New Hires</div></div>
        <div class="kpi-card"><div class="kpi-icon text-danger"><i class="bi bi-person-dash-fill"></i></div><div class="kpi-value text-danger">${data.totalResignations||0}</div><div class="kpi-label">Resignations</div></div>
        <div class="kpi-card"><div class="kpi-icon"><i class="bi bi-graph-up"></i></div><div class="kpi-value">${(data.turnoverRate||0).toFixed(1)}%</div><div class="kpi-label">Turnover Rate</div>${turnoverExtra}</div>
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

    jobTitleChart = mkChart(jobTitleChart, 'jobTitleChart', {
        type: 'bar',
        data: { labels: jobTitle.map(d=>d.label), datasets: [{ data: jobTitle.map(d=>d.value), backgroundColor: '#6D5DFB', borderRadius: 4 }] },
        options: { indexAxis:'y', plugins:{legend:{display:false}}, scales:{x:{grid:{color:'#E4E2F5'},ticks:{color:'#5B5875'}},y:{grid:{display:false},ticks:{color:'#5B5875'}}} }
    });

    tenureChart = mkChart(tenureChart, 'tenureChart', {
        type: 'bar',
        data: { labels: tenure.map(d=>d.label), datasets: [{ data: tenure.map(d=>d.value), backgroundColor: '#A78BFA', borderRadius: 4 }] },
        options: { plugins:{legend:{display:false}}, scales:{x:{grid:{display:false},ticks:{color:'#5B5875'}},y:{grid:{color:'#E4E2F5'},ticks:{color:'#5B5875'}}} }
    });

    genderChart = mkChart(genderChart, 'genderChart', {
        type: 'doughnut',
        data: { labels: gender.map(d=>d.label), datasets: [{ data: gender.map(d=>d.value), backgroundColor: COLORS, borderWidth: 0 }] },
        options: { plugins:{legend:{position:'bottom', labels:{color:'#5B5875', padding:16}}} }
    });
}

async function loadAll() { await Promise.all([loadKpis(), loadCharts()]); }

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

loadPeriods();
