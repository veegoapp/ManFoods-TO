/* Shared dashboard chart logic — Admin & Home areas */
Chart.defaults.color = '#5B5875';

let periodMonth, periodYear, storeFilter = '';
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
    if (periodMonth) p.set('month', periodMonth);
    if (periodYear)  p.set('year',  periodYear);
    if (storeFilter) p.set('store', storeFilter);
    return p.toString();
}

async function loadPeriods() {
    const periods = await fetchJson('/api/dashboard/available-periods');
    const sel = document.getElementById('periodSelect');
    if (!sel) return;
    sel.innerHTML = '<option value="">Select Period</option>';
    periods.forEach(p => {
        const opt = document.createElement('option');
        opt.value = `${p.month}-${p.year}`;
        opt.textContent = monthName(p.month, p.year);
        sel.appendChild(opt);
    });
    if (periods.length > 0) {
        sel.value = `${periods[0].month}-${periods[0].year}`;
        periodMonth = periods[0].month;
        periodYear  = periods[0].year;
        if (document.getElementById('storeSelect')) await loadStores();
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

async function loadKpis() {
    const kpiEl = document.getElementById('kpiCards');
    if (!kpiEl) return;
    const data = await fetchJson('/api/dashboard/kpis?' + buildQuery());
    kpiEl.innerHTML = `
        <div class="kpi-card"><div class="kpi-icon"><i class="bi bi-people-fill"></i></div><div class="kpi-value">${data.totalHeadcount||0}</div><div class="kpi-label">Total Headcount</div></div>
        <div class="kpi-card"><div class="kpi-icon text-success"><i class="bi bi-person-plus-fill"></i></div><div class="kpi-value text-success">${data.newHires||0}</div><div class="kpi-label">New Hires</div></div>
        <div class="kpi-card"><div class="kpi-icon text-danger"><i class="bi bi-person-dash-fill"></i></div><div class="kpi-value text-danger">${data.totalResignations||0}</div><div class="kpi-label">Resignations</div></div>
        <div class="kpi-card"><div class="kpi-icon"><i class="bi bi-graph-up"></i></div><div class="kpi-value">${(data.turnoverRate||0).toFixed(1)}%</div><div class="kpi-label">Turnover Rate</div></div>
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
        storeFilter = '';
        const stSel = document.getElementById('storeSelect');
        if (stSel) { stSel.value = ''; await loadStores(); }
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

loadPeriods();
