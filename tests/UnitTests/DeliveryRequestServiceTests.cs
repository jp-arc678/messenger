using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Application.Dtos;
using Messenger.Application.Services;
using Messenger.Domain.Enums;
using Messenger.UnitTests.Fakes;
using NUnit.Framework;

namespace Messenger.UnitTests
{
    /// <summary>
    /// Phase 1 — กฎของใบแจ้งงาน : BR-1, BR-2, BR-6, BR-8 และ D15–D18
    ///
    /// เวลาอ้างอิงของเทสต์ชุดนี้คือ จันทร์ที่ 10 ส.ค. 2026 เวลา 09:00
    /// (ก่อนเวลาตัดรอบ 10:00 เพื่อให้ค่า default ของ sendDate = วันเดียวกัน)
    /// </summary>
    [TestFixture]
    public class DeliveryRequestServiceTests
    {
        private static readonly DateTime MondayMorning = new DateTime(2026, 8, 10, 9, 0, 0);

        private FakeDeliveryRequestRepository _requests;
        private FakeEmployeeRepository _employees;
        private FakeClock _clock;
        private DeliveryRequestService _service;

        [SetUp]
        public void SetUp()
        {
            _requests = new FakeDeliveryRequestRepository();
            _employees = new FakeEmployeeRepository()
                .WithEmployee("10002", "SDC")
                .WithEmployee("10004", "SDC")
                .WithEmployee("10001", "SDC", "A")
                .WithEmployee("20002", "SBK");
            _clock = new FakeClock(MondayMorning);
            _service = new DeliveryRequestService(_requests, _employees, _clock);
        }

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

        private static CreateRequestCommand ValidCommand()
        {
            return new CreateRequestCommand
            {
                ContactName = "บริษัท ตัวอย่าง จำกัด",
                Address = "123 ถนนทดสอบ",
                Detail = "ส่งเอกสารสัญญา",
                Phone = "021234567",
                JobTypes = new List<JobTypeInput>
                {
                    new JobTypeInput { JobType = JobType.SendDoc, DetailText = "ซองน้ำตาล 1 ซอง" }
                }
            };
        }

        // ==================== BR-8 : เลขใบงาน ====================

        [Test]
        public void สร้างใบแรกของเดือน_ได้เลขลงท้าย0001()
        {
            var result = _service.Create(ValidCommand(), User());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.ReqNo, Is.EqualTo("MSG-SDC-2608-0001"));
        }

        [Test]
        public void สร้างใบที่สองของสาขาเดิม_เลขต้องเดินต่อ()
        {
            _service.Create(ValidCommand(), User());
            var second = _service.Create(ValidCommand(), User());

            Assert.That(second.Value.ReqNo, Is.EqualTo("MSG-SDC-2608-0002"));
        }

        [Test]
        public void แต่ละสาขานับเลขแยกกัน()
        {
            // BR-8 — ลำดับแยกตามสาขา ใบแรกของ SBK ต้องเป็น 0001 แม้ SDC จะมีใบแล้ว
            _service.Create(ValidCommand(), User("10002", "SDC"));
            _service.Create(ValidCommand(), User("10002", "SDC"));

            var sbk = _service.Create(ValidCommand(), User("20002", "SBK"));

            Assert.That(sbk.Value.ReqNo, Is.EqualTo("MSG-SBK-2608-0001"));
        }

        [Test]
        public void ขึ้นเดือนใหม่แล้วเลขต้องเริ่มนับหนึ่งใหม่()
        {
            _service.Create(ValidCommand(), User());

            // ขยับนาฬิกาไปเดือนกันยายน (อังคารที่ 1 ก.ย. 2026)
            _clock.Now = new DateTime(2026, 9, 1, 9, 0, 0);
            var september = _service.Create(ValidCommand(), User());

            Assert.That(september.Value.ReqNo, Is.EqualTo("MSG-SDC-2609-0001"));
        }

        // ==================== BR-1 : วันที่ส่ง ====================

        [Test]
        public void ไม่ระบุวันส่ง_ระบบคำนวณให้ตาม_BR1()
        {
            var result = _service.Create(ValidCommand(), User());

            Assert.That(result.Value.SendDate, Is.EqualTo(new DateTime(2026, 8, 10)));
        }

        [Test]
        public void ไม่ระบุวันส่งและบันทึกหลัง10โมงวันศุกร์_ได้วันจันทร์()
        {
            _clock.Now = new DateTime(2026, 8, 14, 11, 0, 0); // ศุกร์ 11:00

            var result = _service.Create(ValidCommand(), User());

            Assert.That(result.Value.SendDate, Is.EqualTo(new DateTime(2026, 8, 17)));
        }

        [Test]
        public void วันส่งเริ่มต้นบนฟอร์มต้องตรงกับกฎ_BR1()
        {
            _clock.Now = new DateTime(2026, 8, 14, 11, 0, 0);

            Assert.That(_service.GetDefaultSendDate(), Is.EqualTo(new DateTime(2026, 8, 17)));
        }

        // ==================== D16 : วันที่ผู้ใช้เลือกเอง ====================

        [Test]
        public void ผู้ใช้เลือกวันส่งย้อนหลัง_ต้องไม่สำเร็จ()
        {
            var command = ValidCommand();
            command.SendDate = new DateTime(2026, 8, 7);

            var result = _service.Create(command, User());

            Assert.That(result.Success, Is.False);
            Assert.That(result.Errors.Any(e => e.Contains("ย้อนหลัง")), Is.True);
        }

        [Test]
        public void ผู้ใช้เลือกวันเสาร์_ต้องไม่สำเร็จและต้องไม่ถูกเลื่อนให้()
        {
            var command = ValidCommand();
            command.SendDate = new DateTime(2026, 8, 15);

            var result = _service.Create(command, User());

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void ผู้ใช้เลือกวันทำการในอนาคต_สำเร็จ()
        {
            var command = ValidCommand();
            command.SendDate = new DateTime(2026, 8, 17);

            var result = _service.Create(command, User());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.SendDate, Is.EqualTo(new DateTime(2026, 8, 17)));
        }

        // ==================== D15 / D18 : ข้อมูลบังคับ ====================

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void ไม่กรอกชื่อผู้ติดต่อ_ต้องไม่สำเร็จ(string contactName)
        {
            var command = ValidCommand();
            command.ContactName = contactName;

            Assert.That(_service.Create(command, User()).Success, Is.False);
        }

        [TestCase(null)]
        [TestCase("  ")]
        public void ไม่กรอกที่อยู่_ต้องไม่สำเร็จ(string address)
        {
            var command = ValidCommand();
            command.Address = address;

            Assert.That(_service.Create(command, User()).Success, Is.False);
        }

        [TestCase(null)]
        [TestCase("  ")]
        public void ไม่กรอกรายละเอียดงาน_ต้องไม่สำเร็จ(string detail)
        {
            var command = ValidCommand();
            command.Detail = detail;

            Assert.That(_service.Create(command, User()).Success, Is.False);
        }

        [Test]
        public void ไม่กรอกเบอร์โทร_ยังสำเร็จได้()
        {
            // D15 — phone เป็นฟิลด์ที่ไม่บังคับ
            var command = ValidCommand();
            command.Phone = null;

            Assert.That(_service.Create(command, User()).Success, Is.True);
        }

        [Test]
        public void ไม่เลือกประเภทงานเลย_ต้องไม่สำเร็จ()
        {
            var command = ValidCommand();
            command.JobTypes = new List<JobTypeInput>();

            Assert.That(_service.Create(command, User()).Success, Is.False);
        }

        [Test]
        public void เลือกได้หลายประเภทพร้อมรายละเอียดแยกกัน()
        {
            var command = ValidCommand();
            command.JobTypes = new List<JobTypeInput>
            {
                new JobTypeInput { JobType = JobType.SendDoc, DetailText = "เอกสารสัญญา" },
                new JobTypeInput { JobType = JobType.ReceiveCheck, DetailText = "เช็ค 2 ใบ" },
                new JobTypeInput { JobType = JobType.Other, DetailText = "แวะซื้อของ" }
            };

            var result = _service.Create(command, User());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.JobTypes.Count, Is.EqualTo(3));
            Assert.That(result.Value.JobTypes.Single(j => j.JobType == JobType.ReceiveCheck).DetailText,
                Is.EqualTo("เช็ค 2 ใบ"));
        }

        [Test]
        public void ใบงานที่มีรับเอกสาร_ต้องถูกทำเครื่องหมายว่าต้องยืนยันรับของ()
        {
            // BR-4 — เงื่อนไขนี้จะถูกบังคับจริงตอนปิดงานใน Phase 3
            var command = ValidCommand();
            command.JobTypes = new List<JobTypeInput>
            {
                new JobTypeInput { JobType = JobType.ReceiveDoc }
            };

            var result = _service.Create(command, User());

            Assert.That(result.Value.RequiresReceiptConfirmation, Is.True);
        }

        // ==================== D17 : แจ้งแทนคนอื่น ====================

        [Test]
        public void ไม่ระบุผู้แจ้ง_ใช้ตัวเองเป็นผู้แจ้ง()
        {
            var result = _service.Create(ValidCommand(), User("10002"));

            Assert.That(result.Value.RequesterEmpCode, Is.EqualTo("10002"));
        }

        [Test]
        public void แจ้งแทนคนในสาขาเดียวกัน_สำเร็จ()
        {
            var command = ValidCommand();
            command.RequesterEmpCode = "10004";

            var result = _service.Create(command, User("10002", "SDC"));

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.RequesterEmpCode, Is.EqualTo("10004"));
        }

        [Test]
        public void แจ้งแทนคนสาขาอื่น_ต้องไม่สำเร็จ()
        {
            // BR-6 — ข้ามสาขาไม่ได้แม้จะรู้รหัสพนักงาน
            var command = ValidCommand();
            command.RequesterEmpCode = "20002";

            var result = _service.Create(command, User("10002", "SDC"));

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void แจ้งแทนคนที่ไม่มีในระบบ_ต้องไม่สำเร็จ()
        {
            var command = ValidCommand();
            command.RequesterEmpCode = "99999";

            Assert.That(_service.Create(command, User()).Success, Is.False);
        }

        [Test]
        public void ผู้ใช้ที่ไม่มีในระบบแล้ว_ต้องได้ข้อความให้ล็อกอินใหม่()
        {
            // เกิดได้จริงเมื่อข้อมูลพนักงานถูกล้าง แต่ cookie login เดิมยังไม่หมดอายุ
            // ถ้าไม่ดักไว้ ใบงานจะถูกบันทึกเป็นชื่อคนอื่นเงียบ ๆ หรือพังที่ระดับ foreign key
            var ghost = User("70001", "SDC");

            var result = _service.Create(ValidCommand(), ghost);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("เข้าสู่ระบบใหม่"));
        }

        [Test]
        public void รายชื่อผู้แจ้งต้องมีตัวผู้ใช้เองเสมอแม้ไม่มีใน_cache()
        {
            // ถ้ารายการไม่มีตัวเอง ช่องผู้แจ้งบนฟอร์มจะตกไปเป็นคนแรกของรายการ
            var ghost = User("70001", "SDC");

            var selectable = _service.GetSelectableRequesters(ghost);

            Assert.That(selectable.Any(e => e.EmpCode == "70001"), Is.True);
            Assert.That(selectable.First().EmpCode, Is.EqualTo("70001"),
                "ตัวผู้ใช้เองต้องมาก่อน เพื่อให้เป็นค่าเริ่มต้นของ dropdown");
        }

        [Test]
        public void รายชื่อผู้แจ้งต้องมีเฉพาะคนในสาขาเดียวกัน()
        {
            var selectable = _service.GetSelectableRequesters(User("10002", "SDC"));

            Assert.That(selectable.All(e => e.BranchCode == "SDC"), Is.True);
            Assert.That(selectable.Any(e => e.EmpCode == "20002"), Is.False);
        }

        [Test]
        public void แจ้งแทนคนอื่น_ต้องบันทึกคนกดสร้างแยกจากผู้แจ้ง()
        {
            var command = ValidCommand();
            command.RequesterEmpCode = "10004";

            var result = _service.Create(command, User("10002", "SDC"));

            Assert.That(result.Value.RequesterEmpCode, Is.EqualTo("10004"));
            Assert.That(result.Value.CreatedBy, Is.EqualTo("10002"));
        }

        // ==================== BR-2 : edit lock ====================

        [Test]
        public void เจ้าของแก้ใบงานสถานะรับแจ้งได้()
        {
            var created = _service.Create(ValidCommand(), User("10002"));

            Assert.That(_service.CanEdit(created.Value, User("10002")), Is.True);
        }

        [Test]
        public void เจ้าของแก้ไม่ได้เมื่อ_Messenger_ยืนยันรับงานแล้ว()
        {
            var created = _service.Create(ValidCommand(), User("10002"));
            _requests.SetStatus(created.Value.ReqId, RequestStatus.Delivering);

            var locked = _requests.Peek(created.Value.ReqId);

            Assert.That(_service.CanEdit(locked, User("10002")), Is.False);
        }

        [TestCase(RequestStatus.Delivering)]
        [TestCase(RequestStatus.Paused)]
        [TestCase(RequestStatus.Completed)]
        [TestCase(RequestStatus.Cancelled)]
        public void ทุกสถานะที่ไม่ใช่รับแจ้ง_เจ้าของแก้ไม่ได้(RequestStatus status)
        {
            var created = _service.Create(ValidCommand(), User("10002"));
            _requests.SetStatus(created.Value.ReqId, status);

            var request = _requests.Peek(created.Value.ReqId);

            Assert.That(_service.CanEdit(request, User("10002")), Is.False);
        }

        [Test]
        public void คนอื่นที่ไม่ใช่_Admin_แก้ใบของคนอื่นไม่ได้()
        {
            var created = _service.Create(ValidCommand(), User("10002"));

            Assert.That(_service.CanEdit(created.Value, User("10004")), Is.False);
        }

        [Test]
        public void Messenger_ก็แก้ใบของคนอื่นไม่ได้()
        {
            // §5 — "แก้ใบงานคนอื่น" เป็นสิทธิ์ของ Admin เท่านั้น
            var created = _service.Create(ValidCommand(), User("10002"));

            Assert.That(_service.CanEdit(created.Value, User("10003", "SDC", Role.Messenger)), Is.False);
        }

        [TestCase(RequestStatus.Received)]
        [TestCase(RequestStatus.Delivering)]
        [TestCase(RequestStatus.Completed)]
        public void Admin_แก้ได้ทุกสถานะ(RequestStatus status)
        {
            var created = _service.Create(ValidCommand(), User("10002"));
            _requests.SetStatus(created.Value.ReqId, status);

            var request = _requests.Peek(created.Value.ReqId);

            Assert.That(_service.CanEdit(request, User("10001", "SDC", Role.Admin)), Is.True);
        }

        [Test]
        public void Admin_สาขาอื่นแก้ไม่ได้()
        {
            // BR-6 — Admin จำกัดเฉพาะสาขาตัวเอง ไม่ใช่ global (D2)
            var created = _service.Create(ValidCommand(), User("10002", "SDC"));

            Assert.That(_service.CanEdit(created.Value, User("20001", "SBK", Role.Admin)), Is.False);
        }

        // ==================== BR-2 : optimistic locking ====================

        [Test]
        public void แก้ไขด้วยข้อมูลรุ่นล่าสุด_สำเร็จ()
        {
            var created = _service.Create(ValidCommand(), User("10002"));

            var result = _service.Update(UpdateFrom(created.Value.ReqId, created.Value.RowVersion,
                contactName: "ชื่อใหม่"), User("10002"));

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.ContactName, Is.EqualTo("ชื่อใหม่"));
        }

        [Test]
        public void มีคนแก้ตัดหน้า_ต้องได้ผลลัพธ์เป็น_conflict()
        {
            var created = _service.Create(ValidCommand(), User("10002"));
            var staleRowVersion = created.Value.RowVersion;

            // มีคนอื่นบันทึกใบนี้ไปแล้วระหว่างที่เรากำลังกรอกฟอร์ม
            _requests.SimulateExternalEdit(created.Value.ReqId);

            var result = _service.Update(UpdateFrom(created.Value.ReqId, staleRowVersion,
                contactName: "แก้ทับ"), User("10002"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.IsConcurrencyConflict, Is.True);
        }

        [Test]
        public void แก้ไขสำเร็จแล้ว_rowVersion_ต้องเปลี่ยน()
        {
            var created = _service.Create(ValidCommand(), User("10002"));

            var result = _service.Update(UpdateFrom(created.Value.ReqId, created.Value.RowVersion,
                contactName: "ชื่อใหม่"), User("10002"));

            Assert.That(result.Value.RowVersion, Is.Not.EqualTo(created.Value.RowVersion));
        }

        [Test]
        public void แก้ไขโดยไม่ส่ง_rowVersion_ต้องไม่สำเร็จ()
        {
            var created = _service.Create(ValidCommand(), User("10002"));

            var result = _service.Update(UpdateFrom(created.Value.ReqId, null,
                contactName: "ชื่อใหม่"), User("10002"));

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void แก้ไขใบงานที่ถูกล็อกแล้ว_ต้องไม่สำเร็จแม้ส่ง_rowVersion_ถูก()
        {
            var created = _service.Create(ValidCommand(), User("10002"));
            _requests.SetStatus(created.Value.ReqId, RequestStatus.Delivering);

            var result = _service.Update(UpdateFrom(created.Value.ReqId, created.Value.RowVersion,
                contactName: "แอบแก้"), User("10002"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.IsConcurrencyConflict, Is.False, "ต้องเป็นเรื่องสิทธิ์/สถานะ ไม่ใช่ conflict");
        }

        // ==================== BR-6 : branch isolation ====================

        [Test]
        public void ดูใบงานของสาขาอื่นไม่ได้()
        {
            var created = _service.Create(ValidCommand(), User("10002", "SDC"));

            var result = _service.Get(created.Value.ReqId, User("20002", "SBK"));

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void แก้ใบงานของสาขาอื่นไม่ได้()
        {
            var created = _service.Create(ValidCommand(), User("10002", "SDC"));

            var result = _service.Update(UpdateFrom(created.Value.ReqId, created.Value.RowVersion,
                contactName: "ข้ามสาขา"), User("20002", "SBK"));

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void รายการใบงานของ_User_เห็นเฉพาะใบตัวเอง()
        {
            _service.Create(ValidCommand(), User("10002"));

            var otherPersonCommand = ValidCommand();
            otherPersonCommand.RequesterEmpCode = "10004";
            _service.Create(otherPersonCommand, User("10002"));

            var mine = _service.List(User("10002"), null, null);

            Assert.That(mine.Count, Is.EqualTo(1));
            Assert.That(mine.All(r => r.RequesterEmpCode == "10002"), Is.True);
        }

        [Test]
        public void รายการใบงานของ_Admin_และ_Messenger_เห็นทั้งสาขา()
        {
            _service.Create(ValidCommand(), User("10002"));

            var otherPersonCommand = ValidCommand();
            otherPersonCommand.RequesterEmpCode = "10004";
            _service.Create(otherPersonCommand, User("10002"));

            Assert.That(_service.List(User("10001", "SDC", Role.Admin), null, null).Count, Is.EqualTo(2));
            Assert.That(_service.List(User("10003", "SDC", Role.Messenger), null, null).Count, Is.EqualTo(2));
        }

        [Test]
        public void รายการใบงานต้องไม่ข้ามสาขา()
        {
            _service.Create(ValidCommand(), User("10002", "SDC"));
            _service.Create(ValidCommand(), User("20002", "SBK"));

            var sdc = _service.List(User("10001", "SDC", Role.Admin), null, null);

            Assert.That(sdc.Count, Is.EqualTo(1));
            Assert.That(sdc.All(r => r.BranchCode == "SDC"), Is.True);
        }

        // ---------------- helper ----------------

        private static UpdateRequestCommand UpdateFrom(int reqId, byte[] rowVersion, string contactName)
        {
            return new UpdateRequestCommand
            {
                ReqId = reqId,
                RowVersion = rowVersion,
                SendDate = new DateTime(2026, 8, 17),
                ContactName = contactName,
                Address = "123 ถนนทดสอบ",
                Detail = "ส่งเอกสารสัญญา",
                JobTypes = new List<JobTypeInput>
                {
                    new JobTypeInput { JobType = JobType.SendDoc }
                }
            };
        }
    }
}
