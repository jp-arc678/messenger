using System.Collections.Generic;

namespace Messenger.Application.Abstractions
{
    /// <summary>อีเมล 1 ฉบับที่พร้อมส่ง</summary>
    public class EmailMessage
    {
        public IReadOnlyList<string> To { get; set; } = new List<string>();

        public IReadOnlyList<string> Cc { get; set; } = new List<string>();

        public string Subject { get; set; }

        /// <summary>เนื้อความเป็น HTML</summary>
        public string HtmlBody { get; set; }
    }

    /// <summary>
    /// ช่องทางส่งอีเมล (BR-5)
    ///
    /// D5 — ยังไม่ได้ SMTP จริงของบริษัท ระบบจึงเขียนไฟล์ .eml ลงโฟลเดอร์ตอน dev
    /// และเปลี่ยนเป็น SMTP จริงได้ด้วยการแก้ config อย่างเดียว (D28)
    ///
    /// implementation ต้อง "โยน exception เมื่อส่งไม่สำเร็จ" — ผู้เรียกเป็นคน
    /// ตัดสินว่าจะให้ความล้มเหลวนั้นมีผลกับ business flow แค่ไหน (D26)
    /// </summary>
    public interface IEmailSender
    {
        void Send(EmailMessage message);
    }

    /// <summary>
    /// ที่มาของ template อีเมล — คืน null ถ้าไม่มีไฟล์ override
    /// (ระบบจะใช้ template ที่ฝังมากับโค้ดแทน)
    /// </summary>
    public interface IEmailTemplateSource
    {
        string TryRead(string templateName);
    }
}
