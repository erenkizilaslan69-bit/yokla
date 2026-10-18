// wwwroot/js/quick-entry.js ("Söyle ya da yaz") against a tiny fake DOM, a fake MediaRecorder and a hand-driven
// clock; also receipt-review.js on the quick entry confirmation screen. Run: node tools/js/quick-entry.test.js
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..', '..');
const app = p => path.join(root, 'src', 'BudgetApp', p);
const source = fs.readFileSync(app('wwwroot/js/quick-entry.js'), 'utf8');
const reviewSource = fs.readFileSync(app('wwwroot/js/receipt-review.js'), 'utf8');

const problems = [];
function check(name, ok, detail) { if (!ok) problems.push(name + ': ' + (detail === undefined ? '' : detail)); }

function el(extra) {
    return Object.assign({
        hidden: false, textContent: '', disabled: false, value: '', listeners: {}, attrs: {}, classes: new Set(), focused: false,
        addEventListener(type, fn) { this.listeners[type] = fn; },
        setAttribute(k, v) { this.attrs[k] = v; },
        focus() { this.focused = true; },
        classList: null
    }, extra);
}
function withClassList(node) {
    node.classList = { toggle: (c, on) => on ? node.classes.add(c) : node.classes.delete(c), contains: c => node.classes.has(c) };
    return node;
}

async function flush() { for (let i = 0; i < 5; i++) await Promise.resolve(); }

function setup(options) {
    options = options || {};
    let now = 1000000;
    const intervals = [];
    const submitSpan = el();
    const parts = {
        '[data-mic]': el({ hidden: true }),
        '[data-record]': withClassList(el()),
        '[data-record-label]': el(),
        '[data-status]': el(),
        '[data-mic-error]': el({ hidden: true }),
        '[data-mic-error-text]': el(),
        '[data-audio]': el({ files: null }),
        'textarea[name="text"]': el({ value: 'yazılmış metin' }),
        '[data-text-label]': el({ textContent: 'Yaz' }),
        '[data-submit]': el({ querySelector: () => submitSpan })
    };
    let submitted = 0;
    const form = el({
        dataset: { maxSeconds: '30', maxBytes: String(options.maxBytes || 2000000) },
        querySelector: s => parts[s],
        submit() { submitted++; }
    });
    const docListeners = {};
    const document = { getElementById: () => form, addEventListener: (t, f) => { docListeners[t] = f; } };
    const winListeners = {};
    const tracksStopped = { count: 0 };

    class FakeRecorder {
        constructor(stream, opts) { this.state = 'inactive'; this.mimeType = (opts && opts.mimeType) || 'audio/webm'; this.l = {}; FakeRecorder.last = this; }
        static isTypeSupported(t) { return (options.supported || ['audio/webm;codecs=opus']).includes(t); }
        addEventListener(t, f) { this.l[t] = f; }
        start() { this.state = 'recording'; }
        stop() {
            this.state = 'inactive';
            this.l.dataavailable({ data: { size: options.blobSize === undefined ? 4000 : options.blobSize, type: this.mimeType } });
            this.l.stop();
        }
    }
    class FakeTransfer { constructor() { const files = []; this.files = files; this.items = { add: f => files.push(f) }; } }
    if (options.noTransfer) FakeTransfer.prototype.constructor = null;

    const window = {
        isSecureContext: options.insecure ? false : true,
        MediaRecorder: options.noRecorder ? undefined : FakeRecorder,
        addEventListener: (t, f) => { winListeners[t] = f; }
    };
    const navigator = {
        mediaDevices: {
            getUserMedia: async () => {
                if (options.deny) { const e = new Error('denied'); e.name = options.deny; throw e; }
                return { getTracks: () => [{ stop: () => tracksStopped.count++ }] };
            }
        }
    };
    const DataTransfer = options.noTransfer ? function () { throw new Error('no'); } : FakeTransfer;
    const Blob = function (chunks, o) { this.size = chunks.reduce((n, c) => n + c.size, 0); this.type = o.type; };
    const File = function (blobs, name, o) { this.name = name; this.type = o.type; this.size = blobs[0].size; };
    const Date = { now: () => now };
    const setInterval = (fn) => { intervals.push(fn); return intervals.length; };
    const clearInterval = () => { intervals.length = 0; };

    new Function('document', 'window', 'navigator', 'MediaRecorder', 'DataTransfer', 'Blob', 'File', 'Date', 'setInterval', 'clearInterval', source)
        (document, window, navigator, window.MediaRecorder, DataTransfer, Blob, File, Date, setInterval, clearInterval);

    return {
        parts, form, docListeners, winListeners, tracksStopped, FakeRecorder,
        submitted: () => submitted,
        advance(seconds) { now += seconds * 1000; intervals.slice().forEach(f => f()); }
    };
}

(async function () {
    // 1) no MediaRecorder / not HTTPS / no DataTransfer: the microphone stays hidden, text still works
    for (const o of [{ noRecorder: true }, { insecure: true }, { noTransfer: true }]) {
        const t = setup(o);
        check('hidden ' + JSON.stringify(o), t.parts['[data-mic]'].hidden === true);
        check('label stays "Yaz" ' + JSON.stringify(o), t.parts['[data-text-label]'].textContent === 'Yaz');
    }

    // 2) supported: microphone shown, label becomes "Ya da yaz"
    let t = setup();
    check('mic shown', t.parts['[data-mic]'].hidden === false);
    check('label', t.parts['[data-text-label]'].textContent === 'Ya da yaz');

    // 3) permission denied: a clear message, focus on the text box, nothing recorded
    t = setup({ deny: 'NotAllowedError' });
    t.parts['[data-record]'].listeners.click(); await flush();
    check('denied message', t.parts['[data-mic-error]'].hidden === false && /izni verilmedi/.test(t.parts['[data-mic-error-text]'].textContent),
        t.parts['[data-mic-error-text]'].textContent);
    check('denied focus', t.parts['textarea[name="text"]'].focused === true);
    check('denied no submit', t.submitted() === 0);
    t = setup({ deny: 'NotFoundError' });
    t.parts['[data-record]'].listeners.click(); await flush();
    check('no mic message', /bulunamadı/.test(t.parts['[data-mic-error-text]'].textContent));

    // 4) record, counter, stops by itself at 30 s and sends the file; typed text is dropped
    t = setup();
    t.parts['[data-record]'].listeners.click(); await flush();
    check('recording state', t.parts['[data-record]'].classes.has('is-recording') && t.parts['[data-record]'].attrs['aria-pressed'] === 'true');
    check('submit locked while recording', t.parts['[data-submit]'].disabled === true);
    t.advance(7);
    check('clock', t.parts['[data-status]'].textContent === 'Dinliyorum… 0:07 / 0:30', t.parts['[data-status]'].textContent);
    t.advance(20);
    check('countdown', t.parts['[data-status]'].textContent === 'Dinliyorum… 3 saniye kaldı', t.parts['[data-status]'].textContent);
    t.advance(3);
    check('auto stop -> submitted', t.submitted() === 1);
    const files = t.parts['[data-audio]'].files;
    check('file attached', files && files.length === 1 && files[0].name === 'kayit.webm' && files[0].type === 'audio/webm;codecs=opus', JSON.stringify(files));
    check('text cleared', t.parts['textarea[name="text"]'].value === '');
    check('microphone released', t.tracksStopped.count === 1);
    check('busy', t.parts['[data-submit]'].disabled === true);

    // 5) Safari: MP4 recording gets an .m4a name
    t = setup({ supported: ['audio/mp4'] });
    t.parts['[data-record]'].listeners.click(); await flush();
    t.advance(2); t.parts['[data-record]'].listeners.click();
    check('safari m4a', t.parts['[data-audio]'].files[0].name === 'kayit.m4a', t.parts['[data-audio]'].files[0].name);

    // 6) too short: not sent
    t = setup();
    t.parts['[data-record]'].listeners.click(); await flush();
    t.advance(0.2); t.parts['[data-record]'].listeners.click();
    check('too short not sent', t.submitted() === 0 && /Çok kısa/.test(t.parts['[data-status]'].textContent), t.parts['[data-status]'].textContent);
    check('button back', t.parts['[data-record-label]'].textContent === 'Konuşmak için dokun' && t.parts['[data-submit]'].disabled === false);

    // 7) too large: not sent, message
    t = setup({ maxBytes: 1000, blobSize: 5000 });
    t.parts['[data-record]'].listeners.click(); await flush();
    t.advance(3); t.parts['[data-record]'].listeners.click();
    check('too large not sent', t.submitted() === 0 && /çok büyük/.test(t.parts['[data-mic-error-text]'].textContent));

    // 8) Escape drops the recording
    t = setup();
    t.parts['[data-record]'].listeners.click(); await flush();
    t.advance(3); t.docListeners.keydown({ key: 'Escape' });
    check('escape cancels', t.submitted() === 0 && t.tracksStopped.count === 1 && t.parts['[data-status]'].textContent === '');

    // 9) typed text: the submit button locks against a double tap
    t = setup();
    t.form.listeners.submit();
    check('typed submit locks', t.parts['[data-submit]'].disabled === true);

    // 10) the review script on the quick entry screen (no running total element) does not throw
    try {
        const count = el();
        const include = el({ checked: true });
        const line = withClassList(el({ querySelector: s => ({ '[data-include]': include, '.receipt-include': el(), '[data-remove]': el(), '[data-restore]': el(), '[data-amount]': el({ value: '20,00' }) })[s] }));
        const qform = el({ dataset: {}, querySelectorAll: () => [line], querySelector: s => ({ '[data-count]': count })[s] || null });
        new Function('document', reviewSource)({ getElementById: id => id === 'quickEntryReview' ? qform : null });
        check('review count on quick entry', String(count.textContent) === '1', count.textContent);
    } catch (e) { check('review script on quick entry', false, e.message); }

    console.log(problems.length ? 'PROBLEMS:\n' + problems.join('\n') : 'quick-entry: all checks ok');
    process.exit(problems.length ? 1 : 0);
})();
