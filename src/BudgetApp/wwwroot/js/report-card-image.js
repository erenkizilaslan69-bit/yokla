// Report card as a shareable picture (1080 x 1920, the size of a story).
// The picture is drawn in the browser on a <canvas>; nothing is rendered on the server and no library is needed.
// It is built only from the JSON in #report-data, which carries the month, letters, points and percentages:
// no amount of money and no user name ever reaches this script. The look is fixed (dark green and gold), so the
// picture is the same whichever theme the page is in.
(function () {
    'use strict';

    const WIDTH = 1080;
    const HEIGHT = 1920;
    const MARGIN = 80;
    const MAX_CATEGORIES = 4; // more would run into the footer
    const FONT = '"Inter Variable", system-ui, -apple-system, "Segoe UI", sans-serif';

    const TONES = {
        good: { fill: '#E3F4EE', ink: '#0A4F3B' },
        mid: { fill: '#FFF4D6', ink: '#5F4508' },
        low: { fill: '#FBE9E9', ink: '#7A2323' },
        none: { fill: 'rgba(255, 255, 255, .16)', ink: '#FFFFFF' }
    };

    function tone(letter) {
        if (!letter) return TONES.none;
        if (letter === 'AA' || letter === 'BA' || letter === 'BB') return TONES.good;
        if (letter === 'FD' || letter === 'FF') return TONES.low;
        return TONES.mid;
    }

    function font(ctx, weight, size) {
        ctx.font = weight + ' ' + size + 'px ' + FONT;
    }

    function roundedRect(ctx, x, y, w, h, r) {
        ctx.beginPath();
        ctx.moveTo(x + r, y);
        ctx.arcTo(x + w, y, x + w, y + h, r);
        ctx.arcTo(x + w, y + h, x, y + h, r);
        ctx.arcTo(x, y + h, x, y, r);
        ctx.arcTo(x, y, x + w, y, r);
        ctx.closePath();
    }

    // The few non-ASCII characters this file draws itself are built from their codes, so the picture does not
    // depend on how the script file is decoded.
    const ELLIPSIS = String.fromCharCode(0x2026);
    const DASH = String.fromCharCode(0x2014);
    const DOTTED_I = String.fromCharCode(0x130);

    // Shortens a text with an ellipsis until it fits.
    function fit(ctx, text, maxWidth) {
        if (ctx.measureText(text).width <= maxWidth) return text;
        let cut = text;
        while (cut.length > 1 && ctx.measureText(cut + ELLIPSIS).width > maxWidth) cut = cut.slice(0, -1);
        return cut.trimEnd() + ELLIPSIS;
    }

    function wrap(ctx, text, maxWidth, maxLines) {
        const lines = [];
        let line = '';
        text.split(/\s+/).forEach(function (word) {
            const candidate = line ? line + ' ' + word : word;
            if (line && ctx.measureText(candidate).width > maxWidth) {
                lines.push(line);
                line = word;
            } else {
                line = candidate;
            }
        });
        if (line) lines.push(line);
        if (lines.length > maxLines) {
            // the last line that fits takes the next one too, so fit() cuts it and ends it with an ellipsis
            const last = lines[maxLines - 1] + ' ' + lines[maxLines];
            lines.length = maxLines;
            lines[maxLines - 1] = fit(ctx, last, maxWidth);
        }
        return lines;
    }

    function chip(ctx, letter, right, centerY, width, height, size) {
        const colours = tone(letter);
        ctx.fillStyle = colours.fill;
        roundedRect(ctx, right - width, centerY - height / 2, width, height, height * 0.3);
        ctx.fill();
        ctx.fillStyle = colours.ink;
        font(ctx, 800, size);
        ctx.textAlign = 'center';
        ctx.fillText(letter || DASH, right - width / 2, centerY);
    }

    // One line of the transcript: name on the left, value and letter on the right.
    function row(ctx, name, value, letter, y, height, nameSize) {
        const right = WIDTH - MARGIN - 36;
        const chipWidth = 132;
        const centerY = y + height / 2;

        chip(ctx, letter, right, centerY, chipWidth, height - 34, nameSize);

        ctx.textAlign = 'right';
        ctx.fillStyle = 'rgba(255, 255, 255, .78)';
        font(ctx, 600, nameSize - 6);
        const valueText = value || '';
        const valueWidth = ctx.measureText(valueText).width;
        const valueRight = right - chipWidth - 28;
        ctx.fillText(valueText, valueRight, centerY);

        ctx.textAlign = 'left';
        ctx.fillStyle = '#FFFFFF';
        font(ctx, 700, nameSize);
        const left = MARGIN + 36;
        ctx.fillText(fit(ctx, name, valueRight - valueWidth - 28 - left), left, centerY);
    }

    function draw(ctx, data) {
        // background
        const background = ctx.createLinearGradient(0, 0, WIDTH, HEIGHT);
        background.addColorStop(0, '#075E47');
        background.addColorStop(0.55, '#0A4A39');
        background.addColorStop(1, '#06342A');
        ctx.fillStyle = background;
        ctx.fillRect(0, 0, WIDTH, HEIGHT);
        ctx.fillStyle = 'rgba(255, 255, 255, .05)';
        ctx.beginPath(); ctx.arc(WIDTH - 60, 140, 320, 0, Math.PI * 2); ctx.fill();
        ctx.beginPath(); ctx.arc(80, HEIGHT - 120, 260, 0, Math.PI * 2); ctx.fill();

        ctx.textBaseline = 'middle';

        // heading
        ctx.textAlign = 'center';
        ctx.fillStyle = 'rgba(255, 255, 255, .78)';
        font(ctx, 700, 34);
        ctx.fillText('AY KARNES' + DOTTED_I, WIDTH / 2, 150);
        ctx.fillStyle = '#FFFFFF';
        font(ctx, 800, 76);
        ctx.fillText(fit(ctx, data.month, WIDTH - 2 * MARGIN), WIDTH / 2, 240);

        // the overall grade: a gold tile, as on the app's gold cards
        const tile = 300;
        const tileX = (WIDTH - tile) / 2;
        const tileY = 330;
        const gold = ctx.createLinearGradient(tileX, tileY, tileX + tile, tileY + tile);
        gold.addColorStop(0, '#F3C34F');
        gold.addColorStop(1, '#E0A526');
        ctx.fillStyle = gold;
        roundedRect(ctx, tileX, tileY, tile, tile, 72);
        ctx.fill();
        ctx.fillStyle = '#5F4508';
        font(ctx, 800, 168);
        ctx.fillText(data.letter, WIDTH / 2, tileY + tile / 2 + 8);

        ctx.fillStyle = '#FFFFFF';
        font(ctx, 800, 56);
        ctx.fillText(data.points + ' / 100', WIDTH / 2, 700);

        let y = 770;
        if (data.comparison) {
            ctx.fillStyle = 'rgba(255, 255, 255, .78)';
            font(ctx, 500, 34);
            wrap(ctx, data.comparison, WIDTH - 2 * MARGIN - 40, 2).forEach(function (line) {
                ctx.fillText(line, WIDTH / 2, y);
                y += 46;
            });
        }
        y += 24;

        // the courses
        const courses = data.courses || [];
        const courseHeight = 108;
        ctx.fillStyle = 'rgba(255, 255, 255, .09)';
        roundedRect(ctx, MARGIN, y, WIDTH - 2 * MARGIN, courses.length * courseHeight + 24, 36);
        ctx.fill();
        y += 12;
        courses.forEach(function (course) {
            row(ctx, course.name, course.value, course.letter, y, courseHeight, 40);
            y += courseHeight;
        });
        y += 12;

        // the limited categories, as far as they fit
        const categories = data.categories || [];
        if (categories.length > 0) {
            y += 56;
            ctx.textAlign = 'left';
            ctx.fillStyle = 'rgba(255, 255, 255, .78)';
            font(ctx, 700, 30);
            ctx.fillText('KATEGORI LIMITLERI'.replace(/I/g, DOTTED_I), MARGIN + 36, y);
            y += 30;

            const shown = categories.slice(0, MAX_CATEGORIES);
            const categoryHeight = 86;
            shown.forEach(function (category) {
                row(ctx, category.name, category.value, category.letter, y, categoryHeight, 34);
                y += categoryHeight;
            });
            if (categories.length > shown.length) {
                ctx.textAlign = 'left';
                ctx.fillStyle = 'rgba(255, 255, 255, .78)';
                font(ctx, 500, 28);
                ctx.fillText('+' + (categories.length - shown.length) + ' kategori daha', MARGIN + 36, y + 26);
            }
        }

        // footer
        ctx.textAlign = 'center';
        ctx.fillStyle = 'rgba(255, 255, 255, .78)';
        font(ctx, 600, 30);
        ctx.fillText('BudgetApp', WIDTH / 2, HEIGHT - 84);
    }

    function fontsReady() {
        if (!document.fonts || !document.fonts.load) return Promise.resolve();
        return Promise.all([
            document.fonts.load('800 100px "Inter Variable"'),
            document.fonts.load('500 34px "Inter Variable"')
        ]).catch(function () { /* the fallback font is fine */ });
    }

    function toBlob(canvas) {
        return new Promise(function (resolve) {
            if (canvas.toBlob) {
                canvas.toBlob(resolve, 'image/png');
            } else {
                resolve(null);
            }
        });
    }

    function download(canvas, blob, fileName) {
        const link = document.createElement('a');
        const url = blob ? URL.createObjectURL(blob) : canvas.toDataURL('image/png');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        if (blob) setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
    }

    function init() {
        const source = document.getElementById('report-data');
        const canvas = document.getElementById('report-canvas');
        if (!source || !canvas || !canvas.getContext) return;

        const data = JSON.parse(source.textContent);
        canvas.width = WIDTH;
        canvas.height = HEIGHT;
        const ready = fontsReady().then(function () { draw(canvas.getContext('2d'), data); });

        const downloadButton = document.querySelector('[data-report-download]');
        if (downloadButton) {
            downloadButton.addEventListener('click', function () {
                ready.then(function () { return toBlob(canvas); })
                    .then(function (blob) { download(canvas, blob, data.file); });
            });
        }

        // Sharing a file straight to another app only exists on some (mostly mobile) browsers.
        const shareButton = document.querySelector('[data-report-share]');
        if (shareButton && navigator.canShare && window.File) {
            let probe = null;
            try { probe = new File([''], data.file, { type: 'image/png' }); } catch (e) { probe = null; }
            if (probe && navigator.canShare({ files: [probe] })) {
                shareButton.hidden = false;
                shareButton.addEventListener('click', function () {
                    ready.then(function () { return toBlob(canvas); }).then(function (blob) {
                        if (!blob) return;
                        const file = new File([blob], data.file, { type: 'image/png' });
                        return navigator.share({ files: [file], title: data.month + ' karnem' });
                    }).catch(function () { /* the user closed the share sheet */ });
                });
            }
        }
    }

    window.ReportCardImage = { draw: draw, width: WIDTH, height: HEIGHT };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
