module.exports = { title: 'Reports', blocks: [
  { where: 'Side menu  >  Reports' },
  { p: 'Use this page to download Excel reports. It is a catalog of every downloadable report, grouped by section. Click any card to open that report\'s own page, where you pick a period and filters before downloading. This index page itself has no filters - it is just a directory.' },

  { h2: 'Store Operations' }, { img: 'section_1', w: 600 },
  { defs: { head: ['Report', 'What it contains'], rows: [
    ['Action Center', 'Per-store action-plan status: severity, signals, age, ownership and task progress for every accessible store.'],
    ['Stores Overview', 'Every store\'s headcount, turnover, Action Center status and Early Warning high-risk count for the selected month - the single cross-page view of store health.'],
    ['Crew Trainers', 'Trainers against the plan and the 1-trainer-per-6-crew rule for the selected month, by store and by the people responsible, the year\'s trend, and who joined or left the trainer list.'],
  ] } },

  { h2: 'Workforce & Hiring' }, { img: 'section_2', w: 600 },
  { defs: { head: ['Report', 'What it contains'], rows: [
    ['Workforce', 'Active workforce composition for the selected month (headcount by job title, payroll group, tenure and gender, gender count by store) plus the headcount trend over time.'],
    ['Workforce Planning', 'Projected vs actual headcount by job and store: summary, by store, by job, by consultant and manager, the full data sheet and ready-made Excel pivot tables.'],
    ['Workforce Planning - Detailed Data', 'The full flat data sheet (one row per store, job and month) and the pivot tables behind the Workforce Planning report. It is large: download it only when you need to analyse the raw rows.'],
    ['Hiring Forecast', 'Hires needed per month for a year, by store, job, payroll group and by the people responsible for the stores, with the roster and forecast months marked.'],
  ] } },

  { h2: 'Turnover' }, { img: 'section_3', w: 600 },
  { defs: { head: ['Report', 'What it contains'], rows: [
    ['Turnover', 'Company-wide turnover trend across every uploaded period, the latest period broken down by store, the full resignation list, and aggregated breakdowns by job title and tenure.'],
    ['Turnover Trend Matrix', 'One row per store and one column per month, showing Turnover % across all available periods from the selected year onward, with an average column.'],
    ['90-Day Turnover', 'Cohort trend, full list of early leavers, by-store rates and aggregated reasons across all available periods.'],
    ['90-Day Trend Matrix', 'One row per store and one column per hire-cohort month, showing the 90-day early-leave rate across all available cohorts, with an average column.'],
  ] } },

  { h2: 'Comparison' }, { img: 'section_4', w: 600 },
  { defs: { head: ['Report', 'What it contains'], rows: [
    ['Comparison', 'Side-by-side Period A vs Period B: headcount, new hires, resignations, turnover rate and 90-day early-leave rate, company-wide and per store.'],
    ['OC / OM Comparison', 'Stores, headcount, resignations and average turnover rate rolled up by Operation Consultant, Operation Manager, Senior Operation Consultant and Operation Director.'],
  ] } },

  { h2: 'Performance & Risk' }, { img: 'section_5', w: 600 },
  { defs: { head: ['Report', 'What it contains'], rows: [
    ['Retention', 'Milestone rates (90 days to 5 years), survival curve, multi-year trend, store leaderboard and workforce tenure distribution.'],
    ['Scorecard', 'KPI rankings for Store Leaders, Operation Consultants and Operation Managers: turnover, 90-day, retention and exit sentiment.'],
    ['Early Warning', 'At-risk employee watchlist with star risk scores (7 scoring criteria), flagged reasons, hire date and tenure, filterable by period, store and operations leadership.'],
  ] } },

  { h2: 'Exit Interviews' }, { img: 'section_6', w: 600 },
  { defs: { head: ['Report', 'What it contains'], rows: [
    ['Exit Interviews Report', 'Reasons for leaving, engagement drivers, workload ratings, overall experience and anonymous comments, aggregated across all periods matching the selected filters.'],
  ] } },
  { decide: 'open the report you need, choose the period and filters on its page, and download it.' },

  { h2: 'Inside a report (Report Detail)', pb: true },
  { p: 'Click a report card to open that report\'s own page. It generates one downloadable Excel workbook. It does not show charts on screen: the filters on the page control exactly what data is written into the Excel file when you download it, so the export matches what you need. Use "Back" at the top to return to the list.' },
  { img: 'reportdetail/header', w: 600 },
  { img: 'reportdetail/filters', w: 600 },
  { p: 'Which filters appear depends on the report: a period or year/months selector, Store / Operation Manager / Operation Consultant / Senior Operation Consultant / Operation Director and Job title filters, or, for comparison-style reports, two independent Period A / Period B panels. Leave a filter empty for "all". Reset Filters clears them.' },
  { img: 'reportdetail/download', w: 600 },
  { p: 'Whatever you set is built into the Excel file the moment you click Download Excel. To get a different slice of the data, change a filter and download again.' },
  { note: 'some reports are large (for example Workforce Planning - Detailed Data). Download those only when you need the raw rows.' },
] };
