using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Application.Abstractions;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.UnitTests.Fakes
{
    /// <summary>
    /// ที่เก็บใบแจ้งงานปลอมในหน่วยความจำ
    ///
    /// จำลองพฤติกรรมสำคัญ 3 อย่างของ DB จริง :
    /// - เลขลำดับแยกตาม (สาขา + YYMM) และเริ่มที่ 1 เสมอเมื่อขึ้นคู่ใหม่ (BR-8)
    /// - อ่าน/เขียนได้เฉพาะเมื่อ branchCode ตรง (BR-6)
    /// - rowVersion เปลี่ยนทุกครั้งที่บันทึกสำเร็จ (BR-2)
    /// </summary>
    public class FakeDeliveryRequestRepository : IDeliveryRequestRepository
    {
        private readonly Dictionary<int, DeliveryRequest> _requests = new Dictionary<int, DeliveryRequest>();
        private readonly Dictionary<string, int> _sequences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private int _nextReqId = 1;
        private long _rowVersionCounter = 1;

        public CreatedRequest Create(CreateRequestData data)
        {
            var sequenceKey = data.BranchCode + "|" + data.YyMm;

            int lastNumber;
            _sequences.TryGetValue(sequenceKey, out lastNumber);
            var running = lastNumber + 1;
            _sequences[sequenceKey] = running;

            var reqId = _nextReqId++;

            var request = new DeliveryRequest
            {
                ReqId = reqId,
                ReqNo = data.ReqNoFactory(running),
                BranchCode = data.BranchCode,
                BranchName = "สาขา " + data.BranchCode,
                RequesterEmpCode = data.RequesterEmpCode,
                RequesterName = "ผู้แจ้ง " + data.RequesterEmpCode,
                RequestDateTime = data.RequestDateTime,
                SendDate = data.SendDate,
                ContactName = data.ContactName,
                Address = data.Address,
                Phone = data.Phone,
                Detail = data.Detail,
                Status = RequestStatus.Received,
                IsPersonal = data.IsPersonal,
                CreatedBy = data.CreatedBy,
                CreatedAt = data.RequestDateTime,
                RowVersion = NextRowVersion(),
                JobTypes = CloneJobTypes(data.JobTypes, reqId)
            };

            _requests[reqId] = request;

            return new CreatedRequest { ReqId = reqId, ReqNo = request.ReqNo };
        }

        public DeliveryRequest GetById(int reqId, string branchCode)
        {
            DeliveryRequest request;
            if (!_requests.TryGetValue(reqId, out request))
                return null;

            // BR-6 — ใบงานต่างสาขาต้องมองไม่เห็นเลย
            if (!string.Equals(request.BranchCode, branchCode, StringComparison.OrdinalIgnoreCase))
                return null;

            return Clone(request);
        }

        public IReadOnlyList<DeliveryRequest> List(string branchCode, string requesterEmpCode,
                                                   DateTime? sendDateFrom, DateTime? sendDateTo)
        {
            return _requests.Values
                .Where(r => string.Equals(r.BranchCode, branchCode, StringComparison.OrdinalIgnoreCase))
                .Where(r => requesterEmpCode == null ||
                            string.Equals(r.RequesterEmpCode, requesterEmpCode, StringComparison.OrdinalIgnoreCase))
                .Where(r => !sendDateFrom.HasValue || r.SendDate >= sendDateFrom.Value)
                .Where(r => !sendDateTo.HasValue || r.SendDate <= sendDateTo.Value)
                .Select(Clone)
                .ToList();
        }

        public bool Update(UpdateRequestData data)
        {
            DeliveryRequest request;
            if (!_requests.TryGetValue(data.ReqId, out request))
                return false;

            if (!string.Equals(request.BranchCode, data.BranchCode, StringComparison.OrdinalIgnoreCase))
                return false;

            // BR-2 — rowVersion ไม่ตรง แปลว่ามีคนแก้ไปแล้ว
            if (!SameRowVersion(request.RowVersion, data.RowVersion))
                return false;

            request.RequesterEmpCode = data.RequesterEmpCode;
            request.SendDate = data.SendDate;
            request.ContactName = data.ContactName;
            request.Address = data.Address;
            request.Phone = data.Phone;
            request.Detail = data.Detail;
            request.IsPersonal = data.IsPersonal;
            request.UpdatedBy = data.UpdatedBy;
            request.UpdatedAt = DateTime.Now;
            request.RowVersion = NextRowVersion();
            request.JobTypes = CloneJobTypes(data.JobTypes, data.ReqId);

            return true;
        }

        // ---------------- ตัวช่วยสำหรับเทสต์ ----------------

        /// <summary>ตั้งสถานะของใบงานโดยตรง (ใช้จำลองว่า Messenger รับงานไปแล้ว)</summary>
        public void SetStatus(int reqId, RequestStatus status)
        {
            _requests[reqId].Status = status;
        }

        /// <summary>จำลองว่ามีผู้ใช้อื่นแก้ใบงานนี้ไปแล้ว (rowVersion เปลี่ยน)</summary>
        public void SimulateExternalEdit(int reqId)
        {
            _requests[reqId].RowVersion = NextRowVersion();
        }

        public DeliveryRequest Peek(int reqId)
        {
            DeliveryRequest request;
            return _requests.TryGetValue(reqId, out request) ? Clone(request) : null;
        }

        // ---------------- ภายใน ----------------

        private byte[] NextRowVersion()
        {
            return BitConverter.GetBytes(_rowVersionCounter++);
        }

        private static bool SameRowVersion(byte[] left, byte[] right)
        {
            if (left == null || right == null)
                return false;

            return left.SequenceEqual(right);
        }

        private static IList<RequestJobType> CloneJobTypes(IEnumerable<RequestJobType> source, int reqId)
        {
            if (source == null)
                return new List<RequestJobType>();

            return source
                .Select(j => new RequestJobType
                {
                    ReqId = reqId,
                    JobType = j.JobType,
                    DetailText = j.DetailText
                })
                .ToList();
        }

        private static DeliveryRequest Clone(DeliveryRequest source)
        {
            return new DeliveryRequest
            {
                ReqId = source.ReqId,
                ReqNo = source.ReqNo,
                BranchCode = source.BranchCode,
                BranchName = source.BranchName,
                RequesterEmpCode = source.RequesterEmpCode,
                RequesterName = source.RequesterName,
                RequestDateTime = source.RequestDateTime,
                SendDate = source.SendDate,
                ContactName = source.ContactName,
                Address = source.Address,
                Phone = source.Phone,
                Detail = source.Detail,
                Status = source.Status,
                IsPersonal = source.IsPersonal,
                ReceiptConfirmed = source.ReceiptConfirmed,
                RowVersion = source.RowVersion == null ? null : (byte[])source.RowVersion.Clone(),
                CreatedBy = source.CreatedBy,
                CreatedAt = source.CreatedAt,
                UpdatedBy = source.UpdatedBy,
                UpdatedAt = source.UpdatedAt,
                JobTypes = CloneJobTypes(source.JobTypes, source.ReqId)
            };
        }
    }
}
