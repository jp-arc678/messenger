using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Application.Abstractions;
using Messenger.Application.Dtos;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.Application.Services
{
    /// <summary>
    /// รายงานสรุปงาน (Phase 5)
    ///
    /// กฎที่บังคับในคลาสนี้ :
    /// - BR-6 ดึงข้อมูลด้วย branchCode ของผู้ใช้เท่านั้น
    /// - §5   Admin/Messenger สรุปทั้งสาขา · U-User สรุปเฉพาะใบที่ตัวเองเป็นผู้แจ้ง
    /// - D29  เลือกช่วงวันที่ได้ (อิงวันที่ส่ง) ค่าเริ่มต้นคือวันนี้วันเดียว
    ///
    /// การนับทั้งหมดทำในหน่วยความจำจากชุดข้อมูลเดียวที่ดึงมา เพื่อให้ตัวเลขสรุป
    /// กับรายการที่ export ออกไป มาจากชุดเดียวกันเสมอ (ไม่มีทางไม่ตรงกัน)
    /// </summary>
    public class ReportService : IReportService
    {
        /// <summary>กันการเผลอขอช่วงยาวเกินจนดึงข้อมูลทั้งระบบ</summary>
        public const int MaxRangeDays = 366;

        private readonly IDeliveryRequestRepository _requests;
        private readonly IClock _clock;

        public ReportService(IDeliveryRequestRepository requests, IClock clock)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public ServiceResult<DailyReport> GetReport(UserContext user, ReportQuery query)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            query = query ?? new ReportQuery();

            var from = (query.DateFrom ?? _clock.Today).Date;
            var to = (query.DateTo ?? from).Date;

            if (to < from)
                return ServiceResult<DailyReport>.Fail("วันที่สิ้นสุดต้องไม่มาก่อนวันที่เริ่มต้น");

            if ((to - from).TotalDays + 1 > MaxRangeDays)
                return ServiceResult<DailyReport>.Fail($"เลือกช่วงได้ไม่เกิน {MaxRangeDays} วัน");

            var wholeBranch = RequestAccess.SeesWholeBranch(user);

            var requests = _requests.List(new RequestListFilter
            {
                BranchCode = user.BranchCode,
                RequesterEmpCode = wholeBranch ? null : user.EmpCode,
                SendDateFrom = from,
                SendDateTo = to
            });

            var report = new DailyReport
            {
                DateFrom = from,
                DateTo = to,
                BranchCode = user.BranchCode,
                BranchName = user.BranchName,
                WholeBranch = wholeBranch,
                Requests = requests
                    .OrderBy(r => r.SendDate)
                    .ThenBy(r => r.SequenceOrder ?? int.MaxValue)
                    .ThenBy(r => r.ReqNo, StringComparer.Ordinal)
                    .ToList(),
                ByStatus = CountByStatus(requests),
                ByMessenger = CountByMessenger(requests),
                ByDay = CountByDay(requests, from, to)
            };

            return ServiceResult<DailyReport>.Ok(report);
        }

        // ---------------- ภายใน ----------------

        /// <summary>นับครบทั้ง 5 สถานะเสมอ แม้บางสถานะจะเป็นศูนย์ เพื่อให้หน้าจอไม่กระโดด</summary>
        private static IReadOnlyList<StatusCount> CountByStatus(IReadOnlyList<DeliveryRequest> requests)
        {
            return Enum.GetValues(typeof(RequestStatus))
                .Cast<RequestStatus>()
                .Select(status => new StatusCount
                {
                    Status = status,
                    Count = requests.Count(r => r.Status == status)
                })
                .ToList();
        }

        private static IReadOnlyList<MessengerSummary> CountByMessenger(IReadOnlyList<DeliveryRequest> requests)
        {
            return requests
                .Where(r => r.Assignment != null)
                .GroupBy(r => new { r.Assignment.MessengerEmpCode, r.Assignment.MessengerName })
                .Select(group => new MessengerSummary
                {
                    MessengerEmpCode = group.Key.MessengerEmpCode,
                    MessengerName = group.Key.MessengerName,
                    Total = group.Count(),
                    Completed = group.Count(r => r.Status == RequestStatus.Completed),
                    Cancelled = group.Count(r => r.Status == RequestStatus.Cancelled),
                    InProgress = group.Count(r => r.Status == RequestStatus.Delivering ||
                                                  r.Status == RequestStatus.Paused)
                })
                .OrderByDescending(summary => summary.Total)
                .ThenBy(summary => summary.MessengerEmpCode, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// ออกทุกวันในช่วงที่เลือก รวมวันที่ไม่มีงานด้วย (แสดงเป็น 0)
        /// เพื่อให้เห็นวันที่เงียบผิดปกติได้จากตารางเดียวกัน
        /// </summary>
        private static IReadOnlyList<DailySummary> CountByDay(IReadOnlyList<DeliveryRequest> requests,
                                                              DateTime from, DateTime to)
        {
            var days = new List<DailySummary>();

            for (var date = from; date <= to; date = date.AddDays(1))
            {
                var ofDay = requests.Where(r => r.SendDate.Date == date).ToList();

                days.Add(new DailySummary
                {
                    SendDate = date,
                    Total = ofDay.Count,
                    Received = ofDay.Count(r => r.Status == RequestStatus.Received),
                    Delivering = ofDay.Count(r => r.Status == RequestStatus.Delivering),
                    Paused = ofDay.Count(r => r.Status == RequestStatus.Paused),
                    Completed = ofDay.Count(r => r.Status == RequestStatus.Completed),
                    Cancelled = ofDay.Count(r => r.Status == RequestStatus.Cancelled)
                });
            }

            return days;
        }
    }
}
