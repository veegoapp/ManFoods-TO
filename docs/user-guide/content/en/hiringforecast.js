module.exports = { title: 'Hiring Forecast', blocks: [
  { where: 'Side menu  >  Hiring Forecast' },
  { p: 'Use this page to plan recruitment ahead. It shows how many people each store needs to hire in each month of the year. The figures are an estimate based on past resignations and the projection - they are not a hiring order, and they change as new employee lists and projections are uploaded.' },

  { h2: 'Step 1 - Choose your scope' }, { img: 'filters', w: 600 },
  { p: 'Year, Store, Job title and the Operation Consultant / Manager / Senior Consultant / Director selectors narrow the table and its totals. You only see the stores you have access to.' },
  { note: 'the Month filter focuses the summary cards, the Smart Insights and the monthly chart on one month. The table below always shows the whole year.' },

  { h2: 'Step 2 - Read the summary cards' },
  { grid: ['kpi_hires_needed_in_the_year', 'kpi_busiest_month', 'kpi_next_3_months', 'kpi_store_with_most_hires'], perRow: 3, w: 190 },
  { defs: { head: ['Card', 'What it shows'], rows: [
    ['Hires needed in the year', 'Total of all stores and months shown.'],
    ['Busiest month', 'The month with the most hires.'],
    ['Next 3 months', 'Hires needed in the coming three months (from this month on when you look at the current year).'],
    ['Store with most hires', 'The store with the highest total over the year. It only shows when the table is viewed by store.'],
  ] } },
  { decide: 'plan your recruitment capacity around the busiest month and the next three months.' },

  { h2: 'Step 3 - Read the Smart Insights' }, { img: 'insights', w: 600 },
  { p: 'A few plain sentences worked out from the table: the busiest month, whether the next 3 months are above or below the monthly average, whether monthly hiring rises or falls through the year, how concentrated the hiring is, the consultant whose stores need the most hires, and how many hires only replace early leavers. They follow your filters and the "View by" choice.' },

  { h2: 'Step 4 - When are the hires needed?' }, { img: 'monthly_chart', w: 600 },
  { p: 'Hires needed in each month. Dark bars are months that already have an uploaded employee list; light bars are forecast. Hover a bar to see the month and its number.' },
  { look: 'which months stand out, and how the light (forecast) bars compare with the dark ones.' },

  { h2: 'Step 5 - Which jobs do you need to hire for?' }, { img: 'jobs', w: 600 },
  { p: 'Hires needed by job title, following the filters above. Pick a month to see the jobs to hire for in that month; otherwise the whole year is shown. Jobs that need no hires are hidden.' },

  { h2: 'Step 6 - Read the main table' }, { img: 'table', w: 600 },
  { p: 'Each cell is the number of people to hire in that month to reach the projection. Use "View by" above the table to group the rows by store (with its Operation Consultant), job, payroll group or Operation Consultant - the totals stay the same, only the grouping changes.' },
  { bullets: [
    'One column per month and a Total at the end. The last row adds up every row.',
    'The darker the red, the more hires that month. A dash means the projection has no data for that month.',
    '"Roster" months use the uploaded employee list; "Forecast" months are estimated from the latest roster.',
    'Click a column title to sort, and use the search box to find a row.',
  ] },
  { decide: 'start with the stores at the top (largest total) and the darkest months, and agree a recruitment plan with the responsible Operation Consultant.' },
  { note: 'for months that already have an uploaded employee list, hires = shortage + expected resignations (per store and job, as on Workforce Planning). Later months are simulated from the latest list: people expected to resign leave, then hires fill up to that month\'s projection. Expected resignations already include people who leave in their first 90 days.' },

  { h2: 'Step 7 - Who is responsible?' },
  { img: 'group_oc', w: 520 }, { img: 'group_od', w: 520 }, { img: 'group_om', w: 520 }, { img: 'group_soc', w: 520 },
  { p: 'The same hires per month, rolled up by Operation Consultant, Director, Manager and Senior Consultant, with the number of stores each covers.' },
] };
