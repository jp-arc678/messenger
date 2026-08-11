using System;
using Messenger.Application.Abstractions;

namespace Messenger.UnitTests.Fakes
{
    /// <summary>นาฬิกาที่เทสต์กำหนดเวลาเองได้ — จำเป็นสำหรับทดสอบ BR-1</summary>
    public class FakeClock : IClock
    {
        public FakeClock(DateTime now)
        {
            Now = now;
        }

        public DateTime Now { get; set; }

        public DateTime Today => Now.Date;
    }
}
