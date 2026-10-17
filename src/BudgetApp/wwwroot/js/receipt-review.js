// Confirmation screens (receipt, quick entry): removing / restoring lines, the number of lines and, on a receipt,
// a running total compared with the receipt's total. Amounts are typed the Turkish way ("1.234,50").
// Without this script the "Kaydet" tick boxes do the same job.
(function () {
    'use strict';

    const form = document.getElementById('receiptReview') || document.getElementById('quickEntryReview');
    if (!form) return;

    const TOLERANCE = 0.05; // ReceiptMath.TotalTolerance
    const total = form.dataset.total ? parseFloat(form.dataset.total) : null;
    const currency = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' });
    // Same as the server (AppCulture): a negative amount carries the real minus sign (U+2212), not a hyphen.
    const money = { format: function (value) { return currency.format(value).replace(/^-/, '−'); } };
    const lines = Array.from(form.querySelectorAll('[data-line]'));

    function parseAmount(text) {
        const cleaned = (text || '').replace(/[\s₺]/g, '').replace(/\./g, '').replace(',', '.');
        const value = parseFloat(cleaned);
        return isFinite(value) ? value : 0;
    }

    function update() {
        let sum = 0;
        let count = 0;
        lines.forEach(function (line) {
            const included = line.querySelector('[data-include]').checked;
            line.classList.toggle('is-removed', !included);
            line.querySelector('[data-remove]').hidden = !included;
            line.querySelector('[data-restore]').hidden = included;
            if (included) {
                sum += parseAmount(line.querySelector('[data-amount]').value);
                count++;
            }
        });

        sum = Math.round(sum * 100) / 100;
        // Quick entry mixes income and expenses, so it shows only the count.
        const linesTotal = form.querySelector('[data-lines-total]');
        if (linesTotal) linesTotal.textContent = money.format(sum);
        form.querySelector('[data-count]').textContent = count;

        const mismatch = form.querySelector('[data-mismatch]');
        if (mismatch && total !== null) {
            const difference = Math.round((total - sum) * 100) / 100;
            mismatch.hidden = Math.abs(difference) <= TOLERANCE;
            mismatch.querySelector('[data-difference]').textContent = money.format(difference);
        }
    }

    lines.forEach(function (line) {
        const include = line.querySelector('[data-include]');
        line.querySelector('.receipt-include').hidden = true; // the buttons take over
        line.querySelector('[data-remove]').addEventListener('click', function () { include.checked = false; update(); });
        line.querySelector('[data-restore]').addEventListener('click', function () { include.checked = true; update(); });
        line.querySelector('[data-amount]').addEventListener('input', update);
    });

    update();
})();
