module.exports = { title: 'Workforce Overview', blocks: [
  { where: 'Side menu  >  Workforce Overview' },
  { p: 'Use this page to answer one question: who works for me right now, and is the team in good shape? It is a snapshot of your active headcount, so most charts here count current employees, not resignations (that is the Turnover page).' },

  { h2: 'Step 1 - Choose your scope' }, { img: 'filters', w: 600 },
  { p: 'Pick a Store, Operation Manager, Operation Consultant, Senior Operation Consultant, Operation Director or Job, and every chart and table on the page switches to that selection. Press Reset Filters to return to the full picture.' },
  { note: 'the From/To period changes the cards, the breakdown charts and the tables, but not the Headcount Over Time chart, which always shows the full history from the selected year.' },

  { h2: 'Step 2 - Read the headline numbers' },
  { grid: ['kpi_total_headcount', 'kpi_new_hires', 'kpi_resignations'], perRow: 3, w: 190 },
  { look: 'compare New Hires with Resignations.' },
  { decide: 'if resignations are higher than hires, your headcount is shrinking. Check the trend chart below to see whether this is new.' },

  { h2: 'Step 3 - Is the headcount going the right way?' }, { img: 'trend', w: 600 },
  { look: 'the direction of the line over the year.' },
  { decide: 'a falling line means you are losing people faster than you add them. Use the filters to find which store or manager the drop comes from.' },

  { h2: 'Step 4 - Is the team experienced enough?' }, { img: 'tenure', w: 600 },
  { look: 'how tall the "< 3 months" and "3-6 months" bars are compared with "2+ years".' },
  { decide: 'a large share of new people means more coaching and closer follow-up. A team that is mostly 2+ years is stable, but check that you are still hiring for the future.' },

  { h2: 'Step 5 - Who is on the team?' }, { img: 'jobtitle', w: 600 },
  { look: 'which roles carry most of your headcount, and whether a role you depend on looks thin.' },
  { img: 'payroll', w: 600 },
  { look: 'the split between payroll groups.' },
  { img: 'gender', w: 600 },
  { look: 'the gender split of the current team, with counts and percentages.' },

  { h2: 'Step 6 - Which stores need attention?' }, { img: 'stores', w: 600 },
  { look: "each store's headcount, with its gender and payroll group mix." },
  { decide: 'compare stores with each other. A store with a much smaller team than similar stores is worth a conversation with its manager.' },

  { h2: 'Step 7 - Who should you talk to first?' },
  { img: 'oc', w: 600 }, { img: 'od', w: 600 }, { img: 'om', w: 600 }, { img: 'soc', w: 600 },
  { look: "Turnover% next to each person's headcount. It is weighted across all their stores, and the coloured badge highlights the higher rates." },
  { decide: "start your conversations with the people whose Turnover% is highest, then filter the page by that person to see which of their stores drives it." },
] };
