using System;
using Messenger.Application.Abstractions;

namespace Messenger.Infrastructure.Data
{
    /// <summary>
    /// เวลาจริงของเครื่อง server
    ///
    /// ใช้ DateTime.Now (เวลาท้องถิ่น) ไม่ใช่ UtcNow เพราะกฎ BR-1 อ้างอิง
    /// "เวลาทำการ 10:00" ของสำนักงาน ซึ่งเป็นเวลาท้องถิ่นเสมอ
    /// </summary>
    public class SystemClock : IClock
    {
        public DateTime Now => DateTime.Now;

        public DateTime Today => DateTime.Now.Date;
    }
}
