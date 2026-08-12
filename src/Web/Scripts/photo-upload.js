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
                reject(new Error('เปิดไฟล์รูปไม่ได้'));
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

    function prepare(file) {
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

        prepare(file)
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
                if (!response.ok) {
                    throw new Error('อัปโหลดไม่สำเร็จ (' + response.status + ')');
                }
                // ฝั่ง server ทำ Post-Redirect-Get อยู่แล้ว โหลดหน้าใหม่เพื่อดูผลลัพธ์
                window.location.reload();
            })
            .catch(function (error) {
                setBusy(false, error.message + ' — ลองใหม่อีกครั้ง');
            });
    });
})();
