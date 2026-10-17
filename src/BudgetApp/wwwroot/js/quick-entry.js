// "Söyle ya da yaz": records the user's voice with MediaRecorder and sends the recording itself (no speech-to-text
// in the browser) through the form's hidden file field. Recording stops by itself after data-max-seconds.
// Where the browser cannot record (no HTTPS, no MediaRecorder) the microphone stays hidden and the text box is the
// way in; the text form works without this script.
(function () {
    'use strict';

    const form = document.getElementById('quickEntryForm');
    if (!form) return;

    const maxSeconds = parseInt(form.dataset.maxSeconds, 10) || 30;
    const maxBytes = parseInt(form.dataset.maxBytes, 10) || 2000000;
    const MIN_SECONDS = 0.5;
    const COUNTDOWN_FROM = 5;

    const mic = form.querySelector('[data-mic]');
    const button = form.querySelector('[data-record]');
    const label = form.querySelector('[data-record-label]');
    const status = form.querySelector('[data-status]');
    const errorBox = form.querySelector('[data-mic-error]');
    const errorText = form.querySelector('[data-mic-error-text]');
    const audioInput = form.querySelector('[data-audio]');
    const text = form.querySelector('textarea[name="text"]');
    const textLabel = form.querySelector('[data-text-label]');
    const submit = form.querySelector('[data-submit]');

    function busy() {
        submit.disabled = true;
        submit.querySelector('span').textContent = 'Ayırıyorum…';
        if (button) button.disabled = true;
    }

    // Typed text: the button locks so a double tap does not send (and count) it twice.
    form.addEventListener('submit', function () {
        if (submit.disabled) return;
        busy();
    });

    function canRecord() {
        if (!window.isSecureContext || !navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) return false;
        if (typeof window.MediaRecorder !== 'function') return false;
        try { new DataTransfer(); } catch (e) { return false; }
        return true;
    }
    if (!canRecord()) return;

    mic.hidden = false;
    if (textLabel) textLabel.textContent = 'Ya da yaz';

    // The first type the browser can record: WebM/Opus (Chrome, Edge, Firefox), Ogg/Opus, MP4/AAC (Safari).
    function pickType() {
        const types = ['audio/webm;codecs=opus', 'audio/webm', 'audio/ogg;codecs=opus', 'audio/mp4'];
        if (typeof MediaRecorder.isTypeSupported !== 'function') return '';
        for (let i = 0; i < types.length; i++) if (MediaRecorder.isTypeSupported(types[i])) return types[i];
        return '';
    }

    function extensionOf(type) {
        if (type.indexOf('webm') >= 0) return 'webm';
        if (type.indexOf('ogg') >= 0) return 'ogg';
        if (type.indexOf('mp4') >= 0 || type.indexOf('aac') >= 0) return 'm4a';
        return 'audio';
    }

    function showError(message) {
        errorText.textContent = message;
        errorBox.hidden = false;
        text.focus();
    }

    function clock(seconds) {
        const s = Math.floor(seconds);
        return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
    }

    let recorder = null;
    let stream = null;
    let chunks = [];
    let startedAt = 0;
    let timer = null;
    let cancelled = false;

    function setRecording(on) {
        button.classList.toggle('is-recording', on);
        button.setAttribute('aria-pressed', on ? 'true' : 'false');
        label.textContent = on ? 'Bitir' : 'Konuşmak için dokun';
        submit.disabled = on;
    }

    function tick() {
        const elapsed = (Date.now() - startedAt) / 1000;
        const left = maxSeconds - elapsed;
        status.textContent = left <= COUNTDOWN_FROM
            ? 'Dinliyorum… ' + Math.max(0, Math.ceil(left)) + ' saniye kaldı'
            : 'Dinliyorum… ' + clock(elapsed) + ' / ' + clock(maxSeconds);
        if (left <= 0) stop();
    }

    function release() {
        clearInterval(timer);
        timer = null;
        if (stream) stream.getTracks().forEach(function (track) { track.stop(); });
        stream = null;
    }

    function stop() {
        if (recorder && recorder.state !== 'inactive') recorder.stop();
    }

    function finished(type) {
        const seconds = (Date.now() - startedAt) / 1000;
        release();
        setRecording(false);
        if (cancelled) { status.textContent = ''; return; }

        const blob = new Blob(chunks, { type: type || (chunks[0] && chunks[0].type) || 'audio/webm' });
        chunks = [];
        if (seconds < MIN_SECONDS || blob.size === 0) {
            status.textContent = 'Çok kısa oldu; düğmeye dokunup biraz daha uzun konuş.';
            return;
        }
        if (blob.size > maxBytes) {
            status.textContent = '';
            showError('Kayıt çok büyük oldu. Daha kısa söyle ya da yaz.');
            return;
        }

        const file = new File([blob], 'kayit.' + extensionOf(blob.type), { type: blob.type });
        const transfer = new DataTransfer();
        transfer.items.add(file);
        audioInput.files = transfer.files;
        text.value = ''; // the recording is what gets read
        status.textContent = 'Ayırıyorum…';
        busy();
        form.submit();
    }

    async function start() {
        errorBox.hidden = true;
        cancelled = false;
        try {
            stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        } catch (e) {
            const name = e && e.name;
            if (name === 'NotAllowedError' || name === 'SecurityError')
                showError('Mikrofon izni verilmedi. Tarayıcının site ayarlarından izin verebilir ya da aşağıya yazabilirsin.');
            else if (name === 'NotFoundError' || name === 'OverconstrainedError')
                showError('Mikrofon bulunamadı. Aşağıya yazarak ekleyebilirsin.');
            else
                showError('Mikrofon açılamadı. Aşağıya yazarak ekleyebilirsin.');
            return;
        }

        const type = pickType();
        try {
            recorder = type ? new MediaRecorder(stream, { mimeType: type }) : new MediaRecorder(stream);
        } catch (e) {
            release();
            showError('Bu tarayıcıda ses kaydı başlatılamadı. Aşağıya yazarak ekleyebilirsin.');
            return;
        }

        chunks = [];
        recorder.addEventListener('dataavailable', function (event) { if (event.data && event.data.size) chunks.push(event.data); });
        recorder.addEventListener('stop', function () { finished(recorder.mimeType || type); });
        recorder.start(250);
        startedAt = Date.now();
        setRecording(true);
        tick();
        timer = setInterval(tick, 250);
    }

    button.addEventListener('click', function () {
        if (recorder && recorder.state === 'recording') stop();
        else start();
    });

    // Leaving the page (or Escape) while recording drops the recording and frees the microphone.
    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape' && recorder && recorder.state === 'recording') {
            cancelled = true;
            stop();
        }
    });
    window.addEventListener('pagehide', function () {
        cancelled = true;
        stop();
        release();
    });
})();
