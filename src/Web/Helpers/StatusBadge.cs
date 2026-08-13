using System;
using Messenger.Domain.Enums;

namespace Messenger.Web
{
    /// <summary>
    /// สีของป้ายสถานะใบแจ้งงาน (D38)
    ///
    /// เดิมทุกสถานะใช้ <c>text-bg-secondary</c> เหมือนกันหมด ต้องอ่านตัวหนังสือทีละใบ
    /// ถึงจะรู้ว่าใบไหนกำลังส่ง ใบไหนพักอยู่ ซึ่งช้าเมื่อดูคิวงานทั้งวันรวดเดียว
    ///
    /// รวมไว้ที่เดียวเพื่อให้ทุกหน้า (รายการ / รายละเอียด / คิวงาน / รายงาน) ใช้สีชุดเดียวกัน
    /// สีล้อกับปุ่มการกระทำใน <see cref="ViewModels.StatusActionsViewModel.ButtonClass"/>
    /// เช่น ปุ่ม "ปิดงาน" เป็นเขียว สถานะ "เสร็จงานแล้ว" ก็เขียว
    /// </summary>
    public static class StatusBadge
    {
        /// <summary>คืนคลาส Bootstrap ของป้ายสถานะ เช่น <c>text-bg-success</c></summary>
        public static string CssClass(RequestStatus status)
        {
            switch (status)
            {
                case RequestStatus.Received:
                    // ยังไม่มีใครรับงาน — กลางๆ ไม่ต้องเรียกสายตา
                    return "text-bg-secondary";
                case RequestStatus.Delivering:
                    return "text-bg-primary";
                case RequestStatus.Paused:
                    // เหลือง = ต้องมีคนมาสะสาง ไม่ใช่ความผิดพลาด
                    return "text-bg-warning";
                case RequestStatus.Completed:
                    return "text-bg-success";
                case RequestStatus.Cancelled:
                    return "text-bg-danger";
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, "ไม่รู้จักสถานะนี้");
            }
        }
    }
}
