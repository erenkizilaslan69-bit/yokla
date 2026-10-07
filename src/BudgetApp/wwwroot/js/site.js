// Counts a number up from zero once, e.g. <span data-countup="31" data-suffix="%">31%</span>.
// Without JavaScript (or with reduced motion) the server-rendered value simply stays.
(function () {
    const reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduce) return;

    document.querySelectorAll('[data-countup]').forEach(function (el) {
        const target = parseFloat(el.dataset.countup);
        if (!isFinite(target)) return;
        const prefix = el.dataset.prefix || '';
        const suffix = el.dataset.suffix || '';
        const duration = 1000;
        const start = performance.now();

        function frame(now) {
            const t = Math.min(1, (now - start) / duration);
            const eased = 1 - Math.pow(1 - t, 3);
            el.textContent = prefix + Math.round(target * eased) + suffix;
            if (t < 1) requestAnimationFrame(frame);
        }

        requestAnimationFrame(frame);
    });
})();
