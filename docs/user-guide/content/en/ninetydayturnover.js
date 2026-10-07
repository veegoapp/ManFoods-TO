module.exports = { title: '90-Day Turnover', blocks: [
  { where: 'Side menu  >  90-Day Turnover' },
  { p: 'Use this page to see how many new hires leave within their first 90 days. It tracks "early leavers": employees hired in a given month (a cohort) who resigned within their first 90 days. The 90-Day Rate = early leavers / total new hires in that cohort x 100. A cohort is marked "Provisional" until 90 days after the end of its hiring month (for several months: the latest one), because its final rate can still change.' },

  { h2: 'Step 1 - Choose the cohort and your scope' }, { img: 'filters', w: 600 },
  { p: 'The month selector picks which hiring cohort(s) to look at. Everything on the page, except the trend chart and the Full Data Table (both always show every cohort), is scoped to the cohort(s) you select. Choosing a Store, Operation Manager, Operation Consultant, Senior Operation Consultant, Operation Director or Job Title filters every chart and table to just that selection.' },

  { h2: 'Step 2 - Read the headline numbers' },
  { grid: ['kpi_hired_this_cohort', 'kpi_left_within_90_days', 'kpi_90_day_turnover_rate'], perRow: 3, w: 190 },
  { look: 'the 90-Day Turnover Rate: early leavers divided by hires in the cohort.' },
  { decide: 'a high rate means many new people do not stay past their first three months - look at hiring quality, onboarding and training before looking at anything else.' },

  { h2: 'Step 3 - Which hiring months stick better?' }, { img: '90_day_rate_by_cohort_month', w: 600 },
  { p: 'The 90-Day Rate for each monthly hiring cohort (people hired that month), so you can compare how "sticky" different hiring months have been.' },
  { h2: 'Step 4 - Is the trend improving?' }, { img: '90_day_turnover_trend', w: 600 },
  { p: 'The 90-Day Rate for every monthly hiring cohort since the first hires in the data. It ignores the Year and Months selection, but still follows the Store, manager and Job Title filters. Dashed segments are cohorts that are still provisional.' },

  { h2: 'Step 5 - Which stores lose their new hires?' }, { img: 'by_store', w: 600 },
  { p: 'The 90-Day Rate for each store in the selected cohort(s). The chart shows the top 10 stores by early-leaver rate; the full comparison is in the table below.' },
  { img: 'store_table', w: 600 },
  { p: 'Every store with its total hires, early leavers and 90-Day Rate for the selected cohort(s). Click a column header to sort.' },
  { decide: 'talk to the Operation Consultant of the stores at the top and review how new hires are welcomed and trained there.' },

  { h2: 'Step 6 - Why do new hires leave?' }, { img: 'reasons_for_early_leaving', w: 600 },
  { p: 'The reasons given in exit interviews by employees who resigned within their first 90 days (early leavers with no exit interview are not counted).' },

  { h2: 'Step 7 - Who leaves early?' },
  { img: 'gender_breakdown', w: 600 }, { p: 'Gender split of the early leavers in the selected cohort(s).' },
  { img: 'by_job_title', w: 600 }, { p: 'Early leavers grouped by job title.' },
  { img: 'by_payroll_group', w: 600 }, { p: 'Early leavers grouped by payroll group.' },

  { h2: 'Step 8 - Who should you talk to?' },
  { img: 'operation_consultants', w: 600 }, { img: 'operation_directors', w: 600 }, { img: 'operation_managers', w: 600 }, { img: 'senior_operation_consultants', w: 600 },
  { p: 'Operation Consultants, Senior Operation Consultants, Operation Directors and Operation Managers ranked, highest 90-Day Rate first, by their stores\' combined rate (early leavers / hires across all their stores) for the selected cohort(s).' },

  { h2: 'Step 9 - Spot the pattern store by store' }, { img: 'matrix', w: 600 },
  { p: 'Every store\'s 90-Day Rate for each hiring month (all cohorts), one row per store and one column per month, plus a Total column that adds up the monthly rates. Use it to spot stores whose rates are consistently high, or improving or worsening over time. Search by store or Operation Consultant; click any column header to sort.' },

  { h2: 'Step 10 - Who exactly left?' }, { img: 'early_leavers', w: 600 },
  { p: 'The individual employees who resigned within their first 90 days in the selected cohort(s): name, store, job title, hire and resignation dates, and how many days they lasted. Click a column header to sort.' },
  { look: 'very short tenures (a few days) - these usually point to a problem in hiring or the first-day experience rather than in the job itself.' },
] };
