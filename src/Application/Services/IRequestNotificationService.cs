using System.Collections.Generic;
using Messenger.Domain.Entities;

namespace Messenger.Application.Services
{
    /// <summary>ผลของการพยายามส่งอีเมล 1 ครั้ง</summary>
    public class NotificationResult
    {
        /// <summary>ส่งออกไปแล้วจริง</summary>
        public bool Sent { get; set; }

        /// <summary>ที่อยู่ที่ส่งถึง (To + Cc) — ใช้ตรวจสอบและเขียน log</summary>
        public IReadOnlyList<string> Recipients { get; set; } = new List<string>();

        /// <summary>
        /// ข้อความบอกผู้ใช้เมื่อส่งไม่ออกหรือไม่มีใครให้ส่ง — null เมื่อทุกอย่างปกติ
        /// ข้อความนี้จะถูกแสดงเป็น "คำเตือน" ไม่ใช่ "ข้อผิดพลาด" ตาม D26
        /// </summary>
        public string Warning { get; set; }
    }

    /// <summary>
    /// การแจ้งเตือนของระบบ (BR-5)
    ///
    /// ตอนนี้มีอย่างเดียวคืออีเมลแจ้งผู้แจ้งเมื่อปิดงาน
    /// การส่งล้มเหลว "ต้องไม่" ทำให้การปิดงานล้มเหลวตาม D26
    /// จึงไม่มี method ไหนโยน exception ออกมา
    /// </summary>
    public interface IRequestNotificationService
    {
        NotificationResult NotifyCompleted(DeliveryRequest request);
    }
}
