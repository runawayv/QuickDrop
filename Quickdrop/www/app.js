'use strict';

let token = localStorage.getItem('qd_token') || '';
let pinRequired = false;
let refreshing = false;

const $ = (id) => document.getElementById(id);

// ---------- i18n ----------
const I18N = {
    ru: {
        pinPrompt: 'Введите PIN-код доступа', pinEnter: 'Войти', pinWrong: 'Неверный PIN',
        sendToPc: 'Отправить на ПК', chooseFiles: 'Выбрать файлы',
        getFromPc: 'Получить с ПК', loading: 'Загрузка…',
        textTitle: 'Текст', textPh: 'Введите текст для отправки на ПК', sendText: 'Отправить текст',
        linkTitle: 'Ссылка', sendLink: 'Отправить ссылку',
        footer: 'Локальная передача без интернета · QuickDrop',
        emptyFiles: 'Пока нет файлов с ПК. Добавьте их в окне QuickDrop.',
        download: 'Скачать', done: 'Готово', needPin: 'Требуется PIN',
        error: 'Ошибка', netError: 'Ошибка сети', sendError: 'Ошибка отправки',
        textSent: 'Текст отправлен на ПК', linkSent: 'Ссылка отправлена на ПК',
        perSec: '/с'
    },
    en: {
        pinPrompt: 'Enter access PIN', pinEnter: 'Sign in', pinWrong: 'Wrong PIN',
        sendToPc: 'Send to PC', chooseFiles: 'Choose files',
        getFromPc: 'Get from PC', loading: 'Loading…',
        textTitle: 'Text', textPh: 'Type text to send to the PC', sendText: 'Send text',
        linkTitle: 'Link', sendLink: 'Send link',
        footer: 'Local transfer, no internet · QuickDrop',
        emptyFiles: 'No files from the PC yet. Add them in the QuickDrop window.',
        download: 'Download', done: 'Done', needPin: 'PIN required',
        error: 'Error', netError: 'Network error', sendError: 'Failed to send',
        textSent: 'Text sent to PC', linkSent: 'Link sent to PC',
        perSec: '/s'
    }
};

const SIZE_UNITS = {
    ru: ['Б', 'КБ', 'МБ', 'ГБ', 'ТБ'],
    en: ['B', 'KB', 'MB', 'GB', 'TB']
};

let lang = localStorage.getItem('qd-lang') ||
    ((navigator.language || 'en').indexOf('ru') === 0 ? 'ru' : 'en');

function t(key) {
    return (I18N[lang] && I18N[lang][key]) || I18N.en[key] || key;
}

function applyI18n() {
    document.documentElement.lang = lang;
    document.querySelectorAll('[data-i18n]').forEach(el => {
        el.textContent = t(el.dataset.i18n);
    });
    document.querySelectorAll('[data-i18n-ph]').forEach(el => {
        el.placeholder = t(el.dataset.i18nPh);
    });
    const sw = $('langSwitch');
    if (sw) sw.value = lang;
}

window.qdSetLang = function (l) {
    if (!I18N[l]) l = 'en';
    lang = l;
    localStorage.setItem('qd-lang', l);
    applyI18n();
    loadFiles(); // перерисовать списки на выбранном языке
};

// ---------- утилиты ----------
function fmtSize(bytes) {
    const units = SIZE_UNITS[lang] || SIZE_UNITS.en;
    let v = bytes;
    let i = 0;
    while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
    return (i === 0 ? v : v.toFixed(1)) + ' ' + units[i];
}

function toast(msg) {
    const t2 = $('toast');
    t2.textContent = msg;
    t2.classList.remove('hidden');
    clearTimeout(t2._timer);
    t2._timer = setTimeout(() => t2.classList.add('hidden'), 2200);
}

function headersWithToken(extra) {
    const h = Object.assign({}, extra || {});
    if (token) h['X-Pin-Token'] = token;
    return h;
}

function showPin(show) {
    $('pinOverlay').classList.toggle('hidden', !show);
}

function setStatus(ok) {
    $('statusDot').classList.toggle('off', !ok);
}

// ---------- списки ----------
async function loadFiles() {
    refreshing = true;
    try {
        const r = await fetch('/api/files', { headers: headersWithToken() });
        if (r.status === 401) {
            if (pinRequired) showPin(true);
            return;
        }
        const j = await r.json();
        renderFiles(j.files || []);
    } catch (e) {
        setStatus(false);
    } finally {
        refreshing = false;
    }
}

function renderFiles(files) {
    const box = $('fileList');
    box.innerHTML = '';
    if (!files.length) {
        const p = document.createElement('p');
        p.className = 'muted';
        p.textContent = t('emptyFiles');
        box.appendChild(p);
        return;
    }
    for (const f of files) {
        const item = document.createElement('div');
        item.className = 'item';
        const url = '/download?id=' + encodeURIComponent(f.id) +
            (token ? '&token=' + encodeURIComponent(token) : '');
        item.innerHTML =
            '<div class="row">' +
            '  <span class="name"></span>' +
            '  <a class="btn dl-btn" href="' + url + '" download></a>' +
            '</div>' +
            '<div class="small">' + fmtSize(f.size) + '</div>';
        item.querySelector('.name').textContent = f.name; // textContent — защита от XSS
        item.querySelector('.dl-btn').textContent = t('download');
        box.appendChild(item);
    }
}

// ---------- загрузка файлов ----------
function uploadOne(file) {
    return new Promise((resolve) => {
        const item = document.createElement('div');
        item.className = 'item';
        item.innerHTML =
            '<div class="row">' +
            '  <span class="name"></span>' +
            '  <span class="pct small">0%</span>' +
            '</div>' +
            '<div class="bar"><div class="fill"></div></div>' +
            '<div class="row small">' +
            '  <span class="vol"></span>' +
            '  <span class="spd"></span>' +
            '</div>';
        item.querySelector('.name').textContent = file.name;
        $('uploadList').prepend(item);

        const fill = item.querySelector('.fill');
        const pct = item.querySelector('.pct');
        const vol = item.querySelector('.vol');
        const spd = item.querySelector('.spd');

        let lastLoaded = 0;
        let lastTime = Date.now();
        let speed = 0;

        const fd = new FormData();
        fd.append('file', file, file.name);

        const xhr = new XMLHttpRequest();
        xhr.open('POST', '/upload');
        if (token) xhr.setRequestHeader('X-Pin-Token', token);

        xhr.upload.onprogress = (ev) => {
            if (!ev.lengthComputable) return;
            fill.style.width = ((ev.loaded / ev.total) * 100).toFixed(1) + '%';
            pct.textContent = Math.floor((ev.loaded / ev.total) * 100) + '%';
            vol.textContent = fmtSize(ev.loaded) + ' / ' + fmtSize(ev.total);
            const now = Date.now();
            const dt = (now - lastTime) / 1000;
            if (dt >= 0.3) {
                const instant = (ev.loaded - lastLoaded) / dt;
                speed = speed ? speed * 0.6 + instant * 0.4 : instant;
                lastLoaded = ev.loaded;
                lastTime = now;
                spd.textContent = fmtSize(speed) + t('perSec');
            }
        };

        xhr.onload = () => {
            if (xhr.status === 200) {
                fill.style.width = '100%';
                pct.textContent = t('done');
                vol.textContent = fmtSize(file.size);
                spd.textContent = '';
            } else if (xhr.status === 401) {
                pct.textContent = t('needPin');
                if (pinRequired) showPin(true);
            } else {
                pct.textContent = t('error');
            }
            resolve();
        };
        xhr.onerror = () => { pct.textContent = t('netError'); resolve(); };
        xhr.send(fd);
    });
}

// ---------- текст и ссылки ----------
async function sendJson(url, payload, okMsg) {
    try {
        const r = await fetch(url, {
            method: 'POST',
            headers: headersWithToken({ 'Content-Type': 'application/json' }),
            body: JSON.stringify(payload)
        });
        if (r.status === 401) {
            if (pinRequired) showPin(true);
            return;
        }
        toast(r.ok ? okMsg : t('sendError'));
    } catch (e) {
        toast(t('netError'));
    }
}

// ---------- инициализация ----------
async function init() {
    applyI18n();
    try {
        const r = await fetch('/api/info');
        const j = await r.json();
        pinRequired = !!j.requirePin;
        setStatus(true);
        if (pinRequired && !token) showPin(true);
        else loadFiles();
    } catch (e) {
        setStatus(false);
    }
    setInterval(() => {
        if (!refreshing && $('pinOverlay').classList.contains('hidden')) loadFiles();
    }, 3000);
}

$('pinBtn').addEventListener('click', async () => {
    const pin = $('pinInput').value.trim();
    if (!pin) return;
    try {
        const r = await fetch('/api/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ pin: pin })
        });
        if (r.ok) {
            const j = await r.json();
            token = j.token || '';
            localStorage.setItem('qd_token', token);
            $('pinError').classList.add('hidden');
            showPin(false);
            loadFiles();
        } else {
            $('pinError').classList.remove('hidden');
        }
    } catch (e) {
        $('pinError').classList.remove('hidden');
    }
});

$('pinInput').addEventListener('keydown', (e) => {
    if (e.key === 'Enter') $('pinBtn').click();
});

$('fileInput').addEventListener('change', async (e) => {
    const files = Array.from(e.target.files || []);
    e.target.value = '';
    for (const f of files) {
        await uploadOne(f);
    }
    loadFiles();
});

$('textBtn').addEventListener('click', () => {
    const v = $('textInput').value.trim();
    if (!v) return;
    sendJson('/api/text', { text: v }, t('textSent'));
    $('textInput').value = '';
});

$('linkBtn').addEventListener('click', () => {
    let v = $('linkInput').value.trim();
    if (!v) return;
    if (!/^https?:\/\//i.test(v)) v = 'https://' + v;
    sendJson('/api/link', { link: v }, t('linkSent'));
    $('linkInput').value = '';
});

$('langSwitch').addEventListener('change', (e) => window.qdSetLang(e.target.value));

init();