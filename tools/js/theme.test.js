// The theme scripts against a minimal fake DOM: the inline script that sets the theme before the first paint
// (Views/Shared/_ThemeInit.cshtml) and the switch in wwwroot/js/site.js. Run: node tools/js/theme.test.js
const fs = require('fs');
const vm = require('vm');
const path = require('path');
const root = path.resolve(__dirname, '..', '..');
const app = p => path.join(root, 'src', 'BudgetApp', p);

const themeInit = fs.readFileSync(app('Views/Shared/_ThemeInit.cshtml'), 'utf8');
const layout = fs.readFileSync(app('Views/Shared/_Layout.cshtml'), 'utf8');
const siteJs = fs.readFileSync(app('wwwroot/js/site.js'), 'utf8');

let failures = 0;
function check(name, condition, detail) {
    if (!condition) failures++;
    if (!condition) console.log('FAIL  ' + name + (detail ? '  -> ' + detail : ''));
}

function inlineScripts(html) {
    return [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
}

const TOKENS = {
    light: { '--surface': '#FFFFFF', '--surface-2': '#F5F8F6', '--text': '#1B2B26', '--muted': '#66756F', '--line-strong': '#CFDAD5' },
    dark: { '--surface': '#17211E', '--surface-2': '#1E2A26', '--text': '#E6EFEB', '--muted': '#9AABA4', '--line-strong': '#3A4B45' }
};

function makeWorld({ stored = null, systemDark = false, storageThrows = false } = {}) {
    const store = new Map();
    if (stored !== null) store.set('budgetapp-theme', stored);
    const listeners = {};
    const windowListeners = {};
    const attrs = {};
    const meta = { content: '#0B7A5C', setAttribute(k, v) { this[k] = v; } };
    const media = {
        matches: systemDark,
        handlers: [],
        addEventListener(type, fn) { this.handlers.push(fn); },
        set(value) { this.matches = value; this.handlers.forEach(h => h({ matches: value })); }
    };
    function button(value) {
        return {
            dataset: { themeValue: value }, classes: new Set(), attrs: {}, handlers: [],
            classList: { toggle(name, on) { on ? this._s.add(name) : this._s.delete(name); }, _s: null },
            setAttribute(k, v) { this.attrs[k] = v; },
            addEventListener(type, fn) { this.handlers.push(fn); },
            click() { this.handlers.forEach(h => h()); }
        };
    }
    const buttons = ['light', 'dark', 'auto'].map(v => { const b = button(v); b.classList._s = b.classes; return b; });
    const icon = { className: 'bi bi-circle-half' };
    const events = [];

    const document = {
        documentElement: {
            getAttribute: k => (k in attrs ? attrs[k] : null),
            setAttribute: (k, v) => { attrs[k] = v; }
        },
        querySelector: sel => (sel.includes('theme-color') ? meta : null),
        querySelectorAll: sel => sel.includes('data-theme-value') ? buttons : sel.includes('data-theme-icon') ? [icon] : [],
        getElementById: id => ({ id }),
        addEventListener: (type, fn) => { (listeners[type] = listeners[type] || []).push(fn); },
        dispatchEvent: e => { events.push(e); (listeners[e.type] || []).forEach(fn => fn(e)); }
    };
    const localStorage = {
        getItem(k) { if (storageThrows) throw new Error('blocked'); return store.has(k) ? store.get(k) : null; },
        setItem(k, v) { if (storageThrows) throw new Error('blocked'); store.set(k, String(v)); },
        removeItem(k) { if (storageThrows) throw new Error('blocked'); store.delete(k); }
    };
    const charts = [];
    class Chart {
        constructor(el, config) { this.el = el; this.data = config.data; this.options = config.options; this.plugins = config.plugins; this.updates = []; charts.push(this); }
        update(mode) { this.updates.push(mode); }
    }
    const window = {
        matchMedia: q => (q.includes('prefers-color-scheme') ? media : { matches: false, addEventListener() {} }),
        addEventListener: (type, fn) => { (windowListeners[type] = windowListeners[type] || []).push(fn); }
    };
    const sandbox = {
        document, window, localStorage, Chart, Intl, console, Promise, Object, Math,
        CustomEvent: class { constructor(type, init) { this.type = type; this.detail = init && init.detail; } },
        getComputedStyle: () => ({ getPropertyValue: name => TOKENS[attrs['data-bs-theme'] === 'dark' ? 'dark' : 'light'][name] || '' }),
        performance: { now: () => 0 }, requestAnimationFrame: () => 0
    };
    sandbox.window.document = document;
    vm.createContext(sandbox);
    return { sandbox, attrs, meta, media, buttons, icon, events, store, charts, windowListeners };
}

// ---------- 1. the inline script in <head> ----------
const initScript = inlineScripts(themeInit).find(s => s.includes('budgetapp-theme'));
check('init script is present in _ThemeInit', !!initScript);
check('init script comes before the first stylesheet in _Layout', layout.indexOf('<partial name="_ThemeInit"') < layout.indexOf('rel="stylesheet"'));

function runInit(options) { const w = makeWorld(options); vm.runInContext(initScript, w.sandbox); return w; }
let w = runInit({ stored: 'dark', systemDark: false });
check('init: stored "dark" wins over a light system', w.attrs['data-bs-theme'] === 'dark' && w.attrs['data-theme-choice'] === 'dark' && w.meta.content === '#0F1614');
w = runInit({ stored: 'light', systemDark: true });
check('init: stored "light" wins over a dark system', w.attrs['data-bs-theme'] === 'light' && w.meta.content === '#0B7A5C');
w = runInit({ stored: null, systemDark: true });
check('init: nothing stored follows a dark system', w.attrs['data-bs-theme'] === 'dark' && w.attrs['data-theme-choice'] === 'auto');
w = runInit({ stored: null, systemDark: false });
check('init: nothing stored follows a light system', w.attrs['data-bs-theme'] === 'light');
w = runInit({ stored: 'banana', systemDark: true });
check('init: an unknown stored value is treated as "system"', w.attrs['data-bs-theme'] === 'dark' && w.attrs['data-theme-choice'] === 'auto');
w = runInit({ storageThrows: true, systemDark: true });
check('init: blocked storage does not break the page', w.attrs['data-bs-theme'] === 'dark');

// ---------- 2. the switch in site.js ----------
function runSite(options) { const world = runInit(options); vm.runInContext(siteJs, world.sandbox); return world; }

w = runSite({ stored: null, systemDark: false });
check('switch: on load the current choice is marked and no event is fired', w.buttons[2].classes.has('active') && w.buttons[2].attrs['aria-pressed'] === 'true' && w.events.length === 0);
check('switch: on load the toggle shows the "system" icon', w.icon.className === 'bi bi-circle-half');
w.buttons[1].click();
check('switch: choosing "Koyu" switches, stores and announces it',
    w.attrs['data-bs-theme'] === 'dark' && w.store.get('budgetapp-theme') === 'dark' && w.events.length === 1 && w.events[0].type === 'themechange' && w.events[0].detail.theme === 'dark');
check('switch: the menu marks "Koyu" and the icon becomes a moon', w.buttons[1].classes.has('active') && !w.buttons[2].classes.has('active') && w.icon.className === 'bi bi-moon-stars-fill' && w.meta.content === '#0F1614');
w.buttons[1].click();
check('switch: choosing the same theme again fires no second event', w.events.length === 1);
w.media.set(false); w.media.set(true);
check('switch: with an explicit choice the system theme is ignored', w.attrs['data-bs-theme'] === 'dark' && w.events.length === 1);
w.buttons[2].click();
check('switch: "Sistem" forgets the stored choice and follows the system', !w.store.has('budgetapp-theme') && w.attrs['data-theme-choice'] === 'auto' && w.attrs['data-bs-theme'] === 'dark');
w.media.set(false);
check('switch: in "Sistem" a system change switches the page and announces it', w.attrs['data-bs-theme'] === 'light' && w.events.length === 2 && w.events[1].detail.theme === 'light');
w.buttons[0].click();
check('switch: choosing "Açık" stores it', w.store.get('budgetapp-theme') === 'light' && w.icon.className === 'bi bi-sun-fill');
w.store.set('budgetapp-theme', 'dark');
(w.windowListeners.storage || []).forEach(fn => fn({ key: 'budgetapp-theme' }));
check('switch: a change made in another tab is applied here', w.attrs['data-bs-theme'] === 'dark' && w.buttons[1].classes.has('active'));
w = runSite({ storageThrows: true, systemDark: false });
w.buttons[1].click();
check('switch: works for the current page even when storage is blocked', w.attrs['data-bs-theme'] === 'dark');

console.log(failures ? 'theme: ' + failures + ' failure(s)' : 'theme: all checks ok');
process.exit(failures ? 1 : 0);
