/* Loading animation (Lottie) shown over the page body while a page's data request is in flight.
   Used by Workforce Planning, Hiring Forecast and Crew Trainers:
       const done = PageLoader.begin();   // before the request
       try { ... await fetch(...) ... } finally { done(); }
   The animation appears centered in the page body (the area below the header, as seen before any scrolling, and it stays there while scrolling) and only after a short delay, so fast
   (cached) responses never flash it. Overlapping begin() calls share one overlay. */
(function () {
    const SHOW_DELAY_MS = 800;       // responses faster than this (the cached, "fast" opens) never show the animation
    const SIZE_PX = 180;           // width of the animation; its canvas is 4:3
    const ANIMATION_PATH = '/lottie/loading.json';

    let pending = 0, timer = null, overlay = null, anim = null, host = null, prevMinHeight = '';

    function injectStyle() {
        if (document.getElementById('pageLoaderStyle')) return;
        const st = document.createElement('style');
        st.id = 'pageLoaderStyle';
        st.textContent =
            '.page-loader{position:absolute;inset:0;z-index:50;background:var(--bg,#fff);opacity:.92}' +
            '.page-loader-inner{position:sticky;top:calc(var(--header-h,64px) + (100vh - var(--header-h,64px)) / 2 - ' + (SIZE_PX * 0.375) + 'px);margin:0 auto;' +
            'width:' + SIZE_PX + 'px;height:' + (SIZE_PX * 0.75) + 'px}';
        document.head.appendChild(st);
    }

    function show() {
        timer = null;
        if (pending <= 0 || overlay) return;
        host = document.querySelector('.page-content');
        if (!host) return;
        injectStyle();
        if (getComputedStyle(host).position === 'static') host.style.position = 'relative';
        prevMinHeight = host.style.minHeight;
        host.style.minHeight = '70vh';
        overlay = document.createElement('div');
        overlay.className = 'page-loader';
        overlay.setAttribute('role', 'status');
        overlay.setAttribute('aria-busy', 'true');
        const inner = document.createElement('div');
        inner.className = 'page-loader-inner';
        overlay.appendChild(inner);
        host.appendChild(overlay);
        if (window.lottie) {
            anim = window.lottie.loadAnimation({
                container: inner, renderer: 'svg', loop: true, autoplay: true, path: ANIMATION_PATH,
            });
        }
    }

    function hide() {
        if (timer) { clearTimeout(timer); timer = null; }
        if (anim) { anim.destroy(); anim = null; }
        if (overlay) { overlay.remove(); overlay = null; }
        if (host) host.style.minHeight = prevMinHeight;
    }

    window.PageLoader = {
        begin() {
            pending++;
            if (pending === 1 && !overlay && !timer) timer = setTimeout(show, SHOW_DELAY_MS);
            let finished = false;
            return function end() {
                if (finished) return;
                finished = true;
                pending = Math.max(0, pending - 1);
                if (pending === 0) hide();
            };
        },
    };
})();
