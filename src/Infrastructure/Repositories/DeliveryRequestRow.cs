using System;
using System.Collections.Generic;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.Infrastructure.Repositories
{
    /// <summary>รูปร่างของแถวที่ได้จาก view dbo.vwDeliveryRequest</summary>
    internal class DeliveryRequestRow
    {
        public int ReqId { get; set; }
        public string ReqNo { get; set; }
        public string BranchCode { get; set; }
        public string BranchName { get; set; }
        public string RequesterEmpCode { get; set; }
        public string RequesterName { get; set; }
        public string RequesterDeptCode { get; set; }
        public string RequesterUnitName { get; set; }
        public string RequesterPhoneExt { get; set; }
        public string RequesterEmail { get; set; }
        public DateTime RequestDateTime { get; set; }
        public DateTime SendDate { get; set; }
        public string ContactName { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string Detail { get; set; }
        public string Status { get; set; }
        public bool IsPersonal { get; set; }
        public bool ReceiptConfirmed { get; set; }
        public byte[] RowVersion { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public string UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // ---- ส่วนที่มาจาก LEFT JOIN tblMessengerAssignment (null ถ้ายังไม่ยืนยันรับงาน) ----
        public string MessengerEmpCode { get; set; }
        public string MessengerName { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public int? SequenceOrder { get; set; }
        public string Route { get; set; }
        public decimal? DistanceKm { get; set; }
        public bool? ReturnToOffice { get; set; }

        public DeliveryRequest ToEntity()
        {
            return new DeliveryRequest
            {
                ReqId = ReqId,
                ReqNo = ReqNo,
                // BranchCode เป็น CHAR(3) จึงถูก pad ช่องว่างมาจาก DB
                BranchCode = BranchCode?.Trim(),
                BranchName = BranchName,
                RequesterEmpCode = RequesterEmpCode,
                RequesterName = RequesterName,
                RequesterDeptCode = RequesterDeptCode,
                RequesterUnitName = RequesterUnitName,
                RequesterPhoneExt = RequesterPhoneExt,
                RequesterEmail = RequesterEmail,
                RequestDateTime = RequestDateTime,
                SendDate = SendDate,
                ContactName = ContactName,
                Address = Address,
                Phone = Phone,
                Detail = Detail,
                Status = RequestStatuses.Parse(Status),
                IsPersonal = IsPersonal,
                ReceiptConfirmed = ReceiptConfirmed,
                RowVersion = RowVersion,
                CreatedBy = CreatedBy,
                CreatedAt = CreatedAt,
                UpdatedBy = UpdatedBy,
                UpdatedAt = UpdatedAt,
                JobTypes = new List<RequestJobType>(),
                Assignment = ToAssignment()
            };
        }

        /// <summary>คืน null เมื่อยังไม่มีใครยืนยันรับงาน (คอลัมน์จาก LEFT JOIN เป็น NULL ทั้งชุด)</summary>
        private MessengerAssignment ToAssignment()
        {
            if (string.IsNullOrWhiteSpace(MessengerEmpCode) || !SequenceOrder.HasValue)
                return null;

            return new MessengerAssignment
            {
                ReqId = ReqId,
                MessengerEmpCode = MessengerEmpCode,
                MessengerName = MessengerName,
                ConfirmedAt = ConfirmedAt ?? default(DateTime),
                SequenceOrder = SequenceOrder.Value,
                Route = Route,
                DistanceKm = DistanceKm,
                ReturnToOffice = ReturnToOffice
            };
        }
    }

    /// <summary>รูปร่างของแถวจาก dbo.spStatusHistoryListByReq</summary>
    internal class StatusHistoryRow
    {
        public long HistoryId { get; set; }
        public int ReqId { get; set; }
        public string FromStatus { get; set; }
        public string ToStatus { get; set; }
        public string ByEmpCode { get; set; }
        public string ByName { get; set; }
        public DateTime ChangedAt { get; set; }
        public string Note { get; set; }

        public StatusHistoryEntry ToEntity()
        {
            return new StatusHistoryEntry
            {
                HistoryId = HistoryId,
                ReqId = ReqId,

                // FromStatus เป็น NULL เฉพาะบรรทัดแรกสุด (ตอนสร้างใบงาน)
                FromStatus = string.IsNullOrWhiteSpace(FromStatus)
                    ? (RequestStatus?)null
                    : RequestStatuses.Parse(FromStatus),

                ToStatus = RequestStatuses.Parse(ToStatus),
                ByEmpCode = ByEmpCode,
                ByName = ByName,
                ChangedAt = ChangedAt,
                Note = Note
            };
        }
    }

    /// <summary>รูปร่างของแถวจากตาราง tblRequestJobType</summary>
    internal class RequestJobTypeRow
    {
        public int ReqJobTypeId { get; set; }
        public int ReqId { get; set; }
        public string JobType { get; set; }
        public string DetailText { get; set; }

        public RequestJobType ToEntity()
        {
            return new RequestJobType
            {
                ReqJobTypeId = ReqJobTypeId,
                ReqId = ReqId,
                JobType = Domain.Enums.JobTypes.Parse(JobType),
                DetailText = DetailText
            };
        }
    }
}
