using System;

namespace Messenger.Application.Abstractions
{
    /// <summary>
    /// แหล่งเวลาปัจจุบันของระบบ
    ///
    /// BR-1 ผูกกับ "เวลาที่บันทึก" โดยตรง ถ้า service เรียก DateTime.Now เองจะทดสอบไม่ได้
    /// จึงต้องผ่าน interface นี้เสมอ เพื่อให้ unit test กำหนดเวลาได้
    /// </summary>
    public interface IClock
    {
        /// <summary>เวลาปัจจุบันตามเขตเวลาของเครื่อง server</summary>
        DateTime Now { get; }

        /// <summary>วันที่ปัจจุบัน (ตัดเวลาออก) — ใช้ตรวจว่า sendDate ย้อนหลังหรือไม่ (D16)</summary>
        DateTime Today { get; }
    }
}
