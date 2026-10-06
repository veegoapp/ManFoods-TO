module.exports = { title: 'Stores', blocks: [
  { where: 'Side menu  >  Stores' },
  { p: 'Use this page as a directory of every store. Each store has a card showing its headcount, turnover rate, resignations, Operation Consultant / Operation Manager and a health status at a glance, for the selected period.' },

  { h2: 'Step 1 - Choose the period and your scope' }, { img: 'filters', w: 600 },
  { p: 'The period selector picks which month\'s snapshot is shown. Choosing an Operation Manager, Operation Consultant, Senior Operation Consultant or Operation Director filters the cards to just their stores. The Sort dropdown and the search box do not filter data - they only change how the cards are ordered and found.' },

  { h2: 'Step 2 - Read the company-wide totals' },
  { grid: ['kpi_total_stores', 'kpi_total_headcount', 'kpi_total_resignations', 'kpi_avg_turnover_rate', 'kpi_critical_high_stores', 'kpi_stores_with_high_risk_staff'], perRow: 3, w: 190 },
  { p: 'Totals for the selected period across every store: number of stores, headcount, resignations and the average turnover rate, plus how many stores have a Critical or High severity action plan and how many have high-risk staff on the Early Warning list.' },

  { h2: 'Step 3 - Read the Smart Insights' }, { img: 'insights', w: 600 },
  { p: 'Automatic takeaways: the best performing store, the store needing the most attention, how many stores have a Critical or High action plan, the change in average turnover against the previous period, and the Early Warning signals.' },

  { h2: 'Step 4 - Browse the store cards' }, { img: 'cards_row', w: 600 },
  { p: 'One card per store. Each card shows:' },
  { bullets: [
    'the store name, its number of employees and a health badge (Good / Watch / Critical) based on its turnover rate against the configured thresholds;',
    'the turnover rate, with the change against the previous period;',
    'its Operation Consultant / Operation Manager and its resignations;',
    'badges such as the action plan severity and the number of high-risk employees;',
    'when the store has a projection for the selected month, its staffing: actual headcount as a percentage of projected headcount - green (95-100%), amber (85-95%), red (below 85%) or blue (above 100%).',
  ] },
  { p: 'If the store has an open action plan, the card links to that store\'s Action Center detail.' },
  { look: 'cards marked Critical, and a staffing figure in red.' },
  { decide: 'sort the cards by Turnover Rate (high to low), which is the default, and start with the first row.' },
  { h2: 'Inside a store (Store Profile)', pb: true },
  { p: 'Click a store card to open the store\'s own page. It is a 360-degree profile for one store: use it to understand the store\'s current workforce position, turnover and early-leaver pattern, risk signals, exit-interview themes and action-plan progress without leaving the store. Use "Back" at the top to return to the list.' },
  { img: 'storeprofile/hero', w: 600 },
  { p: 'The From and To selectors at the top right set the period for the profile\'s KPIs and analyses. Changing tabs keeps the same store and period.' },
  { h2: 'Branch Health Command Center' }, { img: 'storeprofile/health', w: 600 },
  { p: 'A decision-ready summary built from the selected period\'s current numbers together with the store\'s action-plan baseline, recurring signals and high-risk employees: the overall status (for example Critical), the priority issues, the current position, the action progress and the signal history. The status is a decision aid, not a new metric or data source.' },
  { img: 'storeprofile/kpis', w: 600 },
  { p: 'Headline numbers for the store and period: employees, resignations, turnover rate, Early Warning watchlist and exit sentiment.' },
  { look: 'the status and the "Priority issues" list first.' },
  { img: 'storeprofile/tabs', w: 600 },
  { p: 'The tabs below split the profile into the same topics you already know from the main pages.' },

  { h2: 'Overview tab' },
  { img: 'storeprofile/overview_1_turnover_trend', w: 600 }, { img: 'storeprofile/overview_2_headcount_trend', w: 600 },
  { p: 'The store\'s Turnover Trend, and next to it the Headcount Trend over the same history.' },
  { img: 'storeprofile/overview_3_risk_summary', w: 600 }, { img: 'storeprofile/overview_4_quick_insights', w: 600 },
  { p: 'A short Risk Summary (Early Warning watchlist, high-risk employees and the action plan) and a few auto-generated Quick Insights worth a first look.' },

  { h2: 'Workforce Overview tab' },
  { img: 'storeprofile/workforce_1_headcount_by_job_title', w: 600 }, { img: 'storeprofile/workforce_2_staffing_plan', w: 600 },
  { p: 'Who is currently in the store and how the team is distributed: headcount by job title, payroll group, tenure and gender. The Staffing Plan card compares the store\'s projected headcount with its actual headcount by job title (gap = actual - projected: + is a surplus, - a shortage) for the end of the selected period, and lists the projected headcount for the coming months. It appears only when a job projection has been uploaded for this store.' },
  { img: 'storeprofile/workforce_3_headcount_by_payroll_group', w: 600 }, { img: 'storeprofile/workforce_4_headcount_by_tenure', w: 600 }, { img: 'storeprofile/workforce_5_headcount_by_gender', w: 600 },

  { h2: 'Turnover tab' },
  { img: 'storeprofile/turnover_1_turnover_by_job_title', w: 600 }, { img: 'storeprofile/turnover_2_turnover_by_tenure', w: 600 }, { img: 'storeprofile/turnover_3_turnover_by_payroll_group', w: 600 },
  { img: 'storeprofile/turnover_4_exit_reasons', w: 600 }, { img: 'storeprofile/turnover_5_monthly_turnover_by_period', w: 600 },
  { p: 'The store\'s resignation pattern across the selected period and its available history. Compare the current rate with the trend and the action-plan baseline before deciding whether the issue is new or recurring.' },

  { h2: 'Retention tab' },
  { img: 'storeprofile/retention_1_retention_by_tenure', w: 600 }, { img: 'storeprofile/retention_2_retention_by_job_title', w: 600 }, { img: 'storeprofile/retention_3_retention_by_gender', w: 600 }, { img: 'storeprofile/retention_4_hiring_trend', w: 600 },
  { p: 'How long this store\'s current team stays: the tenure-band mix, retention by job title and gender, and the monthly hiring trend. It is the flip side of the Turnover tab.' },

  { h2: '90-Day Turnover tab' },
  { img: 'storeprofile/ninety_1_90_day_early_leaver_rate', w: 600 }, { img: 'storeprofile/ninety_2_monthly_90_day_turnover', w: 600 },
  { p: 'Employees who resigned within their first 90 days. It helps distinguish an onboarding or early-tenure pattern from broader store turnover.' },

  { h2: 'Early Warning tab' }, { img: 'storeprofile/warning_1_early_warning_watchlist', w: 600 },
  { p: 'Who in the store is on the watchlist, with the job title, the risk score and the reasons behind the score. Use it to decide which employees to speak to first.' },

  { h2: 'Exit Interviews tab' },
  { img: 'storeprofile/exit_1_exit_reasons', w: 600 }, { img: 'storeprofile/exit_2_would_return', w: 600 }, { img: 'storeprofile/exit_3_overall_experience', w: 600 }, { img: 'storeprofile/exit_4_workload', w: 600 }, { img: 'storeprofile/exit_5_training', w: 600 }, { img: 'storeprofile/exit_6_monthly_exit_interviews', w: 600 },
  { p: 'Resignation reasons, return intent, experience ratings and monthly volume for this store and the selected period. Use it to add context to the numbers on the other tabs.' },

  { h2: 'Action Center tab' }, { img: 'storeprofile/action_1_action_plan', w: 600 },
  { p: 'The store\'s active or resolved Action Plan: its recommendations, completion progress, notes, metric snapshots, signal history and baseline changes. It is the follow-through view for issues identified elsewhere in the profile.' },

  { h2: 'Store Leaders tab' }, { img: 'storeprofile/leaders_1_store_leaders', w: 600 },
  { p: 'Every Store Leader who has run this store, the period each led it and how many months that covers. The current leader is marked, so you can line up a leadership change with a shift in the store\'s numbers.' },
  { decide: 'when a store looks Critical, read the Overview and Turnover tabs first, then check Early Warning and Store Leaders to find what changed.' },
] };
