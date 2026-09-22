/* Shared chart color system.
   Semantic colors = business meaning (good/warn/bad/neutral).
   Categorical colors = no good/bad meaning; always looked up by LABEL,
   never by array position, so the same category is always the same color. */

// Single canonical HTML-escaping helper for every page that builds HTML via
// innerHTML/template literals from database- or import-sourced text (employee
// names, store/leader names, comments, notes, etc.). chart-colors.js is the
// one script every dashboard content page already loads first, so this is
// the shared place for it — do not redefine this elsewhere.
function sapEscape(s) {
    if (s === null || s === undefined) return '';
    const div = document.createElement('div');
    div.textContent = String(s);
    return div.innerHTML;
}

// Tooltip hit-testing: the popup must only appear when the cursor is
// actually over a rendered bar/segment, and must show THAT element's data.
// The key setting is intersect:true — the earlier bug (tooltip showing a
// neighboring column's data, or popping up in the empty space between two
// bars) came from intersect:false, which matches by nearest-pixel-distance
// even when the pointer isn't over any element at all. mode:'index' means a
// hovered bar also reveals the other series sharing its category (e.g. the
// A-vs-B comparison bars) — but combined with intersect:true it still only
// fires when the pointer is genuinely over one of those bars, never over
// empty space. For single-series charts index+intersect resolves to just
// the one bar under the cursor. Set globally so every current and future
// chart gets it without per-chart edits; line charts opt back into
// intersect:false locally, where a crosshair-anywhere read is wanted. This
// file is loaded after Chart.js on all chart pages, including Action Center
// pages that do not load dashboard.js.
if (typeof Chart !== 'undefined') {
    Chart.defaults.interaction.mode = 'index';
    Chart.defaults.interaction.intersect = true;
    Chart.defaults.plugins.tooltip.mode = 'index';
    Chart.defaults.plugins.tooltip.intersect = true;

    // The admin portal renders under CSS `zoom: 0.8` (admin-theme.css). CSS
    // zoom is invisible to Chart.js 4's pointer math: the browser hands it a
    // cursor position already shrunk by the zoom factor (e.g. offsetX 208 for
    // a bar Chart.js has laid out at x=260), and Chart.js compares that
    // against its own unzoomed element coordinates without dividing the zoom
    // back out. The mismatch is zero at the canvas's top-left origin and
    // grows with distance — so the first bar reads roughly right while every
    // bar after it resolves to a neighbor or to the empty gap between bars.
    // This plugin restores the mapping by scaling each event's position back
    // up by the same factor before Chart.js runs hit-testing. The factor is
    // measured live (rendered width ÷ layout width), so it self-corrects for
    // any zoom value and is a no-op (ratio ≈ 1) on pages with no zoom.
    Chart.register({
        id: 'ancestorZoomFix',
        beforeEvent(chart, args) {
            const e = args.event;
            if (!e || e.x == null || e.y == null) return;
            const layoutW = chart.canvas.offsetWidth;
            if (!layoutW) return;
            const ratio = chart.canvas.getBoundingClientRect().width / layoutW;
            if (ratio > 0 && Math.abs(ratio - 1) > 0.01) {
                e.x /= ratio;
                e.y /= ratio;
            }
        }
    });
    // Horizontal bar charts (indexAxis:'y' — every leaderboard/ranking chart
    // in the app) otherwise always keep their category labels on the left
    // and grow bars rightward, regardless of page direction: correct for an
    // LTR page, backwards for RTL, where the labels should sit on the right
    // with each bar starting right next to its label and growing toward the
    // left. Canvas itself is deliberately kept LTR (see .chart-body canvas
    // in site.css) — that's a separate fix, for numeric-leading label text
    // (store codes like "1480106 | Agami") that Chart.js's own RTL text
    // handling would otherwise reorder incorrectly; this only repositions
    // the axes via Chart.js's own layout options, so it doesn't touch that.
    // Wrapping the constructor (rather than editing every chart call site)
    // makes both of these apply to every current and future chart
    // automatically, with no per-chart code needed.
    const OriginalChart = Chart;
    window.Chart = new Proxy(OriginalChart, {
        construct(target, args) {
            const [ctx, config] = args;
            config.options = config.options || {};
            const isRtl = document.documentElement.getAttribute('dir') === 'rtl';
            if (isRtl && config.options.indexAxis === 'y') {
                config.options.scales = config.options.scales || {};
                config.options.scales.y = Object.assign({}, config.options.scales.y, { position: 'right' });
                config.options.scales.x = Object.assign({}, config.options.scales.x, { reverse: true });
            }
            return new target(ctx, config);
        }
    });
}

const ChartColors = (function () {
    const SEMANTIC = {
        GOOD:           'oklch(0.62 0.15 155)', // green
        GOOD_LIGHT:     'oklch(0.75 0.13 145)', // light green (Likert level 4)
        WARNING:        'oklch(0.75 0.16 75)',  // amber
        WARNING_STRONG: 'oklch(0.68 0.18 55)',  // orange (stronger warning tier, same family as WARNING)
        BAD:            'oklch(0.6 0.22 22)',   // red
        NEUTRAL:        'oklch(0.5 0.03 258)',  // gray/blue
    };

    // 5-level Likert gradient, index 0 = level 1 (worst) .. index 4 = level 5 (best).
    const LIKERT_SCALE = [SEMANTIC.BAD, SEMANTIC.WARNING_STRONG, SEMANTIC.WARNING, SEMANTIC.GOOD_LIGHT, SEMANTIC.GOOD];

    // Categorical palette: use colors with clearly separated hue and lightness.
    // This keeps adjacent bars/segments distinguishable even in dense charts,
    // while staying separate from the semantic good/warning/bad colors above.
    const CATEGORICAL_PALETTE = [
        '#0072B2', // blue
        '#D55E00', // vermilion
        '#009E73', // green-teal
        '#CC79A7', // purple
        '#E69F00', // orange
        '#56B4E9', // sky blue
        '#6A3D9A', // deep violet
        '#8C8C8C', // neutral gray
    ];

    // Shared series colors for charts with more than one measured series.
    // Keep these intentionally far apart so lines/bars do not merge visually.
    const SERIES = {
        PRIMARY: CATEGORICAL_PALETTE[0],
        SECONDARY: CATEGORICAL_PALETTE[1],
        TERTIARY: CATEGORICAL_PALETTE[2],
        QUATERNARY: CATEGORICAL_PALETTE[3],
        QUINARY: CATEGORICAL_PALETTE[4],
        SKY: CATEGORICAL_PALETTE[5],
        VIOLET: CATEGORICAL_PALETTE[6],
    };

    const SERIES_FILL = {
        PRIMARY: 'rgba(0, 114, 178, .14)',
        SECONDARY: 'rgba(213, 94, 0, .14)',
        TERTIARY: 'rgba(0, 158, 115, .14)',
        QUATERNARY: 'rgba(204, 121, 167, .14)',
        QUINARY: 'rgba(230, 159, 0, .14)',
        SKY: 'rgba(86, 180, 233, .14)',
        VIOLET: 'rgba(106, 61, 154, .14)',
    };

    // Deterministic fallback for labels with no fixed mapping (e.g. free-text
    // resignation reasons): same label always hashes to the same palette slot.
    function hashColor(label) {
        const s = String(label ?? '');
        let hash = 0;
        for (let i = 0; i < s.length; i++) hash = (hash * 31 + s.charCodeAt(i)) | 0;
        return CATEGORICAL_PALETTE[Math.abs(hash) % CATEGORICAL_PALETTE.length];
    }

    // Gender/Payroll Group values come back from the server already translated
    // to the current UI language (see DataLabelTranslator.cs) — both the
    // English and Arabic forms are keyed here to the same color so a chart
    // looks identical after switching language, not just correctly labeled.
    const GENDER_COLORS = {
        'Male':   CATEGORICAL_PALETTE[0],
        'Female': CATEGORICAL_PALETTE[1],
        'Other':  CATEGORICAL_PALETTE[2],
        'ذكر':    CATEGORICAL_PALETTE[0],
        'أنثى':   CATEGORICAL_PALETTE[1],
    };

    const PAYROLL_GROUP_COLORS = {
        'Manfoods Company':   CATEGORICAL_PALETTE[3],
        'Hourly Paid':        CATEGORICAL_PALETTE[4],
        'العاملين وقت كامل':  CATEGORICAL_PALETTE[3],
        'العاملين بالساعة':   CATEGORICAL_PALETTE[4],
    };

    const RETENTION_MILESTONE_COLORS = {
        '6 Months': CATEGORICAL_PALETTE[0],
        '1 Year':   CATEGORICAL_PALETTE[1],
        '2 Years':  CATEGORICAL_PALETTE[2],
        '3 Years':  CATEGORICAL_PALETTE[3],
        '4 Years':  CATEGORICAL_PALETTE[4],
        '5 Years':  CATEGORICAL_PALETTE[5],
    };

    const RETENTION_TENURE_BUCKET_COLORS = {
        '< 6 months':   CATEGORICAL_PALETTE[0],
        '6–12 months':  CATEGORICAL_PALETTE[1],
        '1–2 years':    CATEGORICAL_PALETTE[2],
        '2–3 years':    CATEGORICAL_PALETTE[3],
        '3–4 years':    CATEGORICAL_PALETTE[4],
        '4–5 years':    CATEGORICAL_PALETTE[5],
        '5+ years':     CATEGORICAL_PALETTE[6],
    };

    const EARLY_WARNING_REASON_COLORS = {
        new_hire_window:      CATEGORICAL_PALETTE[0],
        store_history:        CATEGORICAL_PALETTE[1],
        role_history:         CATEGORICAL_PALETTE[2],
        peak_window:          CATEGORICAL_PALETTE[3],
        gender_history:       CATEGORICAL_PALETTE[4],
        exit_interview_score: CATEGORICAL_PALETTE[5],
        store_leader_history: CATEGORICAL_PALETTE[6],
    };

    // labels: string[] -> colors: string[], aligned by LABEL via knownMap,
    // falling back to a stable hash color for anything not in knownMap.
    function colorsByLabel(labels, knownMap) {
        return labels.map(label => (knownMap && knownMap[label]) || hashColor(label));
    }

    function normalize(label) {
        return String(label ?? '').trim();
    }

    // ── Binary evaluation (Yes/No) questions ─────────────────────────────
    // e.g. "Would Return", "Fair Treatment", "Training Adequate".
    // Unrecognized text intentionally falls back to NEUTRAL rather than
    // guessing — a wrong guess (coloring a "No" green) is worse than gray.
    const YES_TOKENS = ['yes', 'y', 'true', 'نعم'];
    const NO_TOKENS  = ['no', 'n', 'false', 'لا'];

    function yesNoColor(label) {
        const s = normalize(label).toLowerCase();
        if (YES_TOKENS.includes(s)) return SEMANTIC.GOOD;
        if (NO_TOKENS.includes(s))  return SEMANTIC.BAD;
        return SEMANTIC.NEUTRAL;
    }

    function colorsByYesNo(labels) {
        return labels.map(yesNoColor);
    }

    // ── 5-point Likert-scale questions ───────────────────────────────────
    // e.g. "Overall Experience". Accepts a numeric 1-5 rating directly, or
    // normalizes common EN/AR phrasings (satisfaction, quality, and
    // agree/disagree wording) to the same 5 levels the caller's API might
    // use instead of numbers.
    //
    // Bucket check order is 3, 1, 2, 5, 4 (neutral, then negatives, then
    // positives) rather than 1..5 in order, because several phrases are
    // substrings of another level's phrase (e.g. "agree" is contained in
    // "disagree", "satisfied" in "dissatisfied", and the Arabic neutral
    // phrase "لا أوافق ولا أعارض" contains "أوافق"/"أعارض"). Checking the
    // more specific/negative phrases first avoids misreading them as the
    // shorter phrase they contain. This mirrors the substring-priority
    // approach already used by ExitInterviewService.Sentiment() server-side.
    const LIKERT_LEVEL_5 = ['excellent', 'very satisfied', 'strongly agree', 'ممتاز', 'أوافق بشدة'];
    const LIKERT_LEVEL_4 = ['good', 'satisfied', 'agree', 'جيد', 'جيدة', 'أوافق'];
    const LIKERT_LEVEL_3 = ['neutral', 'average', 'fair', 'ok', 'okay', 'محايد', 'مقبولة', 'لا أوافق ولا أعارض'];
    const LIKERT_LEVEL_2 = ['poor', 'dissatisfied', 'disagree', 'ضعيف', 'أعارض'];
    const LIKERT_LEVEL_1 = ['very poor', 'very dissatisfied', 'strongly disagree', 'bad', 'سيء', 'أعارض بشدة'];
    const LIKERT_BUCKETS = [
        [3, LIKERT_LEVEL_3],
        [1, LIKERT_LEVEL_1],
        [2, LIKERT_LEVEL_2],
        [5, LIKERT_LEVEL_5],
        [4, LIKERT_LEVEL_4],
    ];

    function likertLevel(label) {
        if (typeof label === 'number') {
            const n = Math.round(label);
            return n >= 1 && n <= 5 ? n : null;
        }
        const raw = normalize(label);
        if (raw === '') return null;
        const asNumber = Number(raw);
        if (!Number.isNaN(asNumber) && asNumber >= 1 && asNumber <= 5) return Math.round(asNumber);
        const lower = raw.toLowerCase();
        for (const [level, phrases] of LIKERT_BUCKETS) {
            if (phrases.some(p => lower.includes(p))) return level;
        }
        return null;
    }

    function likertColor(label) {
        const level = likertLevel(label);
        return level ? LIKERT_SCALE[level - 1] : SEMANTIC.NEUTRAL;
    }

    function colorsByLikert(labels) {
        return labels.map(likertColor);
    }

    /* Admin-configurable rate→color thresholds (Settings page). One
       independent rule set per metric: 'turnover', 'ninety-day', 'retention'.
       Every page that colors a rate by threshold should go through these
       instead of hardcoding its own cutoffs, so the Settings page actually
       controls the whole site. */
    const RATE_COLOR_MAP = {
        none:           { bg: null, fg: null },
        good:           { bg: SEMANTIC.GOOD, fg: '#fff' },
        warning:        { bg: SEMANTIC.WARNING, fg: '#1C1C27' },
        warning_strong: { bg: SEMANTIC.WARNING_STRONG, fg: '#fff' },
        bad:            { bg: SEMANTIC.BAD, fg: '#fff' },
    };

    const _rateRulesCache = {};
    async function loadRateRules(metric) {
        if (_rateRulesCache[metric]) return _rateRulesCache[metric];
        const promise = fetch(`/api/settings/color-rules/${metric}`)
            .then(r => r.ok ? r.json() : null)
            .catch(() => null);
        _rateRulesCache[metric] = promise;
        const rules = await promise;
        if (!rules || !rules.length) { delete _rateRulesCache[metric]; return null; }
        _rateRulesCache[metric] = rules;
        return rules;
    }

    function matchRateColorName(rules, percent) {
        if (!rules || !rules.length) return 'none';
        for (const r of rules) {
            if (r.upTo === null || r.upTo === undefined) return r.color;
            if (percent <= r.upTo) return r.color;
        }
        return rules[rules.length - 1].color;
    }

    function rateColor(rules, percent) {
        return RATE_COLOR_MAP[matchRateColorName(rules, percent)] || RATE_COLOR_MAP.none;
    }

    function rateBadgeHtml(rules, percent, decimals) {
        decimals = decimals ?? 1;
        const c = rateColor(rules, percent);
        const text = percent.toFixed(decimals) + '%';
        if (!c.bg) return `<span style="font-weight:700">${text}</span>`;
        return `<span style="background:${c.bg};color:${c.fg};padding:3px 10px;border-radius:12px;font-size:11.5px;font-weight:700">${text}</span>`;
    }

    return {
        SEMANTIC,
        LIKERT_SCALE,
        CATEGORICAL_PALETTE,
        SERIES,
        SERIES_FILL,
        GENDER_COLORS,
        PAYROLL_GROUP_COLORS,
        RETENTION_MILESTONE_COLORS,
        RETENTION_TENURE_BUCKET_COLORS,
        EARLY_WARNING_REASON_COLORS,
        colorsByLabel,
        hashColor,
        yesNoColor,
        colorsByYesNo,
        likertLevel,
        likertColor,
        colorsByLikert,
        loadRateRules,
        matchRateColorName,
        rateColor,
        rateBadgeHtml,
    };
})();
