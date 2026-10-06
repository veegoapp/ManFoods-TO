// One entry per portal page: where it lives and which elements get their own screenshot.
// sel   = CSS selector of an element inside the thing to shoot
// up    = climb to the nearest ancestor with this class (or tag, e.g. "details")
// each  = shoot every match separately (name is built from the element's label)
// maxH  = cap the height (long tables are cropped to their first rows)
const card = 'chart-card';
module.exports = {
  workforce: {
    url: '/home/dashboard/workforce', ready: '#ocTableBody tr',
    elements: [
      { name: 'filters', union: 'select, .reset-filters-btn' },
      { name: 'kpi', sel: '#kpiCards .kpi-card', each: '.kpi-label' },
      { name: 'jobtitle', sel: '#wfJobTitleChart', up: card },
      { name: 'tenure', sel: '#wfTenureChart', up: card },
      { name: 'payroll', sel: '#wfPayrollChart', up: card },
      { name: 'gender', sel: '#wfGenderChart', up: card },
      { name: 'trend', sel: '#wfHeadcountTrendChart', up: card },
      { name: 'stores', sel: '#wfStoreTableBody', up: card },
      { name: 'oc', sel: '#ocTableBody', up: card },
      { name: 'od', sel: '#odTableBody', up: card },
      { name: 'om', sel: '#omTableBody', up: card },
      { name: 'soc', sel: '#socTableBody', up: card },
    ],
  },
  workforceplanning: {
    url: '/home/dashboard/workforceplanning', ready: '#wpStoreRows tr', open: ['details'],
    elements: [
      { name: 'filters', sel: '#wpFilters' },
      { name: 'kpi', sel: '#wpKpis .kpi-card', each: '.kpi-label' },
      { name: 'insights', sel: '#wpInsights', up: card },
      { name: 'byjob', sel: '#wpJobChart', up: card },
      { name: 'trend', sel: '#wpTrendChart', up: card },
      { name: 'how', sel: '#wpHow', up: 'details' },
      { name: 'payroll_chart', sel: '#wpPayrollChart', up: card },
      { name: 'payroll_table', sel: '#wpPayrollBody', up: card },
      { name: 'stores', sel: '#wpStoreRows', up: card, maxH: 560 },
      { name: 'group_oc', sel: '#wpGrpBodyOc', up: card, maxH: 480 },
      { name: 'group_od', sel: '#wpGrpBodyOd', up: card },
      { name: 'group_om', sel: '#wpGrpBodyOm', up: card },
      { name: 'group_soc', sel: '#wpGrpBodySoc', up: card },
    ],
  },
  hiringforecast: {
    url: '/home/dashboard/hiringforecast', ready: '#hfBodyRows tr',
    elements: [
      { name: 'filters', sel: '#hfFilters' },
      { name: 'kpi', sel: '#hfKpis .kpi-card', each: '.kpi-label' },
      { name: 'insights', sel: '#hfInsights', up: card },
      { name: 'monthly_chart', sel: '#hfChart', up: card },
      { name: 'jobs', sel: '#hfJobChart', up: card, maxH: 900 },
      { name: 'table', sel: '#hfBodyRows', up: card, maxH: 560 },
      { name: 'group_oc', sel: '#hfGrpBodyOc', up: card, maxH: 480 },
      { name: 'group_od', sel: '#hfGrpBodyOd', up: card },
      { name: 'group_om', sel: '#hfGrpBodyOm', up: card },
      { name: 'group_soc', sel: '#hfGrpBodySoc', up: card },
    ],
  },
  // pages below use automatic element detection (see capture.js, def.auto)
  crewtrainers:      { url: '/home/dashboard/crewtrainers', auto: true },
  turnover:          { url: '/home/dashboard/turnover', auto: true },
  ninetydayturnover: { url: '/home/dashboard/ninetydayturnover', auto: true },
  comparisons:       { url: '/home/dashboard/comparisons', auto: true,
    filters: { name: 'filters', sel: '#cmpYearA', xpath: 'ancestor::div[contains(@class,"g-3")][1]' },
    extra: [{ name: 'insights', sel: '#cmpInsightContainer' }, { name: 'kpis', sel: '#cmpKpiGrid' }] },
  retention:         { url: '/home/dashboard/retention', auto: true },
  exitinterviews:    { url: '/home/dashboard/exitinterviews', auto: true },
  earlywarning:      { url: '/home/dashboard/earlywarning', auto: true },
  scorecard:         { url: '/home/dashboard/scorecard', auto: true },
  actioncenter:      { url: '/home/dashboard/actioncenter', auto: true },
  stores:            { url: '/home/dashboard/stores', auto: true, timeout: 150000, maxH: 560 },
};
