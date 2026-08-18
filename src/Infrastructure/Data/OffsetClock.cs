using System;
using Messenger.Application.Abstractions;

namespace Messenger.Infrastructure.Data
{
    /// <summary>
    /// นาฬิกาที่เลื่อนเวลาจริงไปข้างหน้า/ข้างหลังตามจำนวนที่กำหนด — ใช้สำหรับทดสอบเท่านั้น
    ///
    /// ทำไมต้องมี : กฎ BR-1 ผูกกับเส้นแบ่ง 10:00 น. การทดสอบ end-to-end ว่า
    /// "แจ้งงานก่อน 10:00 ได้ sendDate = วันนี้" จึงทำได้แค่ช่วงเช้าของวันทำการเท่านั้น
    /// (UAT ข้อ 2.4) การเลื่อนนาฬิกาของ "ระบบ" แทนการเลื่อนนาฬิกาของ "เครื่อง"
    /// ทำให้ทดสอบเมื่อไรก็ได้โดยไม่ไปยุ่งกับ Windows หรือฐานข้อมูล
    ///
    /// ความปลอดภัย : ServiceRegistry ของโปรเจกต์ Web จะสร้างคลาสนี้ก็ต่อเมื่อ
    /// <c>&lt;compilation debug="true"&gt;</c> เท่านั้น — production ที่ตั้ง debug="false"
    /// จะใช้ <see cref="SystemClock"/> เสมอ ต่อให้เผลอทิ้งค่า config ไว้ก็ไม่มีผล
    ///
    /// ตั้งค่าผ่าน appSetting <c>ClockOffsetMinutes</c> เช่น -780 = ถอยหลัง 13 ชั่วโมง
    /// </summary>
    public class OffsetClock : IClock
    {
        private readonly TimeSpan _offset;

        public OffsetClock(TimeSpan offset)
        {
            _offset = offset;
        }

        public DateTime Now => DateTime.Now + _offset;

        public DateTime Today => Now.Date;
    }
}
