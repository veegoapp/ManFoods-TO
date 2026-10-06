module.exports = { title: 'Scorecard', blocks: [
  { where: 'Side menu  >  Scorecard' },
  { p: 'Use this page to compare Store Leaders. It scores every Store Leader (and rolls that up by Operation Consultant and Operation Manager) on four metrics: Turnover Rate, Early Leaver (90-Day) Rate, 180-Day Retention Rate and Exit Sentiment, so leadership performance can be ranked and tracked over time, not just store performance.' },

  { h2: 'Step 1 - Choose the period and your reporting line' }, { img: 'filters', w: 600 },
  { p: 'Choosing an Operation Manager, Operation Consultant, Senior Operation Consultant or Operation Director filters the ranking and the rollups to just their reporting line. The Year/Months filter sets which period\'s data feeds every metric on the page.' },

  { h2: 'Step 2 - Read the headline numbers' },
  { grid: ['kpi_store_leaders', 'kpi_total_headcount', 'kpi_average_turnover', 'kpi_above_average'], perRow: 3, w: 190 },
  { look: 'Average Turnover and how many leaders are Above Average.' },

  { h2: 'Step 3 - Read the Smart Insights' }, { img: 'scinsights', w: 600 },
  { p: 'Automatic takeaways, such as the best performing leader, worked out from the ranking below.' },

  { h2: 'Step 4 - Rank the Store Leaders' }, { img: 'sc', w: 600 },
  { p: 'Every Store Leader ranked by Turnover Rate, Early Leaver Rate, 180-Day Retention Rate and Exit Sentiment side by side. It is sorted with the worst Turnover Rate first by default - click any column header to re-sort, and use the search box to find a leader.' },
  { p: 'Click a Store Leader to see their turnover rate trend over time across every store they have led, including a marker where they were transferred between stores, so a change in rate can be told apart from a change of store.' },
  { look: 'leaders who are weak on several of the four metrics at once, not just one.' },
  { decide: 'agree support or coaching with the Operation Consultant of the leaders at the top of the list.' },

  { h2: 'Step 5 - Who oversees the weakest leaders?' },
  { img: 'by_operation_consultant', w: 600 }, { img: 'by_operation_manager', w: 600 }, { img: 'by_senior_operation_consultant', w: 600 }, { img: 'by_operation_director', w: 600 },
  { p: 'For each Operation Consultant, Operation Manager, Senior Operation Consultant and Operation Director: how many Store Leaders they oversee, how many of those leaders are "flagged" (performing poorly on the ranking above) and what percentage that represents.' },
] };
