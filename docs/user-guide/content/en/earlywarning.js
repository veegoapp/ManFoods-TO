module.exports = { title: 'Early Warning', blocks: [
  { where: 'Side menu  >  Early Warning' },
  { p: 'Use this page to spot people who may be about to leave, before they actually do. It flags currently active employees who statistically resemble people who resigned in the past. Each employee gets a 1-5 star risk score built from several weighted signals. Higher stars mean higher predicted risk; employees above a threshold land on the High-Risk table and the fuller Watch List.' },

  { h2: 'Step 1 - Choose your scope' }, { img: 'filters', w: 600 },
  { p: 'Choosing a Store filters every chart and both tables to just that store. The Year/Months filter changes which snapshot in time the risk scores are calculated for. Because risk is about currently active employees, it is normally left on the latest period.' },

  { h2: 'Step 2 - Read the headline numbers' },
  { grid: ['kpi_in_watch_list', 'kpi_high_risk_2', 'kpi_still_in_first_90_days', 'kpi_avg_early_leave_rate_company'], perRow: 3, w: 190 },
  { look: 'High Risk (the people at 4-5 stars) first, then how many are still in their first 90 days.' },

  { h2: 'Step 3 - Read the Smart Insights' }, { img: 'ewinsights', w: 600 },
  { p: 'Automatic takeaways worked out from the watch list for your current filters.' },

  { h2: 'Step 4 - What is driving the risk?' }, { img: 'risk_reasons_breakdown', w: 600 },
  { p: 'Which risk signal (reason) is driving flags most often across the watch list right now.' },
  { img: 'risk_score_distribution', w: 600 },
  { p: 'How many watch-listed employees fall into each star rating (1 through 5), so you can see the overall risk distribution.' },

  { h2: 'Step 5 - Where is the risk?' },
  { img: 'watchlist_by_store', w: 600 }, { p: 'Watch-listed employees grouped by store, so you can see which stores carry the most flight-risk headcount right now.' },
  { img: 'top_5_stores_high_risk', w: 600 }, { p: 'Only the highest-risk (4-5 star) employees, grouped by store.' },
  { img: 'watchlist_by_job_title', w: 600 }, { p: 'Watch-listed employees grouped by job title.' },
  { decide: 'start with the stores at the top of the high-risk chart and talk to their Operation Consultants.' },

  { h2: 'Step 6 - Is the watch list growing?' }, { img: 'watchlist_trend_over_time', w: 600 },
  { p: 'How the size of the watch list has trended over time.' },

  { h2: 'Step 7 - Who needs attention first?' }, { img: 'ewhighrisk', w: 600 },
  { p: 'Employees at 4-5 stars - the most urgent list - with their store, Operation Consultant, role, hire date, tenure so far, star score and the specific reasons that flagged them.' },
  { decide: 'make sure someone speaks to each of these employees soon, and look at what the listed reasons suggest (for example a store or role with a history of early leavers).' },
  { img: 'ew', w: 600 },
  { p: 'The Watch List: everyone above the risk threshold who is not already at 4-5 stars (those are in the High Risk table above, so no one is listed twice). It is sortable and searchable, with the same columns as the High-Risk table.' },

  { h2: 'How the star rating is calculated' },
  { p: 'Every flagged employee starts at 0 points and gains points for each risk factor below that applies to them. The total (the risk score) is then mapped to a 1-5 star rating. 4-5 stars is what the High-Risk table and charts use.' },
  { defs: { head: ['Risk score', 'Stars / level'], rows: [['1', '1 star - Low'], ['2 - 3', '2 stars - Low-Medium'], ['4', '3 stars - Medium'], ['5 - 6', '4 stars - High Risk'], ['7+', '5 stars - High Risk']] } },
  { defs: { head: ['Factor', 'Points and when it applies'], rows: [
    ['New Hire Window', '1-3 points: tenure falls within the role\'s new-hire window (higher the closer to the end of it).'],
    ['Store History', '1-3 points: the store\'s 90-day early-leave rate is well above the company average.'],
    ['Role History', '1-3 points: the job title\'s 90-day early-leave rate is well above the company average.'],
    ['Peak Resignation Window', '1-2 points: tenure is approaching or inside the store\'s/role\'s historical peak resignation window.'],
    ['Gender History', '1 point: the employee\'s gender group has a 90-day early-leave rate well above average.'],
    ['Exit Interview Score', '1-2 points: the store\'s exit interviews show above-average negative sentiment.'],
    ['Store Leader History', '1 point: the store leader\'s historical early-leave rate, across every store they have led, is well above average.'],
  ] } },
] };
