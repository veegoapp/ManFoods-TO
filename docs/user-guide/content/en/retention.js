module.exports = { title: 'Retention', blocks: [
  { where: 'Side menu  >  Retention' },
  { p: 'Use this page to see how long employees stay - the opposite lens from Turnover. Most charts are snapshots of today\'s active team: how long people have been on the job, using 6 months (180 days) as the main benchmark. Two parts look at the past instead: the Smart Insights (hire-cohort retention) and the all-time "how soon people leave" chart.' },

  { h2: 'Step 1 - Choose the hires and your scope' }, { img: 'filters', w: 600 },
  { p: 'Choosing a Store, Operation Manager, Operation Consultant, Senior Operation Consultant, Operation Director or Job Title filters every chart and table to just that selection. The Year/Months filter only affects the Smart Insights (which hires they look at); every chart shows the current team from the latest roster, except the monthly hiring chart, which starts at the selected year.' },

  { h2: 'Step 2 - Read the Smart Insights' }, { img: 'rtinsights', w: 600 },
  { p: 'Up to four automatic takeaways: whether 1-year retention of recent hiring months is improving or slipping, the best store (and the weakest, if under 50%) for 1-year retention among employees hired in the selected months, and the share of today\'s team with more than a year of service. Read these first, then use the charts below.' },

  { h2: 'Step 3 - How experienced is today\'s team?' },
  { grid: ['kpi_6_months', 'kpi_6_12_months', 'kpi_1_2_years', 'kpi_2_3_years', 'kpi_3_4_years', 'kpi_4_5_years', 'kpi_5_years'], perRow: 3, w: 190 },
  { p: 'Each card shows the share of today\'s active headcount currently in a tenure band (under 6 months, 6-12 months, 1-2 years ... 5+ years); the bands add up to 100%. It is a snapshot of team experience, not a survival rate, and it ignores the Months filter.' },
  { look: 'how large the "< 6 months" card is compared with the longer-tenure cards.' },
  { decide: 'a large new-joiner share means more coaching and closer follow-up in the coming months.' },

  { h2: 'Step 4 - Where do people drop off?' }, { img: 'team_tenure_curve', w: 600 },
  { p: 'The Team Tenure Curve shows, for each tenure mark (Day 0, 1, 3, 6 months, 1, 1.5, 2, 3, 4 and 5 years), the percentage of today\'s active employees who have been on the job at least that long. It describes the current team, not hires over time, and it ignores the Months filter.' },

  { h2: 'Step 5 - Which stores keep their people?' },
  { img: 'best_5_stores_for_retention', w: 600 }, { p: 'The 5 stores with the highest share of their current team that has 6+ months on the job.' },
  { img: 'worst_5_stores_for_retention', w: 600 }, { p: 'The 5 stores with the lowest share of their current team that has 6+ months on the job - worth a closer look.' },
  { decide: 'visit or call the stores in the worst-five list, and learn what the best-five stores do differently.' },

  { h2: 'Step 6 - Who stays?' },
  { img: 'retention_by_gender', w: 600 }, { p: 'Share of the current team with 6+ months on the job, split by gender.' },
  { img: 'retention_by_job_title_half', w: 600 }, { p: 'Share of each job title\'s current team that has been on the job 6+ months.' },

  { h2: 'Step 7 - Average tenure' },
  { img: 'best_5_stores_by_average_tenure', w: 600 }, { img: 'worst_5_stores_by_average_tenure', w: 600 },
  { p: 'The 5 stores with the highest and the 5 with the lowest average tenure of their current team, in months.' },
  { img: 'best_5_operation_consultants_by_average_tenure', w: 600 }, { img: 'worst_5_operation_consultants_by_average_tenure', w: 600 },
  { p: 'The 5 Operation Consultants with the highest and the 5 with the lowest average tenure across their stores\' current teams.' },

  { h2: 'Step 8 - When do people leave, and how much are we hiring?' },
  { img: 'how_soon_people_leave', w: 600 },
  { p: 'Of everyone who ever resigned (all-time, not limited to the selected period), how long they had been on the job before leaving. It shows whether people tend to leave early or after settling in.' },
  { img: 'monthly_hiring_volume', w: 600 },
  { p: 'New hires per month from the selected year onward (everyone hired that month, including people who have since left; it ignores the Months filter) - context for reading the retention charts alongside how much hiring was actually happening.' },

  { h2: 'Step 9 - Store by store' }, { img: 'rttenuretable', w: 600 },
  { p: 'Every store\'s headcount on the latest roster and a tenure-band mix (how many employees fall in each tenure range), so you can see whether a store\'s team is mostly new or mostly experienced.' },

  { h2: 'Step 10 - Who should you talk to?' },
  { img: 'operation_consultants', w: 600 }, { img: 'operation_directors', w: 600 }, { img: 'operation_managers', w: 600 }, { img: 'senior_operation_consultants', w: 600 },
  { p: 'Operation Consultants, Senior Operation Consultants, Operation Directors and Operation Managers ranked, longest average tenure first, by the average tenure (in months) of the current team across all their stores.' },
] };
