/*
    photo-resize-probe.mjs — ขับ Chrome จริงเพื่อวัด "ไฟล์ที่ถูกส่งขึ้น server จริง ๆ" (UAT 4.5 / BR-3)

    ทำไมต้องใช้เบราว์เซอร์จริง : การย่อรูปเกิดใน <canvas> ของ photo-upload.js ฝั่ง client
    การยิง HTTP ตรงแบบสคริปต์ทดสอบตัวอื่นจึงข้ามส่วนที่ต้องพิสูจน์ไปทั้งหมด

    ไม่พึ่ง puppeteer/playwright — คุยกับ Chrome ผ่าน DevTools Protocol ด้วย WebSocket
    ที่ Node มีมาให้ในตัวตั้งแต่ v22 จึงไม่ต้องเพิ่ม dependency ใด ๆ (§10 ข้อ 8)

    วัด 2 ทางเทียบกันเสมอ :
      1. ขนาดของ File ที่ถูกใส่ลง FormData ก่อนเรียก fetch (ดักที่ตัวหน้าเว็บ)
      2. Content-Length ที่ Chrome ส่งออกไปจริง (ดักที่ชั้น network ของเบราว์เซอร์)
    ตัวที่ 2 เชื่อได้กว่าเพราะหน้าเว็บแตะไม่ได้ ส่วนตัวที่ 1 บอกได้ว่าไฟล์ถูกแปลงเป็นอะไร

    ผลลัพธ์พิมพ์เป็น JSON บรรทัดเดียวขึ้นต้นด้วย ===RESULT=== ให้ฝั่ง PowerShell อ่านต่อ
*/
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [baseUrl, empCode, reqId, filePath, photoType] = process.argv.slice(2);
if (!baseUrl || !empCode || !reqId || !filePath) {
    console.error('ใช้: node photo-resize-probe.mjs <baseUrl> <empCode> <reqId> <filePath> [photoType]');
    process.exit(2);
}

const CHROME = [
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe'
].find(existsSync);
if (!CHROME) { console.error('ไม่พบ Chrome หรือ Edge บนเครื่องนี้'); process.exit(2); }

const PORT = 9333 + (process.pid % 200);
const profile = mkdtempSync(join(tmpdir(), 'uat45-'));
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

const chrome = spawn(CHROME, [
    '--headless=new',
    '--remote-debugging-port=' + PORT,
    '--user-data-dir=' + profile,
    '--no-first-run', '--no-default-browser-check', '--disable-gpu',
    '--disable-extensions', '--disable-background-networking',
    'about:blank'
], { stdio: 'ignore' });

/** รอจน DevTools endpoint ตอบ แล้วคืน ws ของแท็บแรก */
async function attach() {
    for (let i = 0; i < 60; i++) {
        try {
            const list = await (await fetch('http://127.0.0.1:' + PORT + '/json/list')).json();
            const page = list.find(t => t.type === 'page' && t.webSocketDebuggerUrl);
            if (page) return page.webSocketDebuggerUrl;
        } catch { /* ยังไม่ขึ้น */ }
        await sleep(250);
    }
    throw new Error('Chrome ไม่เปิด DevTools endpoint');
}

const ws = new WebSocket(await attach());
await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });

let nextId = 0;
const pending = new Map();
const events = [];
ws.onmessage = (m) => {
    const msg = JSON.parse(m.data);
    if (msg.id !== undefined) {
        const p = pending.get(msg.id);
        if (!p) return;
        pending.delete(msg.id);
        if (msg.error) p.reject(new Error(JSON.stringify(msg.error)));
        else p.resolve(msg.result);
    } else {
        events.push(msg);
    }
};

function send(method, params = {}) {
    const id = ++nextId;
    return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject });
        ws.send(JSON.stringify({ id, method, params }));
        setTimeout(() => { if (pending.delete(id)) reject(new Error(method + ' ไม่ตอบใน 60 วินาที')); }, 60000);
    });
}

async function evaluate(expression) {
    const r = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) {
        const detail = r.exceptionDetails.exception ? r.exceptionDetails.exception.description : '';
        throw new Error(r.exceptionDetails.text + ' ' + detail);
    }
    return r.result.value;
}

async function waitFor(fn, what, timeoutMs = 90000) {
    const until = Date.now() + timeoutMs;
    while (Date.now() < until) {
        const value = await fn();
        if (value) return value;
        await sleep(200);
    }
    throw new Error('หมดเวลารอ: ' + what);
}

async function navigate(url) {
    const before = events.length;
    await send('Page.navigate', { url });
    await waitFor(async () => events.slice(before).some(e => e.method === 'Page.loadEventFired'), 'โหลดหน้า ' + url);
    await sleep(200);   // ให้สคริปต์ในหน้าผูก event handler ให้เสร็จก่อน
}

const result = { chrome: CHROME };
try {
    await send('Page.enable');
    await send('Network.enable');
    await send('Runtime.enable');
    await send('DOM.enable');

    // 1. เข้าสู่ระบบผ่านหน้าจอจริง (SSO จำลอง — ไม่มีรหัสผ่าน)
    await navigate(baseUrl + '/Account/Login');
    await evaluate(
        "document.getElementById('EmpCode').value = " + JSON.stringify(empCode) + ";" +
        "document.getElementById('EmpCode').form.submit(); true;");
    await waitFor(async () => (await evaluate('location.pathname')) !== '/Account/Login', 'เข้าสู่ระบบ');

    // 2. เปิดใบงานที่สถานะ "กำลังส่ง" แล้วดักตัวส่งไฟล์
    await navigate(baseUrl + '/Requests/Details/' + reqId);
    const hasForm = await evaluate("!!document.getElementById('photoUploadForm')");
    if (!hasForm) throw new Error('หน้า /Requests/Details/' + reqId + ' ไม่มีฟอร์มอัปโหลดรูป (สถานะใบงานถูกต้องหรือยัง)');

    // เก็บผลลง sessionStorage เพราะ photo-upload.js สั่ง location.reload() เมื่อสำเร็จ
    // ตัวแปรใน window จะหายไปพร้อมการโหลดหน้าใหม่ แต่ sessionStorage อยู่รอด
    await evaluate([
        "sessionStorage.removeItem('uat45');",
        "(function () {",
        "  var orig = window.fetch;",
        "  window.fetch = function (url, init) {",
        "    var record = { url: String(url) };",
        "    try {",
        "      var f = init && init.body && init.body.get && init.body.get('file');",
        "      if (f) { record.name = f.name; record.size = f.size; record.type = f.type; }",
        "    } catch (e) { record.captureError = String(e); }",
        "    return orig.apply(this, arguments).then(function (res) {",
        "      record.status = res.status;",
        "      sessionStorage.setItem('uat45', JSON.stringify(record));",
        "      return res;",
        "    }, function (err) {",
        "      record.networkError = String(err);",
        "      sessionStorage.setItem('uat45', JSON.stringify(record));",
        "      throw err;",
        "    });",
        "  };",
        "})(); true;"
    ].join('\n'));

    // 3. ใส่ไฟล์ลงช่องเลือกไฟล์เหมือนผู้ใช้เลือกเอง
    const doc = await send('DOM.getDocument');
    const input = await send('DOM.querySelector', { nodeId: doc.root.nodeId, selector: '#photoFile' });
    await send('DOM.setFileInputFiles', { nodeId: input.nodeId, files: [filePath] });
    if (photoType) {
        await evaluate("document.getElementById('photoType').value = " + JSON.stringify(photoType) + "; true;");
    }

    const firstEvent = events.length;
    const hintBefore = await evaluate("(document.getElementById('photoUploadHint') || {}).textContent || ''");
    await evaluate("document.getElementById('photoUploadButton').click(); true;");

    // 4. รอผล — สำเร็จ = มี record ใน sessionStorage · ถูกปฏิเสธก่อนส่ง = ข้อความใต้ฟอร์มเปลี่ยน
    const captured = await waitFor(async () => {
        const raw = await evaluate("sessionStorage.getItem('uat45')");
        if (raw) return { kind: 'sent', record: JSON.parse(raw) };
        const hint = await evaluate("(document.getElementById('photoUploadHint') || {}).textContent || ''");
        if (hint && hint !== hintBefore && !/กำลัง/.test(hint)) return { kind: 'rejected', hint: hint.trim() };
        return null;
    }, 'ผลการอัปโหลด');

    result.outcome = captured.kind;
    if (captured.kind === 'sent') {
        result.sentFile = captured.record;

        // Content-Length ที่ Chrome ส่งออกไปจริง — หน้าเว็บแตะค่านี้ไม่ได้
        const upload = events.slice(firstEvent)
            .filter(e => e.method === 'Network.requestWillBeSent' && /\/Photos\/Upload$/i.test(e.params.request.url))
            .pop();
        if (upload) {
            const extra = events.slice(firstEvent).find(e =>
                e.method === 'Network.requestWillBeSentExtraInfo' && e.params.requestId === upload.params.requestId);
            const headers = extra ? extra.params.headers : upload.params.request.headers;
            const key = Object.keys(headers).find(k => k.toLowerCase() === 'content-length');
            result.contentLength = key ? Number(headers[key]) : null;
            result.requestUrl = upload.params.request.url;
        }
    } else {
        result.hint = captured.hint;
        result.requestSent = events.slice(firstEvent)
            .some(e => e.method === 'Network.requestWillBeSent' && /\/Photos\/Upload$/i.test(e.params.request.url));
    }

    result.ok = true;
} catch (err) {
    result.ok = false;
    result.error = String(err && err.message ? err.message : err);
} finally {
    try { ws.close(); } catch { }
    chrome.kill();
    await sleep(300);
    try { rmSync(profile, { recursive: true, force: true }); } catch { }
}

console.log('===RESULT===' + JSON.stringify(result));
process.exit(result.ok ? 0 : 1);
