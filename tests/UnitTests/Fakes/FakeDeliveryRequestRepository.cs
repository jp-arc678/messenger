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
    /// จำลองพฤติกรรมสำคัญของ DB จริง :
    /// - เลขลำดับใบงานแยกตาม (สาขา + YYMM) และเริ่มที่ 1 เสมอเมื่อขึ้นคู่ใหม่ (BR-8)
    /// - อ่าน/เขียนได้เฉพาะเมื่อ branchCode ตรง (BR-6)
    /// - rowVersion เปลี่ยนทุกครั้งที่บันทึกสำเร็จ (BR-2)
    /// - เปลี่ยนสถานะได้เฉพาะเมื่อสถานะปัจจุบันยังตรงกับที่ผู้เรียกเห็น (§6 + กันกดพร้อมกัน)
    /// - ลำดับวิ่งงานเดินต่อภายใน (สาขา + วันที่ส่ง) เดียวกัน (D11)
    /// </summary>
    public class FakeDeliveryRequestRepository : IDeliveryRequestRepository, IRequestWorkflowRepository
    {
        private readonly Dictionary<int, DeliveryRequest> _requests = new Dictionary<int, DeliveryRequest>();
        private readonly Dictionary<string, int> _sequences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<StatusHistoryEntry> _history = new List<StatusHistoryEntry>();

        private int _nextReqId = 1;
        private long _nextHistoryId = 1;
        private long _rowVersionCounter = 1;

        // ---------------- IDeliveryRequestRepository ----------------

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

                // ของจริงมาจาก join กับ tblEmployee ใน vwDeliveryRequest
                // ที่นี่สร้างให้เป็นรูปแบบเดียวกันเสมอ เพื่อให้เทสต์ BR-5 มีที่อยู่ให้ส่ง
                RequesterEmail = data.RequesterEmpCode + "@example.co.th",
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

            AddHistory(reqId, null, RequestStatus.Received, data.CreatedBy, data.RequestDateTime, "สร้างใบแจ้งงาน");

            return new CreatedRequest { ReqId = reqId, ReqNo = request.ReqNo };
        }

        public DeliveryRequest GetById(int reqId, string branchCode)
        {
            var request = Find(reqId, branchCode);
            return request == null ? null : Clone(request);
        }

        public IReadOnlyList<DeliveryRequest> List(RequestListFilter filter)
        {
            return _requests.Values
                .Where(r => SameText(r.BranchCode, filter.BranchCode))
                .Where(r => filter.RequesterEmpCode == null ||
                            SameText(r.RequesterEmpCode, filter.RequesterEmpCode))
                .Where(r => !filter.SendDateFrom.HasValue || r.SendDate >= filter.SendDateFrom.Value)
                .Where(r => !filter.SendDateTo.HasValue || r.SendDate <= filter.SendDateTo.Value)
                .Where(r => !filter.RequestDateFrom.HasValue || r.RequestDateTime >= filter.RequestDateFrom.Value.Date)
                .Where(r => !filter.RequestDateTo.HasValue ||
                            r.RequestDateTime < filter.RequestDateTo.Value.Date.AddDays(1))
                .Where(r => !filter.Status.HasValue || r.Status == filter.Status.Value)
                .Select(Clone)
                .ToList();
        }

        public bool Update(UpdateRequestData data)
        {
            var request = Find(data.ReqId, data.BranchCode);
            if (request == null)
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

        // ---------------- IRequestWorkflowRepository ----------------

        public bool ChangeStatus(StatusChangeData data)
        {
            var request = Find(data.ReqId, data.BranchCode);
            if (request == null)
                return false;

            // สถานะปัจจุบันต้องตรงกับที่ service เห็นตอนตัดสินใจ ไม่งั้นคือมีคนกดตัดหน้า
            if (request.Status != data.FromStatus)
                return false;

            request.Status = data.ToStatus;
            request.RowVersion = NextRowVersion();

            AddHistory(data.ReqId, data.FromStatus, data.ToStatus, data.ByEmpCode, data.ChangedAt, data.Note);

            return true;
        }

        public MessengerAssignment Confirm(ConfirmAssignmentData data)
        {
            var request = Find(data.ReqId, data.BranchCode);
            if (request == null)
                return null;

            if (request.Status != RequestStatus.Received)
                return null;

            // D11 — ลำดับถัดไปของ (สาขา + วันที่ส่ง) เดียวกัน
            var lastOrder = _requests.Values
                .Where(r => SameText(r.BranchCode, data.BranchCode))
                .Where(r => r.SendDate.Date == request.SendDate.Date)
                .Where(r => r.Assignment != null)
                .Select(r => r.Assignment.SequenceOrder)
                .DefaultIfEmpty(0)
                .Max();

            request.Assignment = new MessengerAssignment
            {
                ReqId = data.ReqId,
                MessengerEmpCode = data.MessengerEmpCode,
                MessengerName = "พนักงาน " + data.MessengerEmpCode,
                ConfirmedAt = data.ConfirmedAt,
                SequenceOrder = lastOrder + 1
            };

            request.Status = RequestStatus.Delivering;
            request.RowVersion = NextRowVersion();

            AddHistory(data.ReqId, RequestStatus.Received, RequestStatus.Delivering,
                       data.ByEmpCode, data.ConfirmedAt, data.Note);

            return CloneAssignment(request.Assignment);
        }

        public bool SwapSequence(int reqIdA, int reqIdB, string branchCode)
        {
            var a = Find(reqIdA, branchCode);
            var b = Find(reqIdB, branchCode);

            if (a?.Assignment == null || b?.Assignment == null)
                return false;

            // ลำดับมีความหมายเฉพาะภายในวันเดียวกัน (D11)
            if (a.SendDate.Date != b.SendDate.Date)
                return false;

            var temp = a.Assignment.SequenceOrder;
            a.Assignment.SequenceOrder = b.Assignment.SequenceOrder;
            b.Assignment.SequenceOrder = temp;

            return true;
        }

        public bool ConfirmReceipt(ReceiptConfirmData data)
        {
            var request = Find(data.ReqId, data.BranchCode);
            if (request == null)
                return false;

            // BR-4 — กดซ้ำไม่ได้ คนแรกที่กดคือคนที่ถูกบันทึกไว้
            if (request.ReceiptConfirmed)
                return false;

            request.ReceiptConfirmed = true;
            request.ReceiptConfirmedAt = data.ConfirmedAt;
            request.ReceiptConfirmedBy = data.ByEmpCode;
            request.RowVersion = NextRowVersion();

            return true;
        }

        public IReadOnlyList<StatusHistoryEntry> ListHistory(int reqId, string branchCode)
        {
            if (Find(reqId, branchCode) == null)
                return new List<StatusHistoryEntry>();

            return _history
                .Where(h => h.ReqId == reqId)
                .OrderBy(h => h.ChangedAt)
                .ThenBy(h => h.HistoryId)
                .ToList();
        }

        // ---------------- ตัวช่วยสำหรับเทสต์ ----------------

        /// <summary>ตั้งสถานะของใบงานโดยตรง (ใช้จำลองว่ามีคนกดตัดหน้าไปแล้ว)</summary>
        public void SetStatus(int reqId, RequestStatus status)
        {
            _requests[reqId].Status = status;
        }

        /// <summary>ตั้ง/ล้างอีเมลผู้แจ้งของใบงาน (ใช้ทดสอบกรณีไม่มีอีเมลให้ส่ง — BR-5)</summary>
        public void SetRequesterEmail(int reqId, string email)
        {
            _requests[reqId].RequesterEmail = email;
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

        private DeliveryRequest Find(int reqId, string branchCode)
        {
            DeliveryRequest request;
            if (!_requests.TryGetValue(reqId, out request))
                return null;

            // BR-6 — ใบงานต่างสาขาต้องมองไม่เห็นเลย
            return SameText(request.BranchCode, branchCode) ? request : null;
        }

        private void AddHistory(int reqId, RequestStatus? from, RequestStatus to,
                                string byEmpCode, DateTime changedAt, string note)
        {
            _history.Add(new StatusHistoryEntry
            {
                HistoryId = _nextHistoryId++,
                ReqId = reqId,
                FromStatus = from,
                ToStatus = to,
                ByEmpCode = byEmpCode,
                ByName = "พนักงาน " + byEmpCode,
                ChangedAt = changedAt,
                Note = note
            });
        }

        private byte[] NextRowVersion()
        {
            return BitConverter.GetBytes(_rowVersionCounter++);
        }

        private static bool SameText(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
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

        private static MessengerAssignment CloneAssignment(MessengerAssignment source)
        {
            if (source == null)
                return null;

            return new MessengerAssignment
            {
                ReqId = source.ReqId,
                MessengerEmpCode = source.MessengerEmpCode,
                MessengerName = source.MessengerName,
                ConfirmedAt = source.ConfirmedAt,
                SequenceOrder = source.SequenceOrder,
                Route = source.Route,
                DistanceKm = source.DistanceKm,
                ReturnToOffice = source.ReturnToOffice
            };
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
                RequesterEmail = source.RequesterEmail,
                RequestDateTime = source.RequestDateTime,
                SendDate = source.SendDate,
                ContactName = source.ContactName,
                Address = source.Address,
                Phone = source.Phone,
                Detail = source.Detail,
                Status = source.Status,
                IsPersonal = source.IsPersonal,
                ReceiptConfirmed = source.ReceiptConfirmed,
                ReceiptConfirmedAt = source.ReceiptConfirmedAt,
                ReceiptConfirmedBy = source.ReceiptConfirmedBy,
                RowVersion = source.RowVersion == null ? null : (byte[])source.RowVersion.Clone(),
                CreatedBy = source.CreatedBy,
                CreatedAt = source.CreatedAt,
                UpdatedBy = source.UpdatedBy,
                UpdatedAt = source.UpdatedAt,
                JobTypes = CloneJobTypes(source.JobTypes, source.ReqId),
                Assignment = CloneAssignment(source.Assignment)
            };
        }
    }
}
