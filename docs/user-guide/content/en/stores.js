module.exports = { title: 'Stores', blocks: [
  { where: 'Side menu  >  Stores' },
  { p: 'Use this page as a directory of every store. Each store has a card showing its headcount, turnover rate, resignations, Operation Consultant and a health status at a glance, for the selected period.' },

  { h2: 'Step 1 - Choose the period and your scope' }, { img: 'filters', w: 600 },
  { p: 'The period selector picks which month\'s snapshot is shown. Choosing an Operation Manager, Operation Consultant, Senior Operation Consultant or Operation Director filters the cards to just their stores. The Sort dropdown only changes the order of the cards. The search box hides cards that do not match a store, Operation Consultant or Operation Manager name; neither changes the underlying figures.' },

  { h2: 'Step 2 - Read the company-wide totals' },
  { grid: ['kpi_total_stores', 'kpi_total_headcount', 'kpi_total_resignations', 'kpi_avg_turnover_rate', 'kpi_critical_high_stores', 'kpi_stores_with_high_risk_staff'], perRow: 3, w: 190 },
  { p: 'Totals for the selected period across every store: number of stores, headcount, resignations and the average turnover rate (the simple average of the store rates). Two further totals show the current position regardless of the period: how many stores have an active Critical or High severity action plan, and how many have high-risk (4-5 star) employees on the Early Warning list.' },

  { h2: 'Step 3 - Read the Smart Insights' }, { img: 'insights', w: 600 },
  { p: 'Automatic takeaways: the best performing store, the store needing the most attention, how many stores have a Critical or High action plan, the change in average turnover against the previous period, and the Early Warning signals. Best and worst store are picked only among stores with 3 or more employees.' },

  { h2: 'Step 4 - Browse the store cards' }, { img: 'cards_row', w: 600 },
  { p: 'One card per store. Each card shows:' },
  { bullets: [
    'the store name, its number of employees and a health badge (Healthy / Watch / Critical) based on its turnover rate against the thresholds configured in Settings;',
    'the turnover rate, with the change against the previous period;',
    'its Operation Consultant and its resignations;',
    'badges such as the action plan severity and the number of high-risk employees;',
    'when the store has a projection for the selected month, its staffing: actual headcount as a percentage of projected headcount - green (95-100%), amber (85-95%), red (below 85%) or blue (above 100%).',
  ] },
  { p: 'If the store has an open action plan, the card links to that store\'s Action Center detail.' },
  { look: 'cards marked Critical, and a staffing figure in red.' },
  { decide: 'sort the cards by Turnover Rate (high to low), which is the default, and start with the first row.' },
  { h2: 'Inside a store (Store Profile)', pb: true },
  { p: 'Click a store card to open the store\'s own page. It is a 360-degree profile for one store: use it to understand the store\'s current workforce position, turnover and early-leaver pattern, risk signals, exit-interview themes and action-plan progress without leaving the store. Use "Back" at the top to return to the list.' },
  { img: 'storeprofile/hero', w: 600 },
  { p: 'The To selector sets the month used for the headline numbers; the From and To selectors together set the range of the trend charts and monthly tables. Changing tabs keeps the same store and period.' },
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
  { p: 'Who is currently in the store and how the team is distributed: headcount by job title, payroll group, tenure and gender. The Staffing Plan card compares the store\'s projected headcount with its actual headcount by job title (gap = actual - projected: + is a surplus, - a shortage) for the end of the selected period, and lists the projected headcount for the coming months. If no job projection has been uploaded for this store for the selected month, the card shows "No projection for this store in the selected month".' },
  { img: 'storeprofile/workforce_3_headcount_by_payroll_group', w: 600 }, { img: 'storeprofile/workforce_4_headcount_by_tenure', w: 600 }, { img: 'storeprofile/workforce_5_headcount_by_gender', w: 600 },

  { h2: 'Turnover tab' },
  { img: 'storeprofile/turnover_1_turnover_by_job_title', w: 600 }, { img: 'storeprofile/turnover_2_turnover_by_tenure', w: 600 }, { img: 'storeprofile/turnover_3_turnover_by_payroll_group', w: 600 },
  { img: 'storeprofile/turnover_4_exit_reasons', w: 600 }, { img: 'storeprofile/turnover_5_monthly_turnover_by_period', w: 600 },
  { p: 'Turnover by job title, tenure and payroll group, the exit reasons, and a month-by-month table of headcount, resignations and turnover rate between the From and To periods. Compare the current rate with the trend and the action-plan baseline before deciding whether the issue is new or recurring.' },

  { h2: 'Retention tab' },
  { img: 'storeprofile/retention_1_retention_by_tenure', w: 600 }, { img: 'storeprofile/retention_2_retention_by_job_title', w: 600 }, { img: 'storeprofile/retention_3_retention_by_gender', w: 600 }, { img: 'storeprofile/retention_4_hiring_trend', w: 600 },
  { p: 'How long this store\'s current team stays: the tenure-band mix, retention by job title and gender, and the monthly hiring trend. It is the flip side of the Turnover tab.' },

  { h2: '90-Day Turnover tab' },
  { img: 'storeprofile/ninety_1_90_day_early_leaver_rate', w: 600 }, { img: 'storeprofile/ninety_2_monthly_90_day_turnover', w: 600 },
  { p: 'The store\'s 90-Day early-leaver rate for the selected period and a monthly table of the rate, new hires and early leavers. It helps distinguish an onboarding or early-tenure pattern from broader store turnover.' },

  { h2: 'Early Warning tab' }, { img: 'storeprofile/warning_1_early_warning_watchlist', w: 600 },
  { p: 'Who in the store is on the watchlist, with the job title, the risk score and the reasons behind the score. Use it to decide which employees to speak to first.' },

  { h2: 'Exit Interviews tab' },
  { img: 'storeprofile/exit_1_exit_reasons', w: 600 }, { img: 'storeprofile/exit_2_would_return', w: 600 }, { img: 'storeprofile/exit_3_overall_experience', w: 600 }, { img: 'storeprofile/exit_4_workload', w: 600 }, { img: 'storeprofile/exit_5_training', w: 600 }, { img: 'storeprofile/exit_6_monthly_exit_interviews', w: 600 },
  { p: 'Resignation reasons, return intent, overall experience, workload and training ratings, and a monthly table of positive sentiment and number of responses for this store and the selected period. Use it to add context to the numbers on the other tabs.' },

  { h2: 'Action Center tab' }, { img: 'storeprofile/action_1_action_plan', w: 600 },
  { p: 'A summary of the store\'s latest Action Plan: its status, severity and number of recommendations. For the full plan, open the store in the Action Center. Plan progress, recurring signals and the change from the plan baseline are shown in the Branch Health Command Center at the top.' },

  { h2: 'Store Leaders tab' }, { img: 'storeprofile/leaders_1_store_leaders', w: 600 },
  { p: 'Every Store Leader who has run this store, the period each led it and how many months that covers. The current leader is marked, so you can line up a leadership change with a shift in the store\'s numbers.' },
  { decide: 'when a store looks Critical, read the Overview and Turnover tabs first, then check Early Warning and Store Leaders to find what changed.' },
] };
