// Theme switch: light / dark / follow the system.
// The theme itself is already set on <html> by the inline script in _ThemeInit.cshtml (before first paint);
// this part wires the navbar menu, remembers the choice and reacts to later changes. Whenever the effective
// theme changes, a "themechange" event is fired on document so things drawn on a canvas (the analysis chart)
// can redraw without a page reload.
(function () {
    const KEY = 'budgetapp-theme';
    const root = document.documentElement;
    const systemDark = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
    const icons = { light: 'bi-sun-fill', dark: 'bi-moon-stars-fill', auto: 'bi-circle-half' };

    function storedChoice() {
        try {
            const value = localStorage.getItem(KEY);
            return value === 'light' || value === 'dark' ? value : 'auto';
        } catch (e) {
            return 'auto';
        }
    }

    function resolve(choice) {
        return choice === 'dark' || (choice === 'auto' && systemDark && systemDark.matches) ? 'dark' : 'light';
    }

    function showChoice(choice) {
        document.querySelectorAll('[data-theme-value]').forEach(function (button) {
            const active = button.dataset.themeValue === choice;
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
        document.querySelectorAll('[data-theme-icon]').forEach(function (icon) {
            icon.className = 'bi ' + icons[choice];
        });
    }

    function apply(choice) {
        const theme = resolve(choice);
        const changed = root.getAttribute('data-bs-theme') !== theme;
        root.setAttribute('data-bs-theme', theme);
        root.setAttribute('data-theme-choice', choice);

        const meta = document.querySelector('meta[name="theme-color"]');
        if (meta) meta.setAttribute('content', theme === 'dark' ? '#0F1614' : '#0B7A5C');

        showChoice(choice);
        if (changed) document.dispatchEvent(new CustomEvent('themechange', { detail: { theme: theme } }));
    }

    document.querySelectorAll('[data-theme-value]').forEach(function (button) {
        button.addEventListener('click', function () {
            const choice = button.dataset.themeValue;
            try {
                if (choice === 'auto') localStorage.removeItem(KEY);
                else localStorage.setItem(KEY, choice);
            } catch (e) { /* storage blocked: the choice lasts for this page only */ }
            apply(choice);
        });
    });

    // "Sistem": follow the operating system while the page is open.
    if (systemDark) {
        const onSystemChange = function () { if (storedChoice() === 'auto') apply('auto'); };
        if (systemDark.addEventListener) systemDark.addEventListener('change', onSystemChange);
        else if (systemDark.addListener) systemDark.addListener(onSystemChange);
    }

    // Another tab changed the choice.
    window.addEventListener('storage', function (event) {
        if (event.key === KEY || event.key === null) apply(storedChoice());
    });

    apply(storedChoice());
})();
