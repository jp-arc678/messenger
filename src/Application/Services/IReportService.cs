using System;
using Messenger.Application.Dtos;

namespace Messenger.Application.Services
{
    /// <summary>ช่วงวันที่ของรายงาน (อิง "วันที่ส่ง")</summary>
    public class ReportQuery
    {
        public DateTime? DateFrom { get; set; }

        public DateTime? DateTo { get; set; }
    }

    /// <summary>
    /// รายงานสรุปงาน (Phase 5)
    ///
    /// ขอบเขตข้อมูลถูกจำกัดตาม §5 + BR-6 เสมอ :
    /// Admin/Messenger เห็นทั้งสาขาตัวเอง · User เห็นเฉพาะใบที่ตัวเองเป็นผู้แจ้ง
    /// </summary>
    public interface IReportService
    {
        ServiceResult<DailyReport> GetReport(UserContext user, ReportQuery query);
    }

    /// <summary>
    /// แปลงรายงานเป็นไฟล์สำหรับดาวน์โหลด (D30 — Excel .xlsx)
    /// อยู่คนละชั้นกับ service เพื่อไม่ให้ Application ผูกกับไลบรารีสร้างไฟล์
    /// </summary>
    public interface IReportExporter
    {
        byte[] Export(DailyReport report);

        string ContentType { get; }

        string BuildFileName(DailyReport report);
    }
}
