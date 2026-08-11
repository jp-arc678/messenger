using System.Collections.Generic;
using Messenger.Domain.Entities;

namespace Messenger.Web.ViewModels
{
    /// <summary>หน้ารายการใบแจ้งงาน</summary>
    public class RequestListViewModel
    {
        public IReadOnlyList<DeliveryRequest> Requests { get; set; } = new List<DeliveryRequest>();

        /// <summary>ช่วงวันส่งที่ใช้กรอง (รูปแบบ ISO yyyy-MM-dd)</summary>
        public string SendDateFrom { get; set; }

        public string SendDateTo { get; set; }

        /// <summary>true = ผู้ใช้เห็นทั้งสาขา (Admin/Messenger), false = เห็นเฉพาะใบตัวเอง</summary>
        public bool SeesWholeBranch { get; set; }

        public string BranchCode { get; set; }

        public string BranchName { get; set; }

        public string Message { get; set; }
    }
}
