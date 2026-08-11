using System;
using System.Collections.Generic;
using Messenger.Application.Dtos;
using Messenger.Domain.Entities;
using Messenger.Domain.Workflow;

namespace Messenger.Web.ViewModels
{
    /// <summary>ใบงาน 1 แถวในคิว พร้อมปุ่มที่ผู้ใช้คนนี้กดได้จริง</summary>
    public class QueueRowViewModel
    {
        public DeliveryRequest Request { get; set; }

        /// <summary>ปุ่มเปลี่ยนสถานะที่ผ่านการตรวจสิทธิ์จาก service แล้ว (§5 + §6)</summary>
        public IReadOnlyList<StatusTransition> Actions { get; set; } = new List<StatusTransition>();

        public bool CanMoveUp { get; set; }

        public bool CanMoveDown { get; set; }
    }

    /// <summary>หน้าคิวงานของสาขาในวันหนึ่ง (Phase 2)</summary>
    public class QueueViewModel
    {
        public QueueDay Day { get; set; }

        /// <summary>วันที่ของคิว รูปแบบ ISO สำหรับช่อง input type=date</summary>
        public string DateText { get; set; }

        public string PreviousDateText { get; set; }

        public string NextDateText { get; set; }

        /// <summary>รอยืนยันรับงาน (Received)</summary>
        public IReadOnlyList<QueueRowViewModel> Pending { get; set; } = new List<QueueRowViewModel>();

        /// <summary>กำลังวิ่งงาน เรียงตามลำดับของวัน (Delivering/Paused)</summary>
        public IReadOnlyList<QueueRowViewModel> Running { get; set; } = new List<QueueRowViewModel>();

        /// <summary>ปิดแล้วในวันนั้น (Completed/Cancelled)</summary>
        public IReadOnlyList<QueueRowViewModel> Closed { get; set; } = new List<QueueRowViewModel>();

        public string Message { get; set; }

        public string ErrorMessage { get; set; }

        public static string FormatDate(DateTime date)
        {
            return RequestFormViewModel.FormatDate(date);
        }
    }
}
