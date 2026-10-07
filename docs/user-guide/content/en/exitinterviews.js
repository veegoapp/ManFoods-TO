module.exports = { title: 'Exit Interviews', blocks: [
  { where: 'Side menu  >  Exit Interviews' },
  { p: 'Use this page to understand why people leave, in aggregate. It summarises what departing employees said in their exit interviews: reasons for leaving, whether they would return, ratings on training, fairness and workload, and free-text comments, for the selected period (based on the employee\'s resignation date, or the form\'s completion date when no resignation is matched). Individual responses are never identified.' },

  { h2: 'Step 1 - Choose the period and your scope' }, { img: 'filters', w: 600 },
  { p: 'Choosing a Store, Store Leader, Operation Consultant, Operation Manager or Job Title filters every chart, table and comment on this page to just that selection. The Year/Months filter scopes everything to that period, except the reasons-trend chart, which always shows the full history.' },

  { h2: 'Step 2 - Read the headline numbers' },
  { grid: ['kpi_exit_interviews', 'kpi_positive_sentiment', 'kpi_top_reason_for_leaving', 'kpi_would_return'], perRow: 3, w: 190 },
  { look: 'Positive Sentiment and Would Return - together they tell you how people feel when they leave.' },

  { h2: 'Step 3 - Read the Smart Insights' }, { img: 'insights', w: 600 },
  { p: 'Up to four automatic takeaways worked out from the exit interviews for your current filters: overall positive sentiment, the leading reason for leaving, and the weakest and strongest engagement drivers. Positive Sentiment is the positive share of the Would Return and Overall Experience answers.' },

  { h2: 'Step 4 - Why do people leave?' }, { img: 'reasons_for_leaving', w: 600 },
  { p: 'The reasons employees gave for resigning, ranked by how often each was cited.' },
  { decide: 'the top reasons are where your retention effort should go first.' },
  { img: 'reasons_for_leaving_over_time', w: 600 },
  { p: 'How the 5 most common resignation reasons (all-time) have trended month over month. It ignores the Year/Months and Job Title filters but follows Store, Store Leader, Operation Consultant and Operation Manager.' },

  { h2: 'Step 5 - Would they come back?' }, { img: 'would_they_return', w: 600 },
  { p: 'Whether departing employees said they would consider returning to work here in the future.' },
  { img: 'reason_vs_return', w: 600 },
  { p: 'For the 6 most common reasons (each with at least 2 interviews), the share of people who answered Yes to returning. It shows which reasons tend to be "final" and which are more circumstantial.' },

  { h2: 'Step 6 - How did they rate their experience?' },
  { img: 'overall_experience', w: 600 }, { p: 'How departing employees rated their overall experience working here.' },
  { img: 'received_adequate_training', w: 600 }, { p: 'How departing employees rated the training they received.' },
  { img: 'fair_treatment', w: 600 }, { p: 'How departing employees rated whether they were treated fairly.' },
  { img: 'workload_conditions', w: 600 }, { p: 'How departing employees rated their workload and working conditions.' },
  { img: 'reason_for_workload_pressure', w: 600 },
  { p: 'A breakdown of the specific work-pressure reasons written in the exit interviews (interviews with no work-pressure answer are not counted).' },

  { h2: 'Step 7 - Who is leaving?' }, { img: 'exit_interviews_by_job_title', w: 600 },
  { p: 'Exit interview responses grouped by the departing employee\'s job title.' },

  { h2: 'Step 8 - Engagement drivers' }, { img: 'drivers_chart', w: 600 }, { img: 'drivers_list', w: 600 },
  { p: 'The share of people who answered positively on each engagement driver, lowest first, shown as a chart and as the same list. Bars are green at 70%+ positive, amber at 40-69% and red below 40%. The drivers at the top need the most attention.' },
  { decide: 'pick the lowest one or two drivers and agree with the Operation Consultant what will change in the stores concerned.' },

  { h2: 'Step 9 - Read what people wrote' }, { img: 'comments', w: 560 },
  { p: 'The free-text comments departing employees left (reasons given as "other", work-pressure reason, what they would change, what they learned, final comments), with store and store leader, searchable by text, store or question and filterable by question.' },
  { img: 'what_would_change', w: 560 },
  { p: 'What departing employees said they would change about the job or store, newest first (click Store or Store Leader to re-sort); the same answers also appear in the Comments table above, under the question "What They\'d Change".' },
] };
