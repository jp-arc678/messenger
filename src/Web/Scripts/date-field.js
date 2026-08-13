// ช่องกรอกวันที่ทั้งระบบ — บังคับให้แสดงและพิมพ์เป็น วว/ดด/ปปปป (D34)
//
// ทำไมต้องมีไฟล์นี้: <input type="date"> ให้เบราว์เซอร์เป็นคนเลือกรูปแบบที่วาดบนหน้าจอ
// ตามภาษาที่ตั้งไว้ในเบราว์เซอร์ เครื่องที่ตั้งเป็นอังกฤษ (US) จะได้ mm/dd/yyyy
// ซึ่งสั่งจากฝั่งเว็บไม่ได้เลย ไม่ว่าจะด้วย CSS หรือ attribute ใด ๆ
//
// วิธีแก้: ครอบช่องเดิมด้วยช่องข้อความที่เราวาดเอง (dd/MM/yyyy) แล้วเก็บ
// <input type="date"> ตัวจริงไว้ซ้อนแบบโปร่งใสใต้ปุ่มปฏิทิน — ช่องตัวจริงยังเป็นคน
// ถือ name/value ที่ส่งกลับ server เป็น ISO (yyyy-MM-dd) เหมือนเดิม ฝั่ง C# จึงไม่ต้องแก้
// และยังกดเรียก "ปฏิทินของเครื่อง" ได้ทั้งบน desktop และมือถือ
(function () {
    'use strict';

    var TEXT_PATTERN = /^(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{4})$/;
    var ISO_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

    var CALENDAR_ICON =
        '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">' +
        '<path d="M3.5 0a.5.5 0 0 1 .5.5V1h8V.5a.5.5 0 0 1 1 0V1h1a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2V3a2 2 0 0 1 2-2h1V.5a.5.5 0 0 1 .5-.5M1 4v10a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V4z"/>' +
        '</svg>';

    function pad(value) {
        return value < 10 ? '0' + value : '' + value;
    }

    /** ISO (yyyy-MM-dd) → ข้อความ วว/ดด/ปปปป · คืนค่าว่างถ้าอ่านไม่ออก */
    function isoToText(iso) {
        var parts = ISO_PATTERN.exec(iso || '');
        return parts ? parts[3] + '/' + parts[2] + '/' + parts[1] : '';
    }

    /** ข้อความที่ผู้ใช้พิมพ์ → ISO · คืนค่าว่างถ้าไม่ใช่วันที่ที่มีอยู่จริง */
    function textToIso(text) {
        var value = (text || '').trim();
        if (!value) {
            return '';
        }

        var day, month, year;
        var parts = TEXT_PATTERN.exec(value);

        if (parts) {
            day = +parts[1];
            month = +parts[2];
            year = +parts[3];
        } else if (/^\d{8}$/.test(value)) {
            // ยอมให้พิมพ์รวดเดียวโดยไม่ใส่ขีด เช่น 13082026
            day = +value.slice(0, 2);
            month = +value.slice(2, 4);
            year = +value.slice(4);
        } else {
            return '';
        }

        // ตรวจว่าวันที่มีอยู่จริง — Date() จะเลื่อน 31/02 ไปเป็น 03/03 ให้เงียบ ๆ ถ้าไม่ดัก
        var probe = new Date(year, month - 1, day);
        if (probe.getFullYear() !== year || probe.getMonth() !== month - 1 || probe.getDate() !== day) {
            return '';
        }

        return year + '-' + pad(month) + '-' + pad(day);
    }

    /** ใส่ / ให้อัตโนมัติระหว่างพิมพ์ — ทำเฉพาะตอนเคอร์เซอร์อยู่ท้ายสุด จะได้ไม่ไปแย่งตำแหน่งตอนแก้กลางข้อความ */
    function autoFormat(box) {
        if (box.selectionStart !== box.value.length) {
            return;
        }

        var digits = box.value.replace(/\D/g, '').slice(0, 8);
        var formatted = digits.slice(0, 2);

        if (digits.length > 2) {
            formatted += '/' + digits.slice(2, 4);
        }
        if (digits.length > 4) {
            formatted += '/' + digits.slice(4, 8);
        }

        if (formatted !== box.value) {
            box.value = formatted;
        }
    }

    function openNativePicker(native) {
        try {
            if (typeof native.showPicker === 'function') {
                native.showPicker();
                return;
            }
        } catch (err) {
            // เบราว์เซอร์ไม่อนุญาตให้เรียกตอนนี้ — ตกลงมาใช้ focus แทน
        }

        native.focus();
    }

    function upgrade(native) {
        if (native.getAttribute('data-date-field') === 'ready') {
            return;
        }
        native.setAttribute('data-date-field', 'ready');

        var wrapper = document.createElement('div');
        wrapper.className = 'date-field';

        var box = document.createElement('input');
        box.type = 'text';
        box.className = native.className;
        box.setAttribute('inputmode', 'numeric');
        box.placeholder = 'วว/ดด/ปปปป';
        box.maxLength = 10;
        box.autocomplete = 'off';
        box.value = isoToText(native.value);

        var picker = document.createElement('span');
        picker.className = 'date-field-picker';
        picker.setAttribute('role', 'button');
        picker.setAttribute('aria-label', 'เปิดปฏิทิน');
        picker.innerHTML = CALENDAR_ICON;

        // ป้ายกำกับเดิมชี้ที่ id ของช่องเก่า — ย้ายให้ชี้ช่องที่ผู้ใช้พิมพ์จริง
        if (native.id) {
            box.id = native.id + 'Text';

            var labels = document.querySelectorAll('label[for="' + native.id + '"]');
            Array.prototype.forEach.call(labels, function (label) {
                label.setAttribute('for', box.id);
            });
        }

        native.parentNode.insertBefore(wrapper, native);
        wrapper.appendChild(box);
        wrapper.appendChild(picker);
        picker.appendChild(native);

        native.className = '';
        native.tabIndex = -1;

        /** ยึดข้อความที่พิมพ์เป็นค่าจริง — ค่าที่อ่านไม่ออกจะถูกล้างแล้วขึ้นกรอบแดง */
        function commit() {
            var typed = box.value.trim();
            var iso = textToIso(typed);

            native.value = iso;

            if (typed && !iso) {
                box.classList.add('is-invalid');
                return;
            }

            box.classList.remove('is-invalid');
            if (iso) {
                box.value = isoToText(iso);
            }
        }

        box.addEventListener('input', function () {
            autoFormat(box);
            box.classList.remove('is-invalid');
        });
        box.addEventListener('change', commit);
        box.addEventListener('blur', commit);

        native.addEventListener('change', function () {
            box.value = isoToText(native.value);
            box.classList.remove('is-invalid');
        });

        picker.addEventListener('click', function () {
            openNativePicker(native);
        });

        // กด Enter ส่งฟอร์มเลยโดยไม่ผ่าน blur — ต้องยึดค่าก่อน ไม่งั้นค่าที่พิมพ์ค้างจะไม่ถูกส่ง
        if (native.form) {
            native.form.addEventListener('submit', commit);
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        var inputs = document.querySelectorAll('input[type="date"]');
        Array.prototype.forEach.call(inputs, upgrade);
    });
})();
