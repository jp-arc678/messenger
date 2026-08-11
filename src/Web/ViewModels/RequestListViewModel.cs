using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.Web.ViewModels
{
    /// <summary>หน้ารายการใบแจ้งงาน</summary>
    public class RequestListViewModel
    {
        public IReadOnlyList<DeliveryRequest> Requests { get; set; } = new List<DeliveryRequest>();

        /// <summary>ช่วงวันส่งที่ใช้กรอง (รูปแบบ ISO yyyy-MM-dd)</summary>
        public string SendDateFrom { get; set; }

        public string SendDateTo { get; set; }

        /// <summary>ช่วง "วันที่บันทึก" ที่ใช้กรอง (รูปแบบ ISO yyyy-MM-dd)</summary>
        public string RequestDateFrom { get; set; }

        public string RequestDateTo { get; set; }

        /// <summary>ชื่อ enum ของสถานะที่กรอง — ว่าง = ทุกสถานะ</summary>
        public string Status { get; set; }

        /// <summary>ตัวเลือกสถานะทั้ง 5 + "ทุกสถานะ"</summary>
        public IEnumerable<SelectListItem> StatusOptions
        {
            get
            {
                var all = new List<SelectListItem>
                {
                    new SelectListItem { Value = string.Empty, Text = "ทุกสถานะ", Selected = string.IsNullOrEmpty(Status) }
                };

                all.AddRange(System.Enum.GetValues(typeof(RequestStatus))
                    .Cast<RequestStatus>()
                    .Select(s => new SelectListItem
                    {
                        Value = RequestStatuses.ToCode(s),
                        Text = RequestStatuses.ToDisplayName(s),
                        Selected = RequestStatuses.ToCode(s) == Status
                    }));

                return all;
            }
        }

        /// <summary>แปลงค่าที่มาจาก querystring เป็นสถานะ — คืน null ถ้าว่างหรือไม่รู้จัก</summary>
        public static RequestStatus? ParseStatus(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            RequestStatus parsed;
            if (!System.Enum.TryParse(text.Trim(), ignoreCase: true, result: out parsed) ||
                !System.Enum.IsDefined(typeof(RequestStatus), parsed))
            {
                return null;
            }

            return parsed;
        }

        /// <summary>true = ผู้ใช้เห็นทั้งสาขา (Admin/Messenger), false = เห็นเฉพาะใบตัวเอง</summary>
        public bool SeesWholeBranch { get; set; }

        public string BranchCode { get; set; }

        public string BranchName { get; set; }

        public string Message { get; set; }

        public string ErrorMessage { get; set; }
    }
}
