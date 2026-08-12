using System.Collections.Generic;
using Messenger.Domain.Entities;
using Messenger.Domain.Workflow;

namespace Messenger.Web.ViewModels
{
    /// <summary>หน้ารายละเอียดใบแจ้งงาน — ใบงาน + ปุ่มที่กดได้ + ประวัติสถานะ</summary>
    public class RequestDetailsViewModel
    {
        public DeliveryRequest Request { get; set; }

        /// <summary>แก้ไขเนื้อหาใบงานได้หรือไม่ (BR-2)</summary>
        public bool CanEdit { get; set; }

        /// <summary>ปุ่มเปลี่ยนสถานะที่ผู้ใช้คนนี้กดได้ ณ สถานะปัจจุบัน (§5 + §6)</summary>
        public IReadOnlyList<StatusTransition> Actions { get; set; } = new List<StatusTransition>();

        /// <summary>audit trail ทุกการเปลี่ยนสถานะ เรียงเก่า → ใหม่</summary>
        public IReadOnlyList<StatusHistoryEntry> History { get; set; } = new List<StatusHistoryEntry>();

        /// <summary>รูปยืนยันของใบงาน (BR-3)</summary>
        public IReadOnlyList<DeliveryPhoto> Photos { get; set; } = new List<DeliveryPhoto>();

        /// <summary>อัปโหลด/ลบรูปได้หรือไม่ (D23 + D24)</summary>
        public bool CanManagePhotos { get; set; }

        /// <summary>โชว์ปุ่ม "ยืนยันรับของแล้ว" หรือไม่ (BR-4)</summary>
        public bool CanConfirmReceipt { get; set; }

        public string Message { get; set; }

        public string ErrorMessage { get; set; }
    }
}
