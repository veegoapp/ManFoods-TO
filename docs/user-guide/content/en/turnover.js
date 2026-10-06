module.exports = { title: 'Turnover', blocks: [
  { where: 'Side menu  >  Turnover' },
  { p: 'Use this page to understand who is leaving and where. It tracks how many employees resigned and how that compares with headcount, broken down by month, job title, tenure, payroll group, gender and store. The cards at the top summarise the selected period; everything below drills into it.' },

  { h2: 'Step 1 - Choose the period and your scope' }, { img: 'filters', w: 600 },
  { p: 'Pick a Store, Operation Manager, Operation Consultant, Senior Operation Consultant or Operation Director and every chart and table on the page switches to that selection. The From/To period narrows the cards, the job title / tenure / payroll / gender / reasons charts and the store table.' },
  { note: 'the "Turnover Rate by Month" chart, the "Monthly Turnover Trend" chart and the full matrix always show the complete history from the selected year onward, because they are built to show trends over time.' },

  { h2: 'Step 2 - Read the headline numbers' },
  { grid: ['kpi_resignations', 'kpi_turnover_rate', 'kpi_trend_vs_prior_period'], perRow: 3, w: 190 },
  { defs: { head: ['Card', 'What it shows'], rows: [
    ['Resignations', 'Total resignations in the selected period.'],
    ['Turnover Rate', 'Resignations divided by the average headcount for the period.'],
    ['Trend vs Prior Period', 'The change in turnover rate compared with the equivalent prior period.'],
  ] } },
  { look: 'Turnover Rate and its trend. A green downward arrow means turnover improved against the prior period.' },

  { h2: 'Step 3 - Is turnover seasonal?' }, { img: 'turnover_rate_by_month', w: 600 },
  { p: 'Turnover rate for every calendar month on record. Each bar = (resignations that month / average headcount that month) x 100. This chart always shows the full history and ignores the From/To filter, so you can see long-term seasonality.' },
  { look: 'months that regularly stand out.' },
  { decide: 'prepare extra hiring and retention effort ahead of the months that are usually worst.' },

  { h2: 'Step 4 - Who is leaving?' },
  { img: 'turnover_by_job_title', w: 600 },
  { p: 'Resignations in the selected period grouped by job title.' },
  { look: 'which roles are losing the most people.' },
  { img: 'turnover_by_tenure', w: 600 },
  { p: 'Resignations grouped by how long the employee had worked (under 3 months, 3-6 months, and so on).' },
  { look: 'whether people leave early or after a longer stay.' },
  { img: 'turnover_by_payroll_group', w: 600 },
  { p: 'Resignations grouped by payroll group for the selected period.' },
  { img: 'gender_breakdown', w: 600 },
  { p: 'Gender split of the resignations in the selected period, with counts and percentages on the side.' },

  { h2: 'Step 5 - Why are they leaving?' }, { img: 'reasons_for_leaving', w: 600 },
  { p: 'Every resignation in the selected period grouped by the reason the employee gave for leaving.' },
  { decide: 'the biggest reason is where your retention effort should go first.' },

  { h2: 'Step 6 - Is the trend improving?' }, { img: 'monthly_turnover_trend', w: 600 },
  { p: 'Month-by-month turnover trend from the selected year to the latest upload, whatever To period you pick.' },

  { h2: 'Step 7 - Which stores lose the most people?' }, { img: 'store_table', w: 600 },
  { p: 'Every store in the selected period with its headcount, new hires, resignations and turnover rate (resignations / average headcount x 100). Click a column header to sort.' },
  { look: 'stores with the highest Turnover% and with resignations well above their new hires.' },
  { decide: 'talk to the Operation Consultant of the stores at the top and agree actions with them.' },

  { h2: 'Step 8 - Who should you talk to?' },
  { img: 'operation_consultants', w: 600 }, { img: 'operation_directors', w: 600 }, { img: 'operation_managers', w: 600 }, { img: 'senior_operation_consultants', w: 600 },
  { p: 'Operation Consultants, Directors, Managers and Senior Consultants ranked by their weighted average turnover rate: the sum of resignations across all their stores divided by the sum of average headcount across those stores - not a simple average of each store\'s rate.' },

  { h2: 'Step 9 - Spot the pattern store by store' }, { img: 'matrix', w: 600 },
  { p: 'A full matrix of every store against every period from the selected year to the latest upload. It always shows the full year range, not just the From/To filter.' },
  { look: 'stores with a high rate in many months in a row - they have a lasting problem, not a one-off.' },
] };
