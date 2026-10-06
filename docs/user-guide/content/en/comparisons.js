module.exports = { title: 'Comparisons', blocks: [
  { where: 'Side menu  >  Comparisons' },
  { p: 'Use this page to see whether things got better or worse. It compares two periods side by side, Period A and Period B, each with its own independent filters. Every card, chart and table shows both periods together plus the change between them.' },

  { h2: 'Step 1 - Choose the two periods' }, { img: 'filters', w: 600 },
  { p: 'Period A and Period B each have their own year, months, store, Operation Manager, Operation Consultant, Senior Operation Consultant and Operation Director filters - they do not have to match. "Copy to last year" fills Period B with the same filters as Period A but one year earlier, which is the quickest way to compare year over year.' },

  { h2: 'Step 2 - Read the Smart Insights' }, { img: 'insights', w: 600 },
  { p: 'Automatic sentences that summarise how Period A compares with Period B for your current filters.' },

  { h2: 'Step 3 - Compare the headline numbers' }, { img: 'kpis', w: 600 },
  { p: 'Headcount, New Hires, Resignations, Turnover Rate, Early Leavers and 90-Day Rate for Period A and Period B side by side, each with the change between them and whether that change is an improvement or not.' },
  { look: 'the Turnover Rate and 90-Day Rate changes first - they tell you immediately whether retention improved.' },

  { h2: 'Step 4 - Which stores got better or worse?' },
  { img: 'turnover_rate_by_store_a_vs_b', w: 600 },
  { p: 'Turnover Rate per store for Period A vs Period B, as paired bars. When no store is selected, the chart shows the top 8 stores by combined A+B rate.' },
  { img: '90_day_rate_by_store_a_vs_b', w: 600 },
  { p: '90-Day Rate per store for Period A vs Period B, as paired bars, using the same months and filters as the comparison.' },
  { decide: 'stores where the Period B bar is much higher than Period A need a conversation with their Operation Consultant.' },

  { h2: 'Step 5 - Headcount and resignations' },
  { img: 'workforce_by_store_a_vs_b', w: 600 },
  { p: 'Active headcount per store for Period A vs Period B, as paired bars - or the total headcount for both periods when a single store is selected.' },
  { img: 'resignations_count_by_store_a_vs_b', w: 600 },
  { p: 'Resignation count per store for Period A vs Period B. It shows counts, not a percentage.' },

  { h2: 'Step 6 - Are people staying longer?' }, { img: 'retention_milestones_a_vs_b', w: 600 },
  { p: 'Retention rate at the 6-month, 1-year and later milestones for Period A vs Period B, for hire cohorts old enough to be eligible at each milestone.' },

  { h2: 'Step 7 - Why are people leaving?' },
  { img: 'reasons_for_leaving_a_vs_b', w: 600 },
  { p: 'The most common reasons recorded in exit interviews, Period A vs Period B, so you can see whether the same reasons dominate or the mix shifted.' },
  { img: 'exit_interview_sentiment_a_vs_b', w: 600 },
  { p: 'Positive exit-interview sentiment and how many interviews were submitted, Period A vs Period B.' },

  { h2: 'Step 8 - Store by store' }, { img: 'cmptable', w: 600 },
  { p: 'Every store with its Turnover Rate and 90-Day Rate in both periods, plus the change between them. It is shown when both periods use "All Stores". Click a column header to sort.' },
] };
