using System.Collections.Generic;
using Messenger.Domain.Workflow;

namespace Messenger.Web.ViewModels
{
    /// <summary>
    /// ชุดปุ่มเปลี่ยนสถานะของใบงาน 1 ใบ ใช้ร่วมกันระหว่างหน้ารายละเอียดและหน้าคิวงาน
    /// รายการปุ่มมาจาก service (§5 + §6) — view ไม่ตัดสินสิทธิ์เอง
    /// </summary>
    public class StatusActionsViewModel
    {
        public int ReqId { get; set; }

        public IReadOnlyList<StatusTransition> Actions { get; set; } = new List<StatusTransition>();

        /// <summary>"queue" = กลับไปหน้าคิวงานหลังกด · อย่างอื่น = กลับไปหน้ารายละเอียด</summary>
        public string ReturnTo { get; set; }

        /// <summary>วันที่ของคิวที่ต้องกลับไป (รูปแบบ ISO) — ใช้เมื่อ ReturnTo = queue</summary>
        public string QueueDate { get; set; }

        /// <summary>true = ปุ่มขนาดเล็ก (ใช้ในรายการคิว)</summary>
        public bool Compact { get; set; }

        /// <summary>
        /// โชว์ปุ่ม "ยืนยันรับของแล้ว" (BR-4) — ไม่ใช่ transition ใน §6
        /// แต่วางไว้ที่เดียวกันเพราะเป็นปุ่มที่ Messenger กดในจังหวะเดียวกัน
        /// </summary>
        public bool CanConfirmReceipt { get; set; }

        public string ButtonSizeClass => Compact ? " btn-sm" : string.Empty;

        /// <summary>สีปุ่มตามความหมายของการกระทำ</summary>
        public static string ButtonClass(RequestAction action)
        {
            switch (action)
            {
                case RequestAction.Confirm:
                    return "btn-primary";
                case RequestAction.Complete:
                    return "btn-success";
                case RequestAction.Pause:
                    return "btn-warning";
                case RequestAction.Resume:
                    return "btn-info";
                case RequestAction.Cancel:
                    return "btn-outline-danger";
                default:
                    return "btn-secondary";
            }
        }
    }
}
