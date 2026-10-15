// Show/hide ("eye") button for password fields. Include this script on a page and every
// <input type="password"> on it gets a small button at its end: click to reveal the text,
// click again to hide it. Labels come from the script tag so they can be localized:
//   <script src="~/js/password-toggle.js" data-show-label="..." data-hide-label="..."></script>
// An input can opt out with data-no-toggle. Needs the Bootstrap Icons font (bi-eye / bi-eye-slash).
(function () {
    var script = document.currentScript;
    var showLabel = (script && script.dataset.showLabel) || 'Show password';
    var hideLabel = (script && script.dataset.hideLabel) || 'Hide password';

    function injectStyles() {
        if (document.getElementById('pw-toggle-styles')) return;
        var style = document.createElement('style');
        style.id = 'pw-toggle-styles';
        style.textContent =
            '.pw-toggle-wrap{position:relative;display:block;width:100%}' +
            '.pw-toggle-btn{position:absolute;inset-inline-end:6px;display:flex;align-items:center;justify-content:center;' +
            'width:32px;padding:0;border:0;background:transparent;color:var(--text-3,#6b7280);font-size:16px;line-height:1;' +
            'cursor:pointer;opacity:.75;transition:opacity .15s,color .15s}' +
            '.pw-toggle-btn:hover{opacity:1}' +
            '.pw-toggle-btn:focus-visible{opacity:1;outline:2px solid currentColor;outline-offset:-2px;border-radius:6px}';
        document.head.appendChild(style);
    }

    function enhance(input) {
        if (input.dataset.pwToggle === 'done' || input.hasAttribute('data-no-toggle')) return;
        input.dataset.pwToggle = 'done';

        var wasFocused = document.activeElement === input;
        var wrap = document.createElement('span');
        wrap.className = 'pw-toggle-wrap';
        input.parentNode.insertBefore(wrap, input);
        wrap.appendChild(input);

        // The wrapper is positioned, so it would now paint over decorations that sit before the input
        // in the same parent (e.g. the lock icon on the sign-in page); lift those back above it.
        for (var prev = wrap.previousElementSibling; prev; prev = prev.previousElementSibling) {
            if (window.getComputedStyle(prev).position === 'absolute') prev.style.zIndex = '1';
        }

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'pw-toggle-btn';
        btn.setAttribute('aria-label', showLabel);
        btn.setAttribute('title', showLabel);
        btn.setAttribute('aria-pressed', 'false');
        btn.innerHTML = '<i class="bi bi-eye" aria-hidden="true"></i>';
        wrap.appendChild(btn);

        // Keep the typed text clear of the button (padding at the end side, in either text direction).
        input.style.paddingInlineEnd = '42px';

        // Line the button up with the input itself (it may have its own margins), also when the
        // input first becomes visible later (e.g. inside a popup) or is resized.
        function position() {
            btn.style.top = input.offsetTop + 'px';
            btn.style.height = input.offsetHeight + 'px';
        }
        position();
        if (window.ResizeObserver) new ResizeObserver(position).observe(input);
        else window.addEventListener('resize', position);

        // Don't steal focus from the input when the button is pressed with the mouse.
        btn.addEventListener('mousedown', function (e) { e.preventDefault(); });
        btn.addEventListener('click', function () {
            var reveal = input.type === 'password';
            input.type = reveal ? 'text' : 'password';
            btn.setAttribute('aria-pressed', reveal ? 'true' : 'false');
            var label = reveal ? hideLabel : showLabel;
            btn.setAttribute('aria-label', label);
            btn.setAttribute('title', label);
            btn.firstChild.className = reveal ? 'bi bi-eye-slash' : 'bi bi-eye';
            input.focus();
        });

        if (wasFocused) input.focus();
    }

    function init() {
        injectStyles();
        Array.prototype.forEach.call(document.querySelectorAll('input[type="password"]'), enhance);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
