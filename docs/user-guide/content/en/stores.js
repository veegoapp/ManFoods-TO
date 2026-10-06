module.exports = { title: 'Stores', blocks: [
  { where: 'Side menu  >  Stores' },
  { p: 'Use this page as a directory of every store. Each store has a card showing its headcount, turnover rate, resignations, Operation Consultant / Operation Manager and a health status at a glance, for the selected period.' },

  { h2: 'Step 1 - Choose the period and your scope' }, { img: 'filters', w: 600 },
  { p: 'The period selector picks which month\'s snapshot is shown. Choosing an Operation Manager, Operation Consultant, Senior Operation Consultant or Operation Director filters the cards to just their stores. The Sort dropdown and the search box do not filter data - they only change how the cards are ordered and found.' },

  { h2: 'Step 2 - Read the company-wide totals' },
  { grid: ['kpi_total_stores', 'kpi_total_headcount', 'kpi_total_resignations', 'kpi_avg_turnover_rate', 'kpi_critical_high_stores', 'kpi_stores_with_high_risk_staff'], perRow: 3, w: 190 },
  { p: 'Totals for the selected period across every store: number of stores, headcount, resignations and the average turnover rate, plus how many stores have a Critical or High severity action plan and how many have high-risk staff on the Early Warning list.' },

  { h2: 'Step 3 - Read the Smart Insights' }, { img: 'insights', w: 600 },
  { p: 'Automatic takeaways: the best performing store, the store needing the most attention, how many stores have a Critical or High action plan, the change in average turnover against the previous period, and the Early Warning signals.' },

  { h2: 'Step 4 - Browse the store cards' }, { img: 'cards_row', w: 600 },
  { p: 'One card per store. Each card shows:' },
  { bullets: [
    'the store name, its number of employees and a health badge (Good / Watch / Critical) based on its turnover rate against the configured thresholds;',
    'the turnover rate, with the change against the previous period;',
    'its Operation Consultant / Operation Manager and its resignations;',
    'badges such as the action plan severity and the number of high-risk employees;',
    'when the store has a projection for the selected month, its staffing: actual headcount as a percentage of projected headcount - green (95-100%), amber (85-95%), red (below 85%) or blue (above 100%).',
  ] },
  { p: 'If the store has an open action plan, the card links to that store\'s Action Center detail.' },
  { look: 'cards marked Critical, and a staffing figure in red.' },
  { decide: 'sort the cards by Turnover Rate (high to low), which is the default, and start with the first row.' },
] };
