using System;
using System.Linq;
using Messenger.Application.Dtos;
using Messenger.Application.Services;
using Messenger.Domain.Enums;
using Messenger.Domain.Workflow;
using Messenger.Infrastructure.Export;
using Messenger.UnitTests.Fakes;
using NUnit.Framework;

namespace Messenger.UnitTests
{
    /// <summary>
    /// Phase 5 — รายงานสรุปงาน (§9) และกฎ D29–D31
    ///
    /// เวลาอ้างอิงคือ จันทร์ที่ 10 ส.ค. 2026 เวลา 09:00
    /// </summary>
    [TestFixture]
    public class ReportServiceTests
    {
        private static readonly DateTime MondayMorning = new DateTime(2026, 8, 10, 9, 0, 0);
        private static readonly DateTime Monday = new DateTime(2026, 8, 10);
        private static readonly DateTime Tuesday = new DateTime(2026, 8, 11);

        private FakeDeliveryRequestRepository _requests;
        private FakeEmployeeRepository _employees;
        private FakeClock _clock;
        private DeliveryRequestService _requestService;
        private RequestWorkflowService _workflow;
        private ReportService _reports;

        [SetUp]
        public void SetUp()
        {
            _requests = new FakeDeliveryRequestRepository();
            _employees = new FakeEmployeeRepository()
                .WithEmployee("10002", "SDC")
                .WithEmployee("10004", "SDC")
                .WithEmployee("10001", "SDC", "A")
                .WithEmployee("10003", "SDC", "M")
                .WithEmployee("20002", "SBK")
                .WithEmployee("20003", "SBK", "M");

            _clock = new FakeClock(MondayMorning);
            _requestService = new DeliveryRequestService(_requests, _employees, _clock);

            var notifications = new RequestNotificationService(
                new FakeEmailSender(), new FakeEmailTemplateSource(), _employees, _clock);
            _workflow = new RequestWorkflowService(_requests, _requests, _employees, notifications, _clock);

            _reports = new ReportService(_requests, _clock);
        }

        // ---------------- ตัวช่วย ----------------

        private static UserContext User(string empCode = "10002", string branchCode = "SDC",
                                        Role role = Role.User)
        {
            return new UserContext
            {
                EmpCode = empCode,
                FullName = "ผู้ใช้ " + empCode,
                BranchCode = branchCode,
                BranchName = "สาขา " + branchCode,
                Role = role
            };
        }

        private static UserContext Messenger(string branchCode = "SDC")
        {
            return User(branchCode == "SDC" ? "10003" : "20003", branchCode, Role.Messenger);
        }

        private static UserContext Admin() => User("10001", "SDC", Role.Admin);

        private int NewRequest(string requesterEmpCode = "10002", string branchCode = "SDC",
                               DateTime? sendDate = null, params JobType[] jobTypes)
        {
            var types = jobTypes.Length > 0 ? jobTypes : new[] { JobType.SendDoc };

            var result = _requestService.Create(new CreateRequestCommand
            {
                RequesterEmpCode = requesterEmpCode,
                SendDate = sendDate,
                ContactName = "บริษัท ตัวอย่าง จำกัด",
                Address = "123 ถนนทดสอบ",
                Detail = "ส่งเอกสารสัญญา",
                JobTypes = types.Select(t => new JobTypeInput { JobType = t }).ToList()
            }, User(requesterEmpCode, branchCode));

            Assert.That(result.Success, Is.True, result.FirstError);
            return result.Value.ReqId;
        }

        private int CompletedRequest(DateTime? sendDate = null)
        {
            var reqId = NewRequest(sendDate: sendDate);
            _workflow.Apply(reqId, RequestAction.Confirm, null, Messenger());
            var done = _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());
            Assert.That(done.Success, Is.True, done.FirstError);
            return reqId;
        }

        private DailyReport Report(UserContext user, DateTime? from = null, DateTime? to = null)
        {
            var result = _reports.GetReport(user, new ReportQuery { DateFrom = from, DateTo = to });
            Assert.That(result.Success, Is.True, result.FirstError);
            return result.Value;
        }

        // ==================== ช่วงวันที่ (D29) ====================

        [Test]
        public void ไม่ระบุช่วงวันที่ต้องได้รายงานของวันนี้()
        {
            NewRequest();                       // sendDate = วันนี้ (จันทร์)
            NewRequest(sendDate: Tuesday);

            var report = Report(Messenger());

            Assert.That(report.DateFrom, Is.EqualTo(Monday));
            Assert.That(report.DateTo, Is.EqualTo(Monday));
            Assert.That(report.TotalCount, Is.EqualTo(1));
        }

        [Test]
        public void เลือกช่วงหลายวันได้และมีแถวครบทุกวันแม้วันที่ไม่มีงาน()
        {
            NewRequest();
            NewRequest(sendDate: new DateTime(2026, 8, 12));

            var report = Report(Messenger(), Monday, new DateTime(2026, 8, 12));

            Assert.That(report.TotalCount, Is.EqualTo(2));
            Assert.That(report.ByDay.Count, Is.EqualTo(3), "ต้องมี 3 แถว: 10, 11, 12 ส.ค.");
            Assert.That(report.ByDay.Single(d => d.SendDate == Tuesday).Total, Is.EqualTo(0));
        }

        [Test]
        public void ช่วงวันที่กลับหัวต้องไม่ผ่าน()
        {
            var result = _reports.GetReport(Messenger(),
                new ReportQuery { DateFrom = Tuesday, DateTo = Monday });

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ไม่มาก่อน"));
        }

        [Test]
        public void ช่วงยาวเกินกำหนดต้องไม่ผ่าน()
        {
            var result = _reports.GetReport(Messenger(),
                new ReportQuery { DateFrom = Monday, DateTo = Monday.AddDays(ReportService.MaxRangeDays) });

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ไม่เกิน"));
        }

        // ==================== ขอบเขตข้อมูล (§5 + BR-6) ====================

        [Test]
        public void Admin_และ_Messenger_เห็นรายงานทั้งสาขา()
        {
            NewRequest("10002");
            NewRequest("10004");

            Assert.That(Report(Admin()).TotalCount, Is.EqualTo(2));
            Assert.That(Report(Admin()).WholeBranch, Is.True);
            Assert.That(Report(Messenger()).TotalCount, Is.EqualTo(2));
        }

        [Test]
        public void User_เห็นเฉพาะใบที่ตัวเองเป็นผู้แจ้ง()
        {
            NewRequest("10002");
            NewRequest("10004");

            var report = Report(User("10002"));

            Assert.That(report.WholeBranch, Is.False);
            Assert.That(report.TotalCount, Is.EqualTo(1));
            Assert.That(report.Requests.Single().RequesterEmpCode, Is.EqualTo("10002"));
        }

        [Test]
        public void รายงานไม่ข้ามสาขา()
        {
            NewRequest("10002", "SDC");
            NewRequest("20002", "SBK");

            Assert.That(Report(Messenger("SDC")).TotalCount, Is.EqualTo(1));
            Assert.That(Report(Messenger("SBK")).TotalCount, Is.EqualTo(1));
            Assert.That(Report(Messenger("SDC")).Requests.All(r => r.BranchCode == "SDC"), Is.True);
        }

        // ==================== ตัวเลขสรุป ====================

        [Test]
        public void นับครบทั้งห้าสถานะแม้บางสถานะเป็นศูนย์()
        {
            NewRequest();
            CompletedRequest();

            var report = Report(Messenger());

            Assert.That(report.ByStatus.Count, Is.EqualTo(5));
            Assert.That(report.CountOf(RequestStatus.Received), Is.EqualTo(1));
            Assert.That(report.CountOf(RequestStatus.Completed), Is.EqualTo(1));
            Assert.That(report.CountOf(RequestStatus.Cancelled), Is.EqualTo(0));
        }

        [Test]
        public void สรุปต่อ_Messenger_นับเฉพาะใบที่ยืนยันรับงานแล้ว()
        {
            NewRequest();                 // ยังไม่ยืนยัน — ไม่ควรถูกนับ
            CompletedRequest();

            var pausedId = NewRequest();
            _workflow.Apply(pausedId, RequestAction.Confirm, null, Messenger());
            _workflow.Apply(pausedId, RequestAction.Pause, "ฝนตก", Messenger());

            var report = Report(Messenger());
            var summary = report.ByMessenger.Single();

            Assert.That(summary.MessengerEmpCode, Is.EqualTo("10003"));
            Assert.That(summary.Total, Is.EqualTo(2));
            Assert.That(summary.Completed, Is.EqualTo(1));
            Assert.That(summary.InProgress, Is.EqualTo(1), "พักการส่งยังนับว่ากำลังวิ่ง");
            Assert.That(summary.Cancelled, Is.EqualTo(0));
        }

        [Test]
        public void แยกงานส่วนตัวกับงานบริษัท()
        {
            NewRequest();

            var personal = new CreateRequestCommand
            {
                ContactName = "ร้านซักรีด",
                Address = "หน้าปากซอย",
                Detail = "ฝากส่งของส่วนตัว",
                IsPersonal = true,
                JobTypes = new[] { new JobTypeInput { JobType = JobType.Other } }
            };
            _requestService.Create(personal, User("10002"));

            var report = Report(Messenger());

            Assert.That(report.PersonalCount, Is.EqualTo(1));
            Assert.That(report.CompanyCount, Is.EqualTo(1));
        }

        [Test]
        public void ตัวเลขสรุปกับรายการที่_export_ต้องมาจากชุดเดียวกัน()
        {
            CompletedRequest();
            NewRequest();

            var report = Report(Messenger());

            Assert.That(report.Requests.Count, Is.EqualTo(report.TotalCount));
            Assert.That(report.ByStatus.Sum(s => s.Count), Is.EqualTo(report.TotalCount));
            Assert.That(report.ByDay.Sum(d => d.Total), Is.EqualTo(report.TotalCount));
        }

        // ==================== ไฟล์ export (D30 + D31) ====================

        [Test]
        public void สร้างไฟล์_Excel_ที่เปิดอ่านกลับได้()
        {
            CompletedRequest();
            var report = Report(Messenger());
            var exporter = new ExcelReportExporter();

            var bytes = exporter.Export(report);

            Assert.That(bytes.Length, Is.GreaterThan(0));

            // เปิดไฟล์กลับด้วย ClosedXML เพื่อยืนยันว่าเป็น xlsx ที่ใช้งานได้จริง
            using (var stream = new System.IO.MemoryStream(bytes))
            using (var workbook = new ClosedXML.Excel.XLWorkbook(stream))
            {
                Assert.That(workbook.Worksheets.Count, Is.EqualTo(2));
                Assert.That(workbook.Worksheet("รายการใบงาน"), Is.Not.Null);
                Assert.That(workbook.Worksheet("สรุป"), Is.Not.Null);

                var detail = workbook.Worksheet("รายการใบงาน");
                Assert.That(detail.Cell(4, 1).GetString(), Is.EqualTo("เลขใบงาน"));
                Assert.That(detail.Cell(5, 1).GetString(), Is.EqualTo(report.Requests.Single().ReqNo));
                Assert.That(detail.Cell(5, 6).GetString(), Is.EqualTo("เสร็จงานแล้ว"));
                Assert.That(detail.Cell(5, 9).GetString(), Is.EqualTo("พนักงาน 10003"));
            }
        }

        [Test]
        public void ชื่อไฟล์บอกสาขาและช่วงวันที่()
        {
            var exporter = new ExcelReportExporter();

            var oneDay = exporter.BuildFileName(Report(Messenger()));
            var range = exporter.BuildFileName(Report(Messenger(), Monday, Tuesday));

            Assert.That(oneDay, Is.EqualTo("Messenger-Report-SDC-20260810.xlsx"));
            Assert.That(range, Is.EqualTo("Messenger-Report-SDC-20260810-20260811.xlsx"));
        }

        [Test]
        public void export_ช่วงที่ไม่มีงานก็ยังได้ไฟล์ที่เปิดได้()
        {
            var report = Report(Messenger(), new DateTime(2026, 9, 1), new DateTime(2026, 9, 2));
            Assert.That(report.TotalCount, Is.EqualTo(0));

            var bytes = new ExcelReportExporter().Export(report);

            using (var stream = new System.IO.MemoryStream(bytes))
            using (var workbook = new ClosedXML.Excel.XLWorkbook(stream))
            {
                Assert.That(workbook.Worksheets.Count, Is.EqualTo(2));
            }
        }
    }
}
