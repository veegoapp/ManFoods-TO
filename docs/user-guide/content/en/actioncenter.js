module.exports = { title: 'Action Center', blocks: [
  { where: 'Side menu  >  Action Center' },
  { p: 'Use this page as your worklist of stores that need action. It scores every store you can access on a Store Health Index - a single 0-100 score where 100 means healthy - and lists the stores that tripped one of the 7 detection signals and got an Action Plan opened. This page always shows the current state of open and resolved plans, so there is no period filter.' },

  { h2: 'Step 1 - Read the headline numbers' },
  { grid: ['kpi_average_health_score', 'kpi_critical_stores', 'kpi_high_risk', 'kpi_active_plans', 'kpi_stalled_plans', 'kpi_resolved_this_month'], perRow: 3, w: 190 },
  { defs: { head: ['Card', 'What it shows'], rows: [
    ['Average Health Score', 'Company-wide store health (100 = healthy).'],
    ['Critical Stores', 'Highest weighted risk - act first.'],
    ['High Risk', 'Well above the company baseline.'],
    ['Active Plans', 'Stores with an open action plan.'],
    ['Stalled Plans', 'Open 45+ days with no task completed.'],
    ['Resolved This Month', 'Plans closed in the current month.'],
  ] } },
  { look: 'Critical Stores and Stalled Plans first.' },
  { decide: 'critical stores need action first; stalled plans need someone to follow up with the owner.' },

  { h2: 'Step 2 - Read the Smart Insights' }, { img: 'insights', w: 600 },
  { p: 'Automatic takeaways about the stores that need attention, worked out from the same data.' },

  { h2: 'Step 3 - What is dragging health down?' }, { img: 'risk_drivers_pillar_impact', w: 600 },
  { p: 'Risk Drivers show the average weighted contribution of each pillar to company-wide risk, so you can see which dimension (turnover, engagement, leadership...) is pulling health down across the network. The Store Health Index is built from six weighted pillars: Overall Turnover (24%), Early Attrition (20%), Engagement (18%), 6-Month Retention (16%), Workforce Outlook (12%) and Leadership Stability (10%).' },
  { img: 'root_cause_breakdown_exit_drivers', w: 600 },
  { p: 'The Root-Cause Breakdown shows the company-wide negative-response rate for each exit-interview dimension (fairness, workload, communication, training...). It replaces a vague "culture" label with the specific driver that leaving employees actually flagged.' },

  { h2: 'Step 4 - Which stores need action?' }, { img: 'ach', w: 600 },
  { p: 'The store scorecard: health score, tier, top driving pillar, the pillar strip, impact-weighted priority, headcount, plan status, the responsible person and task progress. Click a column header to sort, and use the search box to filter by store or responsible name. Open a store for its full breakdown and its Weighted Action Plan.' },
  { look: 'stores in the Critical tier, and their Top Driver.' },
  { decide: 'agree with the responsible person what will be done about the top driver, and check progress at your next visit.' },

  { h2: 'Step 5 - Are plans being opened and resolved?' }, { img: 'plans_opened_vs_resolved_last_6_months', w: 600 },
  { p: 'Plans opened against plans resolved over the last 6 months. If more plans open than close, open plans are piling up.' },

  { h2: 'How an Action Plan is created' },
  { p: 'An Action Plan opens automatically the first time any one of these 7 signals fires for a store that has no plan open, using that period\'s uploaded data:' },
  { defs: { head: ['Signal', 'When it fires'], rows: [
    ['High Overall Turnover', 'Headcount of 5 or more and overall turnover rate at or above 15%.'],
    ['Early Turnover (90 Days)', '3 or more hires in a non-provisional period and an early-leaver rate at or above 30%.'],
    ['Low 6-Month Retention', '3 or more hires and 6-month retention below 70%.'],
    ['Leadership Instability', '2 or more different Store Leaders recorded in the trailing 6 months.'],
    ['Low Exit Sentiment', '2 or more exit interviews with positive sentiment below 50%.'],
    ['Reason Concentration', '2 or more exit-interview reason responses where one reason is cited in 50% or more of them.'],
    ['Early-Warning Watchlist', '2 or more currently active employees flagged high-risk.'],
  ] } },
  { p: 'A store only ever has one Active plan at a time: a new signal firing while a plan is open is added to that same plan. Severity (Medium / High / Critical) is worked out from how many signals are on the plan. Each plan gets a Target Resolution Date when it opens - 90 days out, or 30 days if it is already Critical - and resolves automatically once its store shows no signals for 2 consecutive evaluated periods. See the Action Plan Guide, next, for the full explanation.' },
] };
