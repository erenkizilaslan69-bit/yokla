// wwwroot/js/receipt-review.js (receipt confirmation screen) against a tiny fake DOM.
// Run: node tools/js/receipt-review.test.js
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..', '..');
const app = p => path.join(root, 'src', 'BudgetApp', p);
const source = fs.readFileSync(app('wwwroot/js/receipt-review.js'), 'utf8');

function el(extra) { return Object.assign({ hidden: false, textContent: '', listeners: {}, classes: new Set(),
    addEventListener(type, fn) { this.listeners[type] = fn; },
    classList: null }, extra); }
function withClassList(node) {
    node.classList = { toggle: (c, on) => on ? node.classes.add(c) : node.classes.delete(c), contains: c => node.classes.has(c) };
    return node;
}

function line(amount, included) {
    const parts = {
        '[data-include]': el({ checked: included }),
        '.receipt-include': el(),
        '[data-remove]': el(),
        '[data-restore]': el(),
        '[data-amount]': el({ value: amount })
    };
    return withClassList(el({ parts, querySelector: s => parts[s] }));
}

function run(total, lines) {
    const difference = el();
    const mismatch = el({ querySelector: s => s === '[data-difference]' ? difference : null });
    const sum = el(), count = el();
    const form = el({
        dataset: { total: total },
        querySelectorAll: () => lines,
        querySelector: s => ({ '[data-lines-total]': sum, '[data-count]': count, '[data-mismatch]': mismatch })[s]
    });
    const document = { getElementById: () => form };
    new Function('document', source)(document);
    return { form, sum, count, mismatch, difference };
}

const problems = [];
function check(name, ok, detail) { if (!ok) problems.push(name + ': ' + detail); }

// 42,50 + 34,00 = 76,50 against 76,50: no warning
let lines = [line('42,50', true), line('34,00', true)];
let r = run('76.50', lines);
check('sum', r.sum.textContent.replace(/\s/g, ' ') === '₺76,50', r.sum.textContent);
check('count', String(r.count.textContent) === '2', r.count.textContent);
check('no mismatch', r.mismatch.hidden === true, r.mismatch.hidden);
check('tick boxes hidden', lines.every(l => l.parts['.receipt-include'].hidden), '');
check('restore hidden', lines.every(l => l.parts['[data-restore]'].hidden && !l.parts['[data-remove]'].hidden), '');

// remove the second line: 42,50 against 76,50 -> warning, difference 34,00
lines[1].parts['[data-remove]'].listeners.click();
check('removed unticks', lines[1].parts['[data-include]'].checked === false, '');
check('removed row dimmed', lines[1].classes.has('is-removed'), '');
check('restore shown', !lines[1].parts['[data-restore]'].hidden && lines[1].parts['[data-remove]'].hidden, '');
check('mismatch shown', r.mismatch.hidden === false, r.mismatch.hidden);
check('difference', r.difference.textContent.replace(/\s/g, ' ') === '₺34,00', r.difference.textContent);
check('count after remove', String(r.count.textContent) === '1', r.count.textContent);

// restore and type a Turkish thousands amount
lines[1].parts['[data-restore]'].listeners.click();
lines[0].parts['[data-amount]'].value = '1.042,50';
lines[0].parts['[data-amount]'].listeners.input();
check('thousands', r.sum.textContent.replace(/\s/g, ' ') === '₺1.076,50', r.sum.textContent);

// within five kuruş: no warning
lines[0].parts['[data-amount]'].value = '42,54';
lines[0].parts['[data-amount]'].listeners.input();
check('tolerance', r.mismatch.hidden === true, r.difference.textContent);

// garbage counts as zero, no NaN
lines[0].parts['[data-amount]'].value = 'abc';
lines[0].parts['[data-amount]'].listeners.input();
check('garbage', r.sum.textContent.replace(/\s/g, ' ') === '₺34,00', r.sum.textContent);

// lines above the printed total: the difference is negative and carries the real minus sign
r = run('50.00', [line('76,50', true)]);
check('negative difference', r.difference.textContent.replace(/\s/g, ' ') === '−₺26,50', r.difference.textContent);

// no printed total: no mismatch box at all
r = run(undefined, [line('10,00', true)]);
check('no total', r.sum.textContent.replace(/\s/g, ' ') === '₺10,00', r.sum.textContent);

console.log(problems.length ? 'PROBLEMS:\n' + problems.join('\n') : 'receipt-review: all checks ok');
process.exit(problems.length ? 1 : 0);
