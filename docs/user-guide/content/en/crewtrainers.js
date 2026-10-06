module.exports = { title: 'Crew Trainers', blocks: [
  { where: 'Side menu  >  Crew Trainers' },
  { p: 'Use this page to check whether your stores have enough Crew Trainers. It compares the trainers you have with the rule of one trainer for every 6 crew-level employees, and with the projection, and shows who joined or left the trainer list.' },

  { h2: 'Step 1 - Choose the month and your scope' }, { img: 'filters', w: 600 },
  { p: 'Year and Month pick one of the months that have an uploaded trainer list. Store and the Operation Consultant / Manager / Senior Consultant / Director selectors narrow every number and table on the page. You only see the stores you have access to.' },

  { h2: 'Step 2 - Read the summary cards' },
  { grid: ['kpi_trainers_vs_the_rule', 'kpi_crew_level', 'kpi_trainers_vs_plan', 'kpi_trainers_who_resigned'], perRow: 3, w: 190 },
  { defs: { head: ['Card', 'What it shows'], rows: [
    ['Trainers vs the rule', 'Actual trainers over the trainers required (one per 6 crew-level employees).'],
    ['Crew level', 'The crew-level employees on the roster, the trainers themselves excluded.'],
    ['Trainers vs plan', 'Actual trainers over the trainers in the projection.'],
    ['Trainers who resigned', 'Trainers who were on a list and resigned in the last 6 months.'],
  ] } },
  { look: 'Trainers vs the rule first - below 100% means you have fewer trainers than the rule asks for.' },

  { h2: 'Step 3 - Read the Smart Insights' }, { img: 'insights', w: 520 },
  { p: 'Plain sentences worked out from the numbers on the page: trainers against the rule, the stores most short or above the rule, whether extra trainers could cover gaps elsewhere, the projection against the rule, how the number of trainers changed, who joined or left the list, and how many resigned. They follow your filters.' },
  { decide: 'if some stores have extra trainers while others are short, consider moving trainers between them before recruiting new ones.' },

  { h2: 'Step 4 - How are trainers moving through the year?' }, { img: 'trainers_per_month', w: 600 },
  { p: 'Projected against actual Crew Trainers for each month of the year. Actual is the people on the uploaded list who are also on that month\'s active employee list, so a month without a list or a roster shows 0.' },
  { look: 'whether the actual line follows the projection or falls away from it.' },

  { h2: 'Step 5 - How many stores are short?' }, { img: 'ctcmp', w: 600 },
  { p: 'Stores grouped by how many trainers they have against the rule: Over (more than required), Enough (exactly), Short (fewer), No trainers (required but none) and Not needed (none required and none present). Each group shows its stores, crew level, required, actual, gap vs rule, projected and gap vs plan.' },
  { look: 'the No trainers and Short groups.' },

  { h2: 'Step 6 - Which stores need trainers?' },
  { defs: { head: ['Column', 'Meaning'], rows: [
    ['Crew level', 'The people the trainers train.'],
    ['Required', 'Crew level divided by 6, rounded to the nearest whole number (a half rounds up).'],
    ['Actual', 'Actual trainers in the store.'],
    ['Gap vs rule', 'Actual minus required. Negative = short of trainers, positive = over.'],
    ['Projected / Gap vs plan', 'The trainers in the projection, and actual minus projected.'],
    ['Status', 'Over, Enough, Short, No trainers, or Not needed (none required and none present).'],
  ] } },
  { img: 'trainers_by_store', w: 600 },
  { p: 'Stores most short come first. Click a column title to sort, and use the search box to find a store or a consultant.' },
  { decide: 'agree with the responsible Operation Consultant how the top stores will reach the required number of trainers.' },

  { h2: 'Step 7 - Who joined or left the trainer list?' }, { img: 'trainer_list_changes', w: 600 },
  { p: 'Who joined and who left the trainer list compared with the previous uploaded list. For people who left, the status tells you why: Resigned (they appear in the resignations), Still working, off the list (still on this month\'s active list, so the allowance was removed or they moved), or Not on the roster.' },
  { look: 'people marked "Still working, off the list" - they may be trainers that moved or lost their allowance by mistake.' },

  { h2: 'Step 8 - Who is responsible?' },
  { img: 'operation_consultants', w: 600 }, { img: 'operation_directors', w: 600 }, { img: 'operation_managers', w: 600 }, { img: 'senior_operation_consultants', w: 600 },
  { p: 'The same figures rolled up by Operation Consultant, Director, Manager and Senior Consultant, with the number of stores each covers.' },

] };
