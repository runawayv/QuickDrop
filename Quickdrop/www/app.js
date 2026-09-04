'use strict';

let token = localStorage.getItem('qd_token') || '';
let pinRequired = false;
let refreshing = false;

const $ = (id) => document.getElementById(id);

function fmtSize(bytes) {
    const units = ['Б', 'КБ', 'МБ', 'ГБ', 'ТБ'];
    let v = bytes;
    let i = 0;
    while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
    return (i === 0 ? v : v.toFixed(1)) + ' ' + units[i];
}

function toast(msg) {
    const t = $('toast');
    t.textContent = msg;
    t.classList.remove('hidden');
    clearTimeout(t._timer);
    t._timer = setTimeout(() => t.classList.add('hidden'), 2200);
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
        p.textContent = 'Пока нет файлов с ПК. Добавьте их в окне QuickDrop.';
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
            '  <a class="btn dl-btn" href="' + url + '" download>Скачать</a>' +
            '</div>' +
            '<div class="small">' + fmtSize(f.size) + '</div>';
        item.querySelector('.name').textContent = f.name; // textContent — защита от XSS
        box.appendChild(item);
    }
}

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
                spd.textContent = fmtSize(speed) + '/с';
            }
        };

        xhr.onload = () => {
            if (xhr.status === 200) {
                fill.style.width = '100%';
                pct.textContent = 'Готово';
                vol.textContent = fmtSize(file.size);
                spd.textContent = '';
            } else if (xhr.status === 401) {
                pct.textContent = 'Требуется PIN';
                if (pinRequired) showPin(true);
            } else {
                pct.textContent = 'Ошибка';
            }
            resolve();
        };
        xhr.onerror = () => { pct.textContent = 'Ошибка сети'; resolve(); };
        xhr.send(fd);
    });
}

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
        toast(r.ok ? okMsg : 'Ошибка отправки');
    } catch (e) {
        toast('Ошибка сети');
    }
}

async function init() {
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
    sendJson('/api/text', { text: v }, 'Текст отправлен на ПК');
    $('textInput').value = '';
});

$('linkBtn').addEventListener('click', () => {
    let v = $('linkInput').value.trim();
    if (!v) return;
    if (!/^https?:\/\//i.test(v)) v = 'https://' + v;
    sendJson('/api/link', { link: v }, 'Ссылка отправлена на ПК');
    $('linkInput').value = '';
});

init();