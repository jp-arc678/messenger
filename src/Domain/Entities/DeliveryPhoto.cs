using System;
using Messenger.Domain.Enums;

namespace Messenger.Domain.Entities
{
    /// <summary>
    /// รูปยืนยันของใบแจ้งงาน (BR-3)
    ///
    /// ตัวไฟล์อยู่บน filesystem — DB เก็บเฉพาะ path (ไม่เก็บ binary)
    /// <see cref="FilePath"/> เป็น path แบบสัมพัทธ์กับโฟลเดอร์รากที่ตั้งไว้ใน Web.config
    /// เพื่อให้ย้ายที่เก็บไฟล์ได้โดยไม่ต้องไล่แก้ข้อมูลใน DB
    /// </summary>
    public class DeliveryPhoto
    {
        public int PhotoId { get; set; }

        public int ReqId { get; set; }

        public PhotoType PhotoType { get; set; }

        public string FilePath { get; set; }

        /// <summary>ชื่อไฟล์ที่ผู้ใช้ส่งมา (เก็บไว้อ้างอิงเท่านั้น ไม่ได้ใช้เป็นชื่อจริงบนดิสก์)</summary>
        public string FileName { get; set; }

        public int? FileSizeBytes { get; set; }

        public DateTime CapturedAt { get; set; }

        public string CapturedBy { get; set; }

        public string CapturedByName { get; set; }

        public string PhotoTypeDisplayName => PhotoTypes.ToDisplayName(PhotoType);
    }
}
