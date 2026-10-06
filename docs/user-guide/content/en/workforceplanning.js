module.exports = { title: 'Workforce Planning', blocks: [
  { where: 'Side menu  >  Workforce Planning' },
  { p: 'Use this page to see whether you have the people you planned for. It compares the headcount you planned (the job projection) with the headcount that actually works for you, by job title and by store, for the month you pick. It shows where you are short, by how much, and roughly how many hires are needed. Only stores that have a projection for the month are compared.' },

  { h2: 'Step 1 - Choose the month and your scope' }, { img: 'filters', w: 600 },
  { p: 'Year and Month pick the period (only months that have a projection are listed). Store, Job title and the Operation Consultant / Manager / Senior Consultant / Director selectors narrow every number, chart and table on the page. You only see the stores you have access to.' },
  { note: 'for a future month there is no active-employee roster yet, so the page shows the projection only.' },

  { h2: 'Step 2 - Read the headline numbers' },
  { grid: ['kpi_projected_headcount', 'kpi_actual_headcount', 'kpi_gap', 'kpi_fill_rate', 'kpi_shortage', 'kpi_hiring_need_est'], perRow: 3, w: 190 },
  { defs: { head: ['Card', 'What it shows'], rows: [
    ['Projected Headcount', 'The planned number of employees for the selected month.'],
    ['Actual Headcount', 'Active employees in the same stores for that month.'],
    ['Gap', 'Actual minus projected. A positive (+) number is a surplus, a negative number is a shortage.'],
    ['Fill Rate', 'Actual as a share of projected headcount.'],
    ['Shortage', 'The people missing in the jobs that are below their plan. A surplus in one job never covers another.'],
    ['Hiring Need (est.)', 'The estimated hires required: the shortage plus the expected resignations.'],
  ] } },
  { look: 'Fill Rate and Shortage first, then Gap.' },
  { decide: 'a low Fill Rate means you are short. Treat Hiring Need as your first estimate of how many people to recruit. Remember that the Gap can hide real shortages, because a job above its plan offsets a job below it - the Shortage card does not.' },

  { h2: 'Step 3 - Read the Smart Insights' }, { img: 'insights', w: 600 },
  { p: 'Automatic takeaways worked out from the numbers on this page for your current filters: overall staffing, the most understaffed store, the biggest shortage by job, how many stores are below plan, the estimated hiring need, the consultant with the biggest shortage, stores above projection and next month\'s plan. They update whenever you change a filter.' },
  { decide: 'read these first for a quick summary, then use the charts and tables below to dig into each point.' },

  { h2: 'Step 4 - Which jobs are short?' }, { img: 'byjob', w: 600 },
  { p: 'Planned and actual headcount side by side for each job title. Hover a bar to see the gap, fill rate and hiring need for that job.' },
  { look: 'jobs where the actual bar is clearly lower than the planned bar.' },
  { decide: 'those are the jobs to prioritise in recruitment.' },

  { h2: 'Step 5 - How is reality tracking against the plan?' }, { img: 'trend', w: 600 },
  { p: 'Projected headcount for every month of the selected year (dashed line) and actual headcount for the months that already have an uploaded roster (solid line).' },
  { look: 'whether the solid line is moving towards the dashed line or away from it.' },

  { h2: 'Step 6 - Payroll groups' }, { img: 'payroll_chart', w: 600 }, { img: 'payroll_table', w: 600 },
  { p: 'Planned and actual headcount for each payroll group, with the same numbers in a table you can sort by clicking a header. A job that nobody works in yet has no known payroll group and is listed as Unassigned.' },

  { h2: 'Step 7 - Which stores need attention?' }, { img: 'stores', w: 600 },
  { p: 'Every store with a projection for the month, largest shortage first. Click a column header to sort (click again to reverse). The search box also matches the consultant\'s name. Click a store to open its jobs and see where its numbers come from.' },
  { defs: { head: ['Status', 'Fill rate'], rows: [
    ['On track', '95-100% filled'],
    ['Watch', '85-95% filled'],
    ['Understaffed', 'below 85% filled'],
    ['Overstaffed', 'above 100% filled'],
  ] }, widths: [2600, 6800] },
  { look: 'stores marked Understaffed, then check their Shortage and Hiring need.' },
  { decide: 'open the store to see which jobs are missing people, and discuss a hiring plan with its Operation Consultant.' },
  { p: 'The numbers are calculated like this:' },
  { img: 'how', w: 600 },

  { h2: 'Step 8 - Who should you talk to?' },
  { img: 'group_oc', w: 600 }, { img: 'group_od', w: 600 }, { img: 'group_om', w: 600 }, { img: 'group_soc', w: 600 },
  { p: 'The same figures rolled up by the people responsible for the stores: Operation Consultants, Directors, Managers and Senior Consultants. Each row shows how many stores it covers, their projected and actual headcount, the gap, the shortage, the expected resignations, the hiring need and a bar showing the fill rate.' },
  { defs: { head: ['Bar colour', 'Fill rate'], rows: [
    ['Green', '95-100%'], ['Amber', '85-95%'], ['Red', 'below 85%'], ['Blue', 'above 100%'],
  ] } },
  { decide: 'start with the people whose bar is red, then filter the page by that person to see their stores.' },
] };
