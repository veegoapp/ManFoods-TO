module.exports = { title: 'Comparisons', blocks: [
  { where: 'Side menu  >  Comparisons' },
  { p: 'Use this page to see whether things got better or worse. It compares two periods side by side, Period A and Period B, each with its own independent filters. Every card, chart and table shows both periods together; the headline cards, Smart Insights and the store table also show the change between them (A minus B).' },

  { h2: 'Step 1 - Choose the two periods' }, { img: 'filters', w: 600 },
  { p: 'Period A and Period B each have their own year, months, store, Operation Manager, Operation Consultant, Senior Operation Consultant and Operation Director filters - they do not have to match. The "Same months as A, last year" button fills Period B with the same filters as Period A but one year earlier, which is the quickest way to compare year over year.' },

  { h2: 'Step 2 - Read the Smart Insights' }, { img: 'insights', w: 600 },
  { p: 'Up to three automatic cards: whether the Turnover Rate and the 90-Day Rate improved or worsened from Period B to Period A, and (only when both periods use All Stores) the store whose Turnover Rate moved the most.' },

  { h2: 'Step 3 - Compare the headline numbers' }, { img: 'kpis', w: 600 },
  { p: 'Headcount, New Hires, Resignations, Turnover Rate, Early Leavers and 90-Day Rate for Period A and Period B side by side, each with the change A minus B. Green means A is better than B (more hires, fewer resignations or leavers, lower rates); Headcount is not coloured. With several months selected, Headcount adds up each month\'s roster.' },
  { look: 'the Turnover Rate and 90-Day Rate changes first - they tell you immediately whether retention improved.' },

  { h2: 'Step 4 - Which stores got better or worse?' },
  { img: 'turnover_rate_by_store_a_vs_b', w: 600 },
  { p: 'Turnover Rate per store for Period A vs Period B, as paired bars. When neither period has a store selected, the chart shows the top 8 stores by combined A+B rate; if either period has a store selected, it shows a single Overall pair.' },
  { img: '90_day_rate_by_store_a_vs_b', w: 600 },
  { p: '90-Day Rate per store for Period A vs Period B, as paired bars (top 8 stores by combined A+B rate when neither period has a store selected, otherwise a single Overall pair), using the same months and filters as the comparison.' },
  { decide: 'stores where the Period B bar is much higher than Period A need a conversation with their Operation Consultant.' },

  { h2: 'Step 5 - Headcount and resignations' },
  { img: 'workforce_by_store_a_vs_b', w: 600 },
  { p: 'Active headcount per store for Period A vs Period B, as paired bars (top 8 stores by combined A+B; the months of a period are added together), or one overall pair when either period has a store selected.' },
  { img: 'resignations_count_by_store_a_vs_b', w: 600 },
  { p: 'Resignation count per store for Period A vs Period B (top 8 stores by combined A+B), or one overall pair when either period has a store selected. It shows counts, not a percentage.' },

  { h2: 'Step 6 - Are people staying longer?' }, { img: 'retention_milestones_a_vs_b', w: 600 },
  { p: 'Retention rate at the 6-month, 1-year and 2-5-year milestones for Period A vs Period B, based on employees hired in the selected months who are old enough (or already resigned) at each milestone; a milestone with no eligible hires has no bar.' },

  { h2: 'Step 7 - Why are people leaving?' },
  { img: 'reasons_for_leaving_a_vs_b', w: 600 },
  { p: 'The 8 most common reasons recorded in exit interviews (by combined A+B count), Period A vs Period B, so you can see whether the same reasons dominate or the mix shifted.' },
  { img: 'exit_interview_sentiment_a_vs_b', w: 600 },
  { p: 'Positive exit-interview sentiment (the positive share of the Would Return and Overall Experience answers) and how many interviews were submitted, Period A vs Period B.' },

  { h2: 'Step 8 - Store by store' }, { img: 'cmptable', w: 600 },
  { p: 'Every store with its Turnover Rate and 90-Day Rate in both periods, plus the change (A minus B) for each. It is shown only when both periods use "All Stores". Click a column header to sort.' },
] };
