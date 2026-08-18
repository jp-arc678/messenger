// ย่อรูปฝั่ง client ให้ไม่เกิน 2 MB ก่อนอัปโหลด (BR-3)
//
// ทำไมต้องย่อฝั่ง client : รูปจากกล้องมือถือทุกวันนี้ใบละ 3-8 MB การส่งขึ้นไป
// ทั้งก้อนบนเน็ตมือถือช้ามากและมักติด limit ของ IIS ก่อนถึง server ด้วยซ้ำ
//
// ฝั่ง server ยังตรวจขนาดและชนิดไฟล์ซ้ำเสมอ (PhotoService) เพราะสคริปต์นี้
// ถูกข้ามได้ง่าย ๆ ด้วยการยิง request ตรง — ที่นี่คือเรื่องประสบการณ์ใช้งาน
// ไม่ใช่เรื่องความปลอดภัย
(function () {
    'use strict';

    var MAX_DIMENSION = 1600;   // ด้านยาวสุดหลังย่อ — พอสำหรับอ่านชื่อ/ลายเซ็นบนเอกสาร
    var MIN_QUALITY = 0.4;
    var QUALITY_STEP = 0.15;

    var form = document.getElementById('photoUploadForm');
    if (!form) {
        return;
    }

    var fileInput = document.getElementById('photoFile');
    var button = document.getElementById('photoUploadButton');
    var hint = document.getElementById('photoUploadHint');
    var maxBytes = parseInt(form.getAttribute('data-max-bytes'), 10) || 2097152;

    // เบราว์เซอร์เก่าที่ไม่มี canvas.toBlob / fetch ให้ส่งฟอร์มแบบปกติไปเลย
    // (ไฟล์ใหญ่จะโดน server ปฏิเสธพร้อมข้อความบอกเหตุผล ไม่ได้พังเงียบ ๆ)
    if (!fileInput || !window.FormData || !window.fetch ||
        !window.HTMLCanvasElement || !HTMLCanvasElement.prototype.toBlob) {
        return;
    }

    /**
     * ข้อผิดพลาดที่ลองไฟล์เดิมซ้ำอีกกี่ครั้งก็ไม่มีทางผ่าน (เช่น เลือกไฟล์ที่ไม่ใช่รูป)
     * แยกจากข้อผิดพลาดชั่วคราวอย่างเน็ตหลุด เพื่อจะได้ไม่ต่อท้ายว่า "ลองใหม่อีกครั้ง"
     * ซึ่งทำให้ผู้ใช้นั่งกดซ้ำกับไฟล์เดิมโดยไม่รู้ว่าต้องเปลี่ยนไฟล์
     */
    function PermanentError(message) {
        this.name = 'PermanentError';
        this.message = message;
        this.permanent = true;
    }
    PermanentError.prototype = Object.create(Error.prototype);
    PermanentError.prototype.constructor = PermanentError;

    function setBusy(busy, message) {
        if (button) {
            button.disabled = busy;
            button.textContent = busy ? 'กำลังอัปโหลด…' : 'อัปโหลดรูป';
        }
        if (hint && message) {
            hint.textContent = message;
        }
    }

    function loadImage(file) {
        return new Promise(function (resolve, reject) {
            var url = URL.createObjectURL(file);
            var image = new Image();

            image.onload = function () {
                URL.revokeObjectURL(url);
                resolve(image);
            };
            image.onerror = function () {
                URL.revokeObjectURL(url);
                // ไฟล์ที่เปิดเป็นรูปไม่ได้ = ไม่ใช่รูปจริง (เช่น .pdf ที่เปลี่ยนนามสกุลเป็น .jpg)
                // หรือไฟล์เสียหาย — ทั้งสองกรณีลองไฟล์เดิมซ้ำก็ไม่มีทางผ่าน จึงต้องบอกให้เปลี่ยนไฟล์
                reject(new PermanentError(
                    'ไฟล์นี้ไม่ใช่รูปภาพ หรือไฟล์เสียหาย — กรุณาเลือกไฟล์รูป (.jpg หรือ .png)'));
            };

            image.src = url;
        });
    }

    function drawScaled(image) {
        var scale = Math.min(1, MAX_DIMENSION / Math.max(image.width, image.height));
        var canvas = document.createElement('canvas');

        canvas.width = Math.round(image.width * scale);
        canvas.height = Math.round(image.height * scale);

        var context = canvas.getContext('2d');
        context.drawImage(image, 0, 0, canvas.width, canvas.height);

        return canvas;
    }

    function toBlob(canvas, quality) {
        return new Promise(function (resolve) {
            canvas.toBlob(function (blob) { resolve(blob); }, 'image/jpeg', quality);
        });
    }

    // ลดคุณภาพลงทีละขั้นจนกว่าจะเข้าเกณฑ์ — วิธีนี้ได้รูปที่ยังอ่านออก
    // ดีกว่าการย่อขนาดภาพลงเรื่อย ๆ จนตัวหนังสือหาย
    function compress(canvas) {
        function attempt(quality) {
            return toBlob(canvas, quality).then(function (blob) {
                if (!blob || blob.size <= maxBytes || quality <= MIN_QUALITY) {
                    return blob;
                }
                return attempt(quality - QUALITY_STEP);
            });
        }

        return attempt(0.85);
    }

    /**
     * ปฏิเสธไฟล์ที่ไม่ใช่รูปตั้งแต่ก่อนส่ง (UAT-01)
     *
     * เดิมไฟล์ผิดชนิดจะถูกส่งขึ้นไปให้ server ปฏิเสธ ซึ่งใช้ได้กับไฟล์เล็ก
     * แต่ถ้าไฟล์ใหญ่เกิน maxRequestLength ด้วย ASP.NET จะตัด request ทิ้ง
     * ก่อนถึงโค้ดตรวจไฟล์ ผู้ใช้เลยได้หน้า error เปล่า ๆ แทนคำอธิบาย
     * ดักตรงนี้ทำให้รู้ผลทันทีโดยไม่ต้องรออัปโหลดไฟล์ใหญ่จนจบก่อนค่อยพัง
     */
    function assertLooksLikeImage(file) {
        var name = (file.name || '').toLowerCase();
        var byExtension = /\.(jpe?g|png)$/.test(name);
        // บางเบราว์เซอร์/บางไฟล์ไม่ส่ง type มาให้ ตกลงไปใช้นามสกุลแทน
        var byType = file.type === 'image/jpeg' || file.type === 'image/png';

        if (byType || (!file.type && byExtension)) {
            return;
        }

        throw new PermanentError(
            'ระบบรับเฉพาะไฟล์รูป JPG และ PNG — กรุณาเลือกไฟล์รูปแล้วลองใหม่');
    }

    function prepare(file) {
        assertLooksLikeImage(file);

        // ไฟล์เล็กอยู่แล้วก็ส่งของเดิมไป ไม่ต้องแปลงให้เสียคุณภาพฟรี ๆ
        if (file.size <= maxBytes) {
            return Promise.resolve(file);
        }

        return loadImage(file)
            .then(drawScaled)
            .then(compress)
            .then(function (blob) {
                if (!blob) {
                    return file;
                }
                return new File([blob], 'photo.jpg', { type: 'image/jpeg' });
            });
    }

    form.addEventListener('submit', function (event) {
        var file = fileInput.files && fileInput.files[0];
        if (!file) {
            return; // ปล่อยให้ server ตอบว่ายังไม่ได้เลือกไฟล์
        }

        event.preventDefault();
        setBusy(true, 'กำลังย่อรูป…');

        // prepare() โยน error แบบ synchronous ได้ (assertLooksLikeImage)
        // ห่อด้วย Promise.resolve().then เพื่อให้ทุกกรณีไหลลง .catch เส้นเดียวกัน
        Promise.resolve()
            .then(function () { return prepare(file); })
            .then(function (prepared) {
                var data = new FormData(form);
                data.set('file', prepared, prepared.name || 'photo.jpg');

                return fetch(form.action, {
                    method: 'POST',
                    body: data,
                    credentials: 'same-origin'
                });
            })
            .then(function (response) {
                // 413 = ไฟล์ทะลุ maxRequestLength ตั้งแต่ชั้น ASP.NET (ดู Global.asax)
                // ลองไฟล์เดิมซ้ำก็ไม่มีทางผ่าน จึงต้องบอกให้เปลี่ยนไฟล์
                if (response.status === 413) {
                    throw new PermanentError(
                        'ไฟล์ใหญ่เกินกว่าที่ระบบรับได้ — กรุณาเลือกไฟล์รูป JPG หรือ PNG ที่เล็กลง');
                }
                if (!response.ok) {
                    throw new Error('อัปโหลดไม่สำเร็จ (' + response.status + ')');
                }
                // ฝั่ง server ทำ Post-Redirect-Get อยู่แล้ว โหลดหน้าใหม่เพื่อดูผลลัพธ์
                window.location.reload();
            })
            .catch(function (error) {
                setBusy(false, error.permanent
                    ? error.message
                    : error.message + ' — ลองใหม่อีกครั้ง');
            });
    });
})();
