using System;

namespace Messenger.Application.Services
{
    /// <summary>
    /// กฎวันที่ส่ง (BR-1) และเงื่อนไขวันที่ผู้ใช้เลือกเองได้ (D16)
    ///
    /// แยกออกมาเป็นคลาสเดี่ยวเพราะเป็นกฎที่ต้องมี unit test ครบทุกกรณี
    /// และถูกใช้จากหลายที่ (ตอนสร้าง, ตอนแก้, ตอนแสดงค่า default บนฟอร์ม)
    /// </summary>
    public static class SendDateCalculator
    {
        /// <summary>เวลาตัดรอบของ BR-1 ข้อ 2 — เกิน 10:00 แล้วเลื่อนเป็นวันถัดไป</summary>
        public static readonly TimeSpan CutoffTime = new TimeSpan(10, 0, 0);

        /// <summary>
        /// คำนวณ sendDate เริ่มต้นตาม BR-1
        ///
        /// 1. ตั้งต้น = วันที่บันทึก
        /// 2. ถ้าเวลาบันทึก "เกิน" 10:00 (เทียบแบบ &gt; ตาม D8) → เลื่อนเป็นวันถัดไป
        /// 3. ถ้าผลลัพธ์ตกเสาร์/อาทิตย์ → เลื่อนไปวันจันทร์
        ///
        /// กฎทั้งสามข้อ compose กันได้ เช่น ศุกร์ 11:00 → เสาร์ → จันทร์
        /// </summary>
        public static DateTime CalculateDefault(DateTime requestDateTime)
        {
            var sendDate = requestDateTime.Date;

            // ข้อ 2 — ใช้ > เท่านั้น : 10:00:00 ตรง ยังนับเป็นวันนี้ (D8)
            if (requestDateTime.TimeOfDay > CutoffTime)
                sendDate = sendDate.AddDays(1);

            // ข้อ 3 — เสาร์เลื่อน 2 วัน / อาทิตย์เลื่อน 1 วัน ได้วันจันทร์ทั้งคู่
            while (IsWeekend(sendDate))
                sendDate = sendDate.AddDays(1);

            return sendDate;
        }

        public static bool IsWeekend(DateTime date)
        {
            return date.DayOfWeek == DayOfWeek.Saturday
                || date.DayOfWeek == DayOfWeek.Sunday;
        }

        /// <summary>
        /// ตรวจ sendDate ที่ผู้ใช้เลือกเอง ตาม D16
        /// คืน null ถ้าผ่าน หรือคืนข้อความบอกเหตุผลถ้าไม่ผ่าน
        ///
        /// สังเกตว่าเมื่อผู้ใช้เลือกเอง ระบบ "ไม่เลื่อนวันให้อัตโนมัติ" แต่ปฏิเสธไปเลย
        /// กฎเลื่อนวันของ BR-1 ใช้เฉพาะตอนคำนวณค่า default เท่านั้น
        /// </summary>
        public static string ValidateUserPickedDate(DateTime sendDate, DateTime today)
        {
            if (sendDate.Date < today.Date)
                return "วันที่ส่งต้องไม่เป็นวันย้อนหลัง";

            if (IsWeekend(sendDate))
                return "วันที่ส่งต้องไม่ตรงกับวันเสาร์หรือวันอาทิตย์";

            return null;
        }
    }
}
