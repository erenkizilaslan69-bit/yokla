// Receipt upload: shows a preview and shrinks the photo in the browser before it is sent, so that a 12-megapixel
// camera picture travels as a JPEG of a few hundred kilobytes (cheaper, faster, kinder to the free quota).
// If the browser cannot decode the picture (e.g. HEIC outside Safari), the original file is sent unchanged and
// the server's size limit applies.
(function () {
    'use strict';

    const form = document.getElementById('receiptForm');
    if (!form) return;

    const maxEdge = parseInt(form.dataset.maxEdge, 10) || 2048;
    const maxBytes = parseInt(form.dataset.maxBytes, 10) || 4000000;
    const inputs = Array.from(form.querySelectorAll('.receipt-input'));
    const submit = document.getElementById('receiptSubmit');
    const preview = document.getElementById('receiptPreview');
    const previewImage = preview.querySelector('img');
    const previewInfo = preview.querySelector('.receipt-preview-info');
    let previewUrl = null;

    submit.disabled = true;

    function kilobytes(bytes) {
        return bytes >= 1000000 ? (bytes / 1000000).toFixed(1).replace('.', ',') + ' MB' : Math.round(bytes / 1000) + ' KB';
    }

    async function shrink(file) {
        if (!window.createImageBitmap || !window.DataTransfer) return null;
        try {
            const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
            const scale = Math.min(1, maxEdge / Math.max(bitmap.width, bitmap.height));
            const canvas = document.createElement('canvas');
            canvas.width = Math.round(bitmap.width * scale);
            canvas.height = Math.round(bitmap.height * scale);
            canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height);
            if (bitmap.close) bitmap.close();
            const blob = await new Promise(function (resolve) { canvas.toBlob(resolve, 'image/jpeg', 0.85); });
            return blob ? { blob: blob, width: canvas.width, height: canvas.height } : null;
        } catch (e) {
            return null;
        }
    }

    async function choose(input) {
        const file = input.files && input.files[0];
        if (!file) return;
        // Only one of "camera" and "gallery" is sent.
        inputs.forEach(function (other) { if (other !== input) other.value = ''; });

        submit.disabled = true;
        previewInfo.textContent = 'Fotoğraf hazırlanıyor…';
        preview.hidden = false;

        const small = await shrink(file);
        let sent = file;
        if (small && small.blob.size < file.size) {
            const transfer = new DataTransfer();
            sent = new File([small.blob], 'fis.jpg', { type: 'image/jpeg' });
            transfer.items.add(sent);
            input.files = transfer.files;
        }

        if (previewUrl) URL.revokeObjectURL(previewUrl);
        previewUrl = URL.createObjectURL(sent);
        previewImage.hidden = false;
        previewImage.onerror = function () { previewImage.hidden = true; }; // e.g. HEIC that cannot be shown
        previewImage.src = previewUrl;

        if (sent.size > maxBytes) {
            previewInfo.textContent = 'Fotoğraf çok büyük (' + kilobytes(sent.size) + '). Daha küçük bir fotoğraf seç.';
            return;
        }
        previewInfo.textContent = small
            ? 'Gönderilecek: ' + small.width + ' × ' + small.height + ' piksel, ' + kilobytes(sent.size)
            : 'Gönderilecek: ' + kilobytes(sent.size);
        submit.disabled = false;
    }

    inputs.forEach(function (input) {
        input.addEventListener('change', function () { choose(input); });
    });

    form.addEventListener('submit', function () {
        submit.disabled = true;
        submit.querySelector('span').textContent = 'Fiş okunuyor…';
    });
})();
