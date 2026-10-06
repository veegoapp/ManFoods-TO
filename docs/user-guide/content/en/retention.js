module.exports = { title: 'Retention', blocks: [
  { where: 'Side menu  >  Retention' },
  { p: 'Use this page to see how long employees stay - the opposite lens from Turnover. It is built around the 6-month retention milestone: for hires old enough to have reached (or resigned before) that milestone, are they still on the team? Employees hired too recently to have reached 180 days are excluded from milestone-based figures until they are eligible.' },

  { h2: 'Step 1 - Choose the hires and your scope' }, { img: 'filters', w: 600 },
  { p: 'The Year/Months filter picks which hire cohort(s) to analyse - retention is always measured from each employee\'s own hire date, not from the filtered period itself. Choosing a Store, Operation Manager, Operation Consultant, Senior Operation Consultant or Operation Director filters every chart and table to just that selection.' },

  { h2: 'Step 2 - Read the Smart Insights' }, { img: 'rtinsights', w: 600 },
  { p: 'Automatic takeaways about retention for your current filters. Read these first, then use the charts below to see where each one comes from.' },

  { h2: 'Step 3 - How experienced is today\'s team?' },
  { grid: ['kpi_6_months', 'kpi_6_12_months', 'kpi_1_2_years', 'kpi_2_3_years', 'kpi_3_4_years', 'kpi_4_5_years', 'kpi_5_years'], perRow: 3, w: 190 },
  { p: 'Each card shows the share of today\'s active headcount that has reached a tenure milestone (under 6 months, 6-12 months, 1-2 years and so on). It is a snapshot of team experience, not a survival rate.' },
  { look: 'how large the "< 6 months" card is compared with the longer-tenure cards.' },
  { decide: 'a large new-joiner share means more coaching and closer follow-up in the coming months.' },

  { h2: 'Step 4 - Where do people drop off?' }, { img: 'team_tenure_curve', w: 600 },
  { p: 'The survival curve shows, for each tenure milestone (for example 30, 60, 90, 180 days), what percentage of hires eligible for that milestone are still employed - where people tend to drop off.' },

  { h2: 'Step 5 - Which stores keep their people?' },
  { img: 'best_5_stores_for_retention', w: 600 }, { p: 'The stores with the best 6-month retention rate.' },
  { img: 'worst_5_stores_for_retention', w: 600 }, { p: 'The stores with the worst 6-month retention rate - worth a closer look.' },
  { decide: 'visit or call the stores in the worst-five list, and learn what the best-five stores do differently.' },

  { h2: 'Step 6 - Who stays?' },
  { img: 'retention_by_gender', w: 600 }, { p: 'Share of the current team with 6+ months on the job, split by gender.' },
  { img: 'retention_by_job_title', w: 600 }, { p: 'Share of each job title\'s current team that has been on the job 6+ months.' },

  { h2: 'Step 7 - Average tenure' },
  { img: 'best_5_stores_by_average_tenure', w: 600 }, { img: 'worst_5_stores_by_average_tenure', w: 600 },
  { p: 'The stores with the highest and lowest average tenure of their current team, in months.' },
  { img: 'best_5_operation_consultants_by_average_tenure', w: 600 }, { img: 'worst_5_operation_consultants_by_average_tenure', w: 600 },
  { p: 'The Operation Consultants with the highest and lowest average tenure across their stores\' current teams.' },

  { h2: 'Step 8 - When do people leave, and how much are we hiring?' },
  { img: 'how_soon_people_leave', w: 600 },
  { p: 'Of everyone who ever resigned (all-time, not limited to the selected period), how long they had been on the job before leaving. It shows whether people tend to leave early or after settling in.' },
  { img: 'monthly_hiring_volume', w: 600 },
  { p: 'New hires per month, all-time - context for reading the retention charts alongside how much hiring was actually happening.' },

  { h2: 'Step 9 - Store by store' }, { img: 'rttenuretable', w: 600 },
  { p: 'Every store\'s headcount and a tenure-band mix (how many employees fall in each tenure range), so you can see whether a store\'s team is mostly new or mostly experienced.' },

  { h2: 'Step 10 - Who should you talk to?' },
  { img: 'operation_consultants', w: 600 }, { img: 'operation_directors', w: 600 }, { img: 'operation_managers', w: 600 }, { img: 'senior_operation_consultants', w: 600 },
  { p: 'Operation Consultants, Directors and Managers ranked by the average tenure of employees across all their stores.' },
] };
