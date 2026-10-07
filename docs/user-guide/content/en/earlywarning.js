module.exports = { title: 'Early Warning', blocks: [
  { where: 'Side menu  >  Early Warning' },
  { p: 'Use this page to spot people who may be about to leave, before they actually do. It flags currently active employees who statistically resemble people who resigned in the past. Each flagged employee gets a 1-5 star risk score built from several weighted signals; employees with no matching signal are not listed. Higher stars mean higher predicted risk. Everyone flagged counts as "watch-listed" (cards and charts); those at 4-5 stars appear in the High-Risk table and the rest (1-3 stars) in the Watch List table.' },

  { h2: 'Step 1 - Choose your scope' }, { img: 'filters', w: 600 },
  { p: 'Choosing a Store or Job Title filters every card, chart and table to that selection. The Year/Months filter picks which monthly roster the risk scores are calculated on (the latest selected month). Because risk is about currently active employees, it is normally left on the latest period; the trend chart ignores it.' },

  { h2: 'Step 2 - Read the headline numbers' },
  { grid: ['kpi_in_watch_list', 'kpi_high_risk_2', 'kpi_still_in_first_90_days', 'kpi_avg_early_leave_rate_company'], perRow: 3, w: 190 },
  { look: 'High Risk (the people at 4-5 stars) first, then how many are still inside their role\'s new-hire window (90 days by default, 30-180 days by role). The fourth card shows the company-wide early-leave rate used as the baseline.' },

  { h2: 'Step 3 - Read the Smart Insights' }, { img: 'ewinsights', w: 600 },
  { p: 'Automatic takeaways worked out from the watch list for your current filters.' },

  { h2: 'Step 4 - What is driving the risk?' }, { img: 'risk_reasons_breakdown', w: 600 },
  { p: 'Which risk signal (reason) is driving flags most often across the watch list right now.' },
  { img: 'risk_score_distribution', w: 600 },
  { p: 'How many watch-listed employees fall into each star rating (1 through 5), so you can see the overall risk distribution.' },

  { h2: 'Step 5 - Where is the risk?' },
  { img: 'watchlist_by_store', w: 600 }, { p: 'Watch-listed employees grouped by store, so you can see which stores carry the most flight-risk headcount right now.' },
  { img: 'top_5_stores_high_risk', w: 600 }, { p: 'The 5 stores with the most highest-risk (4-5 star) employees.' },
  { img: 'watchlist_by_job_title', w: 600 }, { p: 'Watch-listed employees grouped by job title.' },
  { decide: 'start with the stores at the top of the high-risk chart and talk to their Operation Consultants.' },

  { h2: 'Step 6 - Is the watch list growing?' }, { img: 'watchlist_trend_over_time', w: 600 },
  { p: 'Two lines, the total watch list and the High Risk (4-5 star) count, for every available month. It ignores the Year/Months filter.' },

  { h2: 'Step 7 - Who needs attention first?' }, { img: 'ewhighrisk', w: 600 },
  { p: 'Employees at 4-5 stars - the most urgent list - with their store, Operation Consultant, role, hire date, tenure so far, star score and the specific reasons that flagged them.' },
  { decide: 'make sure someone speaks to each of these employees soon, and look at what the listed reasons suggest (for example a store or role with a history of early leavers).' },
  { img: 'ew', w: 600 },
  { p: 'The Watch List: every flagged employee at 1-3 stars (those at 4-5 stars are in the High-Risk table above, so no one is listed twice). It is sortable and searchable (name, store, consultant, job title), with the same columns as the High-Risk table.' },

  { h2: 'How the star rating is calculated' },
  { p: 'Every flagged employee starts at 0 points and gains points for each risk factor below that applies to them. The total (the risk score) is then mapped to a 1-5 star rating. 4-5 stars is what the High-Risk table and charts use. The history-based factors only use employees hired on or after 1 January 2026.' },
  { defs: { head: ['Risk score', 'Stars / level'], rows: [['1', '1 star - Low'], ['2 - 3', '2 stars - Low-Medium'], ['4', '3 stars - Medium'], ['5 - 6', '4 stars - High Risk'], ['7+', '5 stars - High Risk']] } },
  { defs: { head: ['Factor', 'Points and when it applies'], rows: [
    ['New Hire Window', '1-3 points: tenure falls within the role\'s new-hire window (30-180 days by role): under 40% of it elapsed = 1 point, 40-74% = 2, 75% or more = 3.'],
    ['Store History', '1-3 points: the store\'s 90-day early-leave rate is well above the average store (1, 1.5 or 2 standard deviations above it).'],
    ['Role History', '1-3 points: the job title\'s 90-day early-leave rate is well above the average job title (1, 1.5 or 2 standard deviations above it).'],
    ['Peak Resignation Window', '1-2 points: tenure is approaching (1 point) or inside (2 points) the store\'s/role\'s historical peak resignation window, which only applies after the first 90 days.'],
    ['Gender History', '1 point: the employee\'s gender group has a 90-day early-leave rate well above the average across gender groups.'],
    ['Exit Interview Score', '1-2 points: the store\'s exit interviews (at least 3) show above-average negative answers (workload, fairness, communication, training).'],
    ['Store Leader History', '1 point: the store leader\'s historical early-leave rate, across every store they have led, is well above average.'],
  ] } },
] };
