// ฟอร์มใบแจ้งงาน — เปิด/ปิดช่องรายละเอียดตามการติ๊กประเภทงาน (D18)
//
// ช่องรายละเอียดที่ยัง disabled จะไม่ถูกส่งกลับไปกับฟอร์ม ทำให้ประเภทงานที่
// ไม่ได้ติ๊กไม่มีข้อมูลค้างไปด้วย
(function () {
    'use strict';

    function syncDetailField(checkbox) {
        var targetId = checkbox.getAttribute('data-detail-target');
        if (!targetId) {
            return;
        }

        var detail = document.getElementById(targetId);
        if (!detail) {
            return;
        }

        detail.disabled = !checkbox.checked;

        if (!checkbox.checked) {
            detail.value = '';
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        var checkboxes = document.querySelectorAll('.js-jobtype');

        Array.prototype.forEach.call(checkboxes, function (checkbox) {
            syncDetailField(checkbox);

            checkbox.addEventListener('change', function () {
                syncDetailField(checkbox);
            });
        });
    });
})();
