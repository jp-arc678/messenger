/*
    mobile-layout-probe.mjs — ตรวจหน้าจอขนาดมือถือด้วย Chrome device emulation (UAT 8.1–8.3)

    ⚠ นี่คือ "เครื่องจำลอง" ไม่ใช่เครื่องจริง — ตรวจได้เฉพาะเรื่องที่ขึ้นกับ layout engine
    (ความกว้างที่ล้น · ตารางที่เลื่อนในกรอบตัวเอง · เมนู hamburger · ขนาด/การซ้อนของปุ่ม)
    เรื่องที่ขึ้นกับ OS ของเครื่อง เช่น การเปิดกล้องหลัง (8.4) หรือปฏิทินของเครื่อง (8.5)
    จำลองแบบนี้ไม่ได้ ต้องใช้มือถือจริงเท่านั้น

    วิธีวัด :
      8.1  document.scrollingElement.scrollWidth ต้องไม่เกินความกว้างจอ
           และไล่ดูทุก element ว่ามีตัวไหนยื่นพ้นขอบขวาโดย "ไม่ได้อยู่ในกรอบที่เลื่อนได้" ไหม
      8.2  กดปุ่ม hamburger จริง ๆ แล้วนับรายการเมนูที่กางออก + ยิง elementFromPoint
           ที่กลางปุ่มแต่ละอันเพื่อยืนยันว่ากดโดนตัวมันเอง ไม่มีอะไรบังอยู่
      8.3  วัดกรอบของปุ่มดำเนินการทุกปุ่ม เทียบกันทีละคู่ว่าทับกันไหม + ขนาดพอให้นิ้วกดไหม

    ผลลัพธ์พิมพ์เป็น JSON บรรทัดเดียวขึ้นต้นด้วย ===RESULT===
*/
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [baseUrl, empCode, pathList, queuePath] = process.argv.slice(2);
if (!baseUrl || !empCode || !pathList) {
    console.error('ใช้: node mobile-layout-probe.mjs <baseUrl> <empCode> <path1,path2,...> [queuePath]');
    process.exit(2);
}
const paths = pathList.split(',').map(p => p.trim()).filter(Boolean);

// iPhone 14 / Pixel 7 อยู่ในย่านนี้ — 390×844 คือค่าที่เกณฑ์ UAT 8.1 ระบุไว้
const VIEWPORT = { width: 390, height: 844, deviceScaleFactor: 3, mobile: true };
const UA = 'Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) ' +
           'Chrome/151.0.0.0 Mobile Safari/537.36';

const CHROME = [
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe'
].find(existsSync);
if (!CHROME) { console.error('ไม่พบ Chrome หรือ Edge บนเครื่องนี้'); process.exit(2); }

const PORT = 9600 + (process.pid % 200);
const profile = mkdtempSync(join(tmpdir(), 'uat8-'));
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

const chrome = spawn(CHROME, [
    '--headless=new',
    '--remote-debugging-port=' + PORT,
    '--user-data-dir=' + profile,
    '--no-first-run', '--no-default-browser-check', '--disable-gpu',
    '--disable-extensions', '--disable-background-networking',
    'about:blank'
], { stdio: 'ignore' });

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

const json = async (expression) => JSON.parse(await evaluate(expression));

async function waitFor(fn, what, timeoutMs = 60000) {
    const until = Date.now() + timeoutMs;
    while (Date.now() < until) {
        const v = await fn();
        if (v) return v;
        await sleep(150);
    }
    throw new Error('หมดเวลารอ: ' + what);
}

async function navigate(url) {
    const before = events.length;
    await send('Page.navigate', { url });
    await waitFor(async () => events.slice(before).some(e => e.method === 'Page.loadEventFired'), 'โหลดหน้า ' + url);
    await sleep(250);   // เผื่อ Bootstrap/สคริปต์ในหน้าจัด layout เสร็จ
}

/* ---------------------------------------------------------------- ชุดตรวจที่ฉีดเข้าไปในหน้า */

// 8.1 — ความกว้างล้นจอ + ตารางต้องเลื่อนในกรอบของตัวเอง
const LAYOUT_AUDIT = `(function () {
    var vw = window.innerWidth;
    var out = {
        url: location.pathname + location.search,
        title: (document.querySelector('h1, h2') || {}).textContent || document.title,
        innerWidth: vw,
        pageScrollWidth: document.scrollingElement.scrollWidth,
        overflowing: [],
        tables: []
    };

    function describe(el) {
        var cls = typeof el.className === 'string' ? el.className : '';
        return el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + (cls ? '.' + cls.trim().split(/\\s+/).slice(0, 2).join('.') : '');
    }

    // element ที่ยื่นพ้นขอบขวา "โดยไม่ได้อยู่ในกรอบที่เลื่อนได้" คือสาเหตุที่ทำให้ทั้งหน้าเลื่อนซ้าย-ขวา
    var all = document.querySelectorAll('body *');
    for (var i = 0; i < all.length; i++) {
        var el = all[i];
        var st = getComputedStyle(el);
        if (st.display === 'none' || st.visibility === 'hidden' || st.position === 'fixed') continue;
        var r = el.getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue;
        if (r.right <= vw + 1 && r.left >= -1) continue;

        var p = el.parentElement, inScroller = false;
        while (p) {
            var ps = getComputedStyle(p);
            if ((ps.overflowX === 'auto' || ps.overflowX === 'scroll' || ps.overflow === 'auto' || ps.overflow === 'hidden')
                && p.scrollWidth > p.clientWidth + 1) { inScroller = true; break; }
            p = p.parentElement;
        }
        if (inScroller) continue;

        if (out.overflowing.length < 8) {
            out.overflowing.push({ el: describe(el), left: Math.round(r.left), right: Math.round(r.right) });
        }
    }

    var tables = document.querySelectorAll('table');
    for (var t = 0; t < tables.length; t++) {
        var table = tables[t], scroller = null, node = table.parentElement;
        while (node) {
            var ns = getComputedStyle(node);
            if (ns.overflowX === 'auto' || ns.overflowX === 'scroll') { scroller = node; break; }
            node = node.parentElement;
        }
        var tw = Math.round(table.getBoundingClientRect().width);
        out.tables.push({
            table: describe(table),
            width: tw,
            hasScrollContainer: !!scroller,
            container: scroller ? describe(scroller) : null,
            containerClient: scroller ? scroller.clientWidth : null,
            containerScroll: scroller ? scroller.scrollWidth : null,
            needsScroll: scroller ? scroller.scrollWidth > scroller.clientWidth + 1 : tw > vw + 1
        });
    }

    return JSON.stringify(out);
})()`;

/*
    หมายเหตุ : ทุกที่ที่เลื่อนจอต้องระบุ behavior:'instant'
    Bootstrap reboot ตั้ง scroll-behavior:smooth ไว้ที่ :root การเลื่อนจึงเป็นแอนิเมชัน
    ถ้าไม่สั่ง instant การวัดกรอบทันทีหลังเลื่อนจะได้ค่าเก่า แล้ว elementFromPoint
    จะคืน null ให้ทุกปุ่มที่อยู่นอกจอ — ดูเหมือน "กดไม่โดน" ทั้งที่หน้าเว็บปกติดี
*/

// 8.2 — เมนู hamburger
const MENU_OPEN = `(function () {
    var toggler = document.querySelector('.navbar-toggler');
    if (!toggler) return JSON.stringify({ hasToggler: false });
    var r = toggler.getBoundingClientRect();
    var collapse = document.querySelector('#mainNav');
    var out = {
        hasToggler: true,
        togglerVisible: r.width > 0 && r.height > 0 && getComputedStyle(toggler).display !== 'none',
        togglerSize: [Math.round(r.width), Math.round(r.height)],
        collapsedBefore: !!collapse && !collapse.classList.contains('show')
    };
    toggler.click();
    return JSON.stringify(out);
})()`;

const MENU_READ = `(function () {
    var collapse = document.querySelector('#mainNav');
    if (!collapse) return JSON.stringify({ shown: false, items: [] });
    var nodes = collapse.querySelectorAll('a.nav-link, button[type="submit"]');
    var items = [];
    for (var i = 0; i < nodes.length; i++) {
        var a = nodes[i];
        a.scrollIntoView({ block: 'center', behavior: 'instant' });
        var r = a.getBoundingClientRect();
        var hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
        items.push({
            text: (a.textContent || '').trim(),
            w: Math.round(r.width), h: Math.round(r.height),
            visible: r.width > 0 && r.height > 0,
            hitsSelf: !!hit && (hit === a || a.contains(hit) || a.contains(hit.parentElement))
        });
    }
    window.scrollTo({ top: 0, behavior: 'instant' });
    return JSON.stringify({ shown: collapse.classList.contains('show'), items: items });
})()`;

// 8.3 — ปุ่มดำเนินการ : ขนาดพอให้นิ้วกด · ไม่ทับกัน · กดแล้วโดนตัวเอง
const BUTTONS_AUDIT = `(function () {
    var scope = document.querySelector('main') || document.body;
    var nodes = scope.querySelectorAll('button, a.btn, input[type="submit"]');
    var items = [];
    for (var i = 0; i < nodes.length; i++) {
        var b = nodes[i];
        var st = getComputedStyle(b);
        if (st.display === 'none' || st.visibility === 'hidden') continue;
        b.scrollIntoView({ block: 'center', behavior: 'instant' });
        var r = b.getBoundingClientRect();
        if (r.width === 0 || r.height === 0) continue;
        var cx = r.left + r.width / 2, cy = r.top + r.height / 2;
        var hit = document.elementFromPoint(cx, cy);
        items.push({
            text: (b.textContent || b.value || '').trim().slice(0, 24),
            w: Math.round(r.width), h: Math.round(r.height),
            x: Math.round(r.left + window.scrollX), y: Math.round(r.top + window.scrollY),
            disabled: !!b.disabled,
            hitsSelf: !!hit && (hit === b || b.contains(hit) || b.contains(hit.parentElement))
        });
    }
    window.scrollTo({ top: 0, behavior: 'instant' });

    var overlaps = [];
    for (var a = 0; a < items.length; a++) {
        for (var c = a + 1; c < items.length; c++) {
            var p = items[a], q = items[c];
            var ox = Math.min(p.x + p.w, q.x + q.w) - Math.max(p.x, q.x);
            var oy = Math.min(p.y + p.h, q.y + q.h) - Math.max(p.y, q.y);
            if (ox > 1 && oy > 1) overlaps.push([p.text, q.text]);
        }
    }

    return JSON.stringify({
        count: items.length,
        buttons: items,
        overlaps: overlaps,
        disabledCount: items.filter(function (b) { return b.disabled; }).length,
        tooSmall: items.filter(function (b) { return Math.min(b.w, b.h) < 24; }).map(function (b) { return b.text + ' (' + b.w + 'x' + b.h + ')'; }),
        // ต่ำกว่า 44px คือต่ำกว่าที่ Apple/Google แนะนำ — ยังกดได้ แต่ควรรู้ไว้
        underThumb: items.filter(function (b) { return Math.min(b.w, b.h) < 44; }).map(function (b) { return b.text + ' (' + b.w + 'x' + b.h + ')'; }),
        // ปุ่มที่ disabled ไม่รับ pointer event เป็นเรื่องปกติ (Chrome คืน element แม่ให้แทน)
        // จึงต้องไม่นับเป็น "กดไม่โดน" ไม่งั้นแถวแรก/แถวสุดท้ายของคิวจะฟ้องทุกครั้ง
        notHit: items.filter(function (b) { return !b.hitsSelf && !b.disabled; }).map(function (b) { return b.text; })
    });
})()`;

/* ---------------------------------------------------------------- ลำดับการตรวจ */

const result = { chrome: CHROME, viewport: VIEWPORT, pages: [] };
try {
    await send('Page.enable');
    await send('Runtime.enable');
    await send('Emulation.setDeviceMetricsOverride', VIEWPORT);
    await send('Emulation.setUserAgentOverride', { userAgent: UA });
    await send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 });
    await send('Emulation.setEmitTouchEventsForMouse', { enabled: true, configuration: 'mobile' });

    // หน้า login ต้องดูดีบนมือถือด้วย จึงตรวจก่อนเข้าสู่ระบบ
    await navigate(baseUrl + '/Account/Login');
    result.pages.push(await json(LAYOUT_AUDIT));

    await evaluate(
        "document.getElementById('EmpCode').value = " + JSON.stringify(empCode) + ";" +
        "document.getElementById('EmpCode').form.submit(); true;");
    await waitFor(async () => (await evaluate('location.pathname')) !== '/Account/Login', 'เข้าสู่ระบบ');
    await sleep(300);

    for (const path of paths) {
        await navigate(baseUrl + path);
        const audit = await json(LAYOUT_AUDIT);
        audit.path = path;
        result.pages.push(audit);
    }

    // 8.2 — กดเมนูจากหน้าแรกของรายการ
    await navigate(baseUrl + paths[0]);
    result.menu = await json(MENU_OPEN);
    await sleep(700);                       // รอ transition ของ Bootstrap collapse
    Object.assign(result.menu, await json(MENU_READ));

    // 8.3 — ปุ่มบนหน้าคิวงาน
    if (queuePath) {
        await navigate(baseUrl + queuePath);
        result.buttons = await json(BUTTONS_AUDIT);
        result.buttons.path = queuePath;
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
