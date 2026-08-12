using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Application.Dtos;
using Messenger.Application.Services;
using Messenger.Domain.Enums;
using Messenger.Domain.Workflow;
using Messenger.UnitTests.Fakes;
using NUnit.Framework;

namespace Messenger.UnitTests
{
    /// <summary>
    /// Phase 2 — workflow ของ Messenger : ยืนยันรับงาน จัดลำดับคิว และเปลี่ยนสถานะ (§6)
    ///
    /// เวลาอ้างอิงคือ จันทร์ที่ 10 ส.ค. 2026 เวลา 09:00 (ก่อนเวลาตัดรอบ 10:00)
    /// ใบงานที่สร้างในเทสต์จึงมี sendDate = วันเดียวกันเสมอ ตาม BR-1
    /// </summary>
    [TestFixture]
    public class RequestWorkflowServiceTests
    {
        private static readonly DateTime MondayMorning = new DateTime(2026, 8, 10, 9, 0, 0);
        private static readonly DateTime Monday = new DateTime(2026, 8, 10);
        private static readonly DateTime Tuesday = new DateTime(2026, 8, 11);

        private FakeDeliveryRequestRepository _requests;
        private FakeEmployeeRepository _employees;
        private FakeClock _clock;
        private DeliveryRequestService _requestService;
        private RequestWorkflowService _workflow;

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
                .WithEmployee("20001", "SBK", "A")
                .WithEmployee("20003", "SBK", "M");

            _clock = new FakeClock(MondayMorning);
            _requestService = new DeliveryRequestService(_requests, _employees, _clock);
            _workflow = new RequestWorkflowService(_requests, _requests, _employees, _clock);
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

        private static UserContext Admin(string branchCode = "SDC")
        {
            return User(branchCode == "SDC" ? "10001" : "20001", branchCode, Role.Admin);
        }

        private static CreateRequestCommand ValidCommand(DateTime? sendDate = null,
                                                         params JobType[] jobTypes)
        {
            var types = jobTypes.Length > 0 ? jobTypes : new[] { JobType.SendDoc };

            return new CreateRequestCommand
            {
                ContactName = "บริษัท ตัวอย่าง จำกัด",
                Address = "123 ถนนทดสอบ",
                Detail = "ส่งเอกสารสัญญา",
                SendDate = sendDate,
                JobTypes = types.Select(t => new JobTypeInput { JobType = t }).ToList()
            };
        }

        /// <summary>สร้างใบงาน 1 ใบแล้วคืน reqId</summary>
        private int NewRequest(string requesterEmpCode = "10002", string branchCode = "SDC",
                               DateTime? sendDate = null)
        {
            var result = _requestService.Create(ValidCommand(sendDate), User(requesterEmpCode, branchCode));
            Assert.That(result.Success, Is.True, result.FirstError);
            return result.Value.ReqId;
        }

        /// <summary>ใบงานที่มีประเภท "รับเอกสาร" และยืนยันรับงานแล้ว — ใบที่ติดเงื่อนไข BR-4</summary>
        private int ReceiveDocRequest()
        {
            var created = _requestService.Create(
                ValidCommand(null, JobType.SendDoc, JobType.ReceiveDoc), User("10002"));

            Assert.That(created.Success, Is.True, created.FirstError);
            Assert.That(created.Value.RequiresReceiptConfirmation, Is.True);

            var confirmed = _workflow.Apply(created.Value.ReqId, RequestAction.Confirm, null, Messenger());
            Assert.That(confirmed.Success, Is.True, confirmed.FirstError);

            return created.Value.ReqId;
        }

        /// <summary>สร้างใบงานที่ยืนยันรับงานแล้ว (สถานะ Delivering)</summary>
        private int ConfirmedRequest(string branchCode = "SDC", DateTime? sendDate = null)
        {
            var reqId = NewRequest(branchCode == "SDC" ? "10002" : "20002", branchCode, sendDate);
            var confirmed = _workflow.Apply(reqId, RequestAction.Confirm, null, Messenger(branchCode));
            Assert.That(confirmed.Success, Is.True, confirmed.FirstError);
            return reqId;
        }

        // ==================== ยืนยันรับงาน (Received → Delivering) ====================

        [Test]
        public void Messenger_ยืนยันรับงานได้_และได้ลำดับแรกของวัน()
        {
            var reqId = NewRequest();

            var result = _workflow.Apply(reqId, RequestAction.Confirm, null, Messenger());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.Status, Is.EqualTo(RequestStatus.Delivering));
            Assert.That(result.Value.Assignment, Is.Not.Null);
            Assert.That(result.Value.Assignment.SequenceOrder, Is.EqualTo(1));
            Assert.That(result.Value.Assignment.MessengerEmpCode, Is.EqualTo("10003"));
        }

        [Test]
        public void User_ยืนยันรับงานไม่ได้แม้เป็นใบของตัวเอง()
        {
            var reqId = NewRequest("10002");

            var result = _workflow.Apply(reqId, RequestAction.Confirm, null, User("10002"));

            Assert.That(result.Success, Is.False);
            Assert.That(_requests.Peek(reqId).Status, Is.EqualTo(RequestStatus.Received));
        }

        [Test]
        public void Admin_ยืนยันแทนได้_แต่ผู้รับงานคือ_Messenger_ประจำสาขา()
        {
            // D22 — Admin เป็นคนกด แต่คนวิ่งงานจริงคือ Messenger ของสาขานั้น
            var reqId = NewRequest();

            var result = _workflow.Apply(reqId, RequestAction.Confirm, null, Admin());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.Assignment.MessengerEmpCode, Is.EqualTo("10003"));
        }

        [Test]
        public void Admin_ยืนยันแทนไม่ได้ถ้าสาขายังไม่มี_Messenger()
        {
            var employees = new FakeEmployeeRepository()
                .WithEmployee("10002", "SDC")
                .WithEmployee("10001", "SDC", "A");

            var requestService = new DeliveryRequestService(_requests, employees, _clock);
            var workflow = new RequestWorkflowService(_requests, _requests, employees, _clock);

            var created = requestService.Create(ValidCommand(), User("10002"));
            var result = workflow.Apply(created.Value.ReqId, RequestAction.Confirm, null, Admin());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ยังไม่มีเจ้าหน้าที่ Messenger"));
            Assert.That(_requests.Peek(created.Value.ReqId).Status, Is.EqualTo(RequestStatus.Received));
        }

        [Test]
        public void ลำดับวิ่งงานเดินต่อภายในวันเดียวกัน()
        {
            var first = ConfirmedRequest();
            var second = ConfirmedRequest();
            var third = ConfirmedRequest();

            Assert.That(_requests.Peek(first).Assignment.SequenceOrder, Is.EqualTo(1));
            Assert.That(_requests.Peek(second).Assignment.SequenceOrder, Is.EqualTo(2));
            Assert.That(_requests.Peek(third).Assignment.SequenceOrder, Is.EqualTo(3));
        }

        [Test]
        public void ลำดับวิ่งงานเริ่มนับใหม่ในวันถัดไป()
        {
            ConfirmedRequest();

            // D11 — ลำดับเป็นของ "วัน" ไม่ใช่ของใบงานสะสม
            var tomorrow = ConfirmedRequest(sendDate: Tuesday);

            Assert.That(_requests.Peek(tomorrow).Assignment.SequenceOrder, Is.EqualTo(1));
        }

        [Test]
        public void ลำดับวิ่งงานแยกกันคนละสาขา()
        {
            ConfirmedRequest("SDC");
            var sbk = ConfirmedRequest("SBK");

            Assert.That(_requests.Peek(sbk).Assignment.SequenceOrder, Is.EqualTo(1));
        }

        [Test]
        public void ยืนยันซ้ำหลังมีคนกดตัดหน้าไปแล้ว_ต้องไม่สำเร็จ()
        {
            var reqId = NewRequest();

            var first = _workflow.Apply(reqId, RequestAction.Confirm, null, Messenger());
            var second = _workflow.Apply(reqId, RequestAction.Confirm, null, Admin());

            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.False);
        }

        [Test]
        public void ยืนยันใบงานของสาขาอื่นไม่ได้()
        {
            // BR-6 — Messenger สาขา SBK ต้องมองไม่เห็นใบของ SDC เลย
            var reqId = NewRequest("10002", "SDC");

            var result = _workflow.Apply(reqId, RequestAction.Confirm, null, Messenger("SBK"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ไม่พบใบแจ้งงาน"));
            Assert.That(_requests.Peek(reqId).Status, Is.EqualTo(RequestStatus.Received));
        }

        // ==================== พัก / กลับมาส่งต่อ ====================

        [Test]
        public void พักการส่งต้องระบุเหตุผล()
        {
            var reqId = ConfirmedRequest();

            var result = _workflow.Apply(reqId, RequestAction.Pause, "   ", Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("เหตุผล"));
            Assert.That(_requests.Peek(reqId).Status, Is.EqualTo(RequestStatus.Delivering));
        }

        [Test]
        public void พักการส่งพร้อมเหตุผลได้_และเหตุผลถูกบันทึกในประวัติ()
        {
            var reqId = ConfirmedRequest();

            var result = _workflow.Apply(reqId, RequestAction.Pause, "ผู้รับไม่อยู่", Messenger());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.Status, Is.EqualTo(RequestStatus.Paused));

            var history = _workflow.GetHistory(reqId, Messenger()).Value;
            Assert.That(history.Last().Note, Does.Contain("ผู้รับไม่อยู่"));
        }

        [Test]
        public void เหตุผลยาวเกินกำหนดต้องไม่ผ่าน()
        {
            var reqId = ConfirmedRequest();
            var tooLong = new string('ก', RequestWorkflowService.MaxReasonLength + 1);

            var result = _workflow.Apply(reqId, RequestAction.Pause, tooLong, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ยาวเกิน"));
        }

        [Test]
        public void กลับมาส่งต่อจากสถานะพักได้()
        {
            var reqId = ConfirmedRequest();
            _workflow.Apply(reqId, RequestAction.Pause, "ฝนตกหนัก", Messenger());

            var result = _workflow.Apply(reqId, RequestAction.Resume, null, Messenger());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.Status, Is.EqualTo(RequestStatus.Delivering));
        }

        [Test]
        public void พักซ้ำจากสถานะพักไม่ได้()
        {
            var reqId = ConfirmedRequest();
            _workflow.Apply(reqId, RequestAction.Pause, "ฝนตกหนัก", Messenger());

            var result = _workflow.Apply(reqId, RequestAction.Pause, "ฝนยังไม่หยุด", Messenger());

            Assert.That(result.Success, Is.False, "§6 ไม่มีเส้นทาง Paused → Paused");
        }

        // ==================== ปิดงาน ====================

        [Test]
        public void ปิดงานจากสถานะกำลังส่งได้()
        {
            var reqId = ConfirmedRequest();

            var result = _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.Status, Is.EqualTo(RequestStatus.Completed));
        }

        [Test]
        public void ปิดงานจากสถานะรับแจ้งไม่ได้()
        {
            var reqId = NewRequest();

            var result = _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());

            Assert.That(result.Success, Is.False, "§6 ไม่มีเส้นทาง Received → Completed");
            Assert.That(_requests.Peek(reqId).Status, Is.EqualTo(RequestStatus.Received));
        }

        [Test]
        public void ปิดงานจากสถานะพักไม่ได้ต้องกลับมาส่งต่อก่อน()
        {
            var reqId = ConfirmedRequest();
            _workflow.Apply(reqId, RequestAction.Pause, "รอเอกสารเพิ่ม", Messenger());

            var result = _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());

            Assert.That(result.Success, Is.False, "§6 ไม่มีเส้นทาง Paused → Completed");
        }

        // ==================== BR-4 : เงื่อนไขปิดงานของใบที่มี "รับเอกสาร" ====================

        [Test]
        public void ใบที่มีรับเอกสารปิดงานไม่ได้ถ้ายังไม่ยืนยันรับของ()
        {
            var reqId = ReceiveDocRequest();

            var result = _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ยืนยันว่ารับของแล้ว"));
            Assert.That(_requests.Peek(reqId).Status, Is.EqualTo(RequestStatus.Delivering));
        }

        [Test]
        public void ยืนยันรับของแล้วจึงปิดงานได้()
        {
            var reqId = ReceiveDocRequest();

            var confirmed = _workflow.ConfirmReceipt(reqId, Messenger());
            var completed = _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());

            Assert.That(confirmed.Success, Is.True, confirmed.FirstError);
            Assert.That(confirmed.Value.ReceiptConfirmed, Is.True);
            Assert.That(confirmed.Value.ReceiptConfirmedBy, Is.EqualTo("10003"));
            Assert.That(completed.Success, Is.True, completed.FirstError);
            Assert.That(completed.Value.Status, Is.EqualTo(RequestStatus.Completed));
        }

        [Test]
        public void ใบที่ไม่มีรับเอกสารปิดงานได้เลยและไม่ต้องยืนยันรับของ()
        {
            var reqId = ConfirmedRequest();

            var confirmReceipt = _workflow.ConfirmReceipt(reqId, Messenger());
            var completed = _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());

            Assert.That(confirmReceipt.Success, Is.False, "ใบที่ไม่มี ReceiveDoc ไม่ควรให้กดยืนยันรับของ");
            Assert.That(completed.Success, Is.True, completed.FirstError);
        }

        [Test]
        public void ยกเลิกใบที่มีรับเอกสารได้โดยไม่ต้องยืนยันรับของ()
        {
            // BR-4 เป็นเงื่อนไขของการ "ปิดงาน" เท่านั้น ไม่เกี่ยวกับการยกเลิก
            var reqId = ReceiveDocRequest();

            var result = _workflow.Apply(reqId, RequestAction.Cancel, "ผู้รับปิดกิจการ", Messenger());

            Assert.That(result.Success, Is.True, result.FirstError);
        }

        [Test]
        public void User_ยืนยันรับของไม่ได้()
        {
            var reqId = ReceiveDocRequest();

            var result = _workflow.ConfirmReceipt(reqId, User("10002"));

            Assert.That(result.Success, Is.False);
            Assert.That(_requests.Peek(reqId).ReceiptConfirmed, Is.False);
        }

        [Test]
        public void ยืนยันรับของข้ามสาขาไม่ได้()
        {
            var reqId = ReceiveDocRequest();

            var result = _workflow.ConfirmReceipt(reqId, Messenger("SBK"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ไม่พบใบแจ้งงาน"));
        }

        [Test]
        public void ยืนยันรับของตอนยังไม่รับงานไม่ได้()
        {
            // D23 — ยืนยันได้เฉพาะช่วงที่งานกำลังเดินอยู่ เหมือนกฎของรูป
            var created = _requestService.Create(
                ValidCommand(null, JobType.SendDoc, JobType.ReceiveDoc), User("10002"));

            var result = _workflow.ConfirmReceipt(created.Value.ReqId, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("กำลังส่ง"));
        }

        [Test]
        public void กดยืนยันรับของซ้ำไม่ทับข้อมูลคนแรก()
        {
            var reqId = ReceiveDocRequest();

            _workflow.ConfirmReceipt(reqId, Messenger());
            var again = _workflow.ConfirmReceipt(reqId, Admin());

            Assert.That(again.Success, Is.True, "กดซ้ำถือว่าไม่มีอะไรต้องทำ ไม่ใช่ข้อผิดพลาด");
            Assert.That(_requests.Peek(reqId).ReceiptConfirmedBy, Is.EqualTo("10003"));
        }

        [Test]
        public void ปุ่มยืนยันรับของโผล่เฉพาะใบที่ติดเงื่อนไข_BR4()
        {
            var withReceiveDoc = _requests.Peek(ReceiveDocRequest());
            var withoutReceiveDoc = _requests.Peek(ConfirmedRequest());

            Assert.That(_workflow.CanConfirmReceipt(withReceiveDoc, Messenger()), Is.True);
            Assert.That(_workflow.CanConfirmReceipt(withReceiveDoc, User("10002")), Is.False);
            Assert.That(_workflow.CanConfirmReceipt(withoutReceiveDoc, Messenger()), Is.False);

            _workflow.ConfirmReceipt(withReceiveDoc.ReqId, Messenger());
            Assert.That(_workflow.CanConfirmReceipt(_requests.Peek(withReceiveDoc.ReqId), Messenger()), Is.False,
                "ยืนยันไปแล้วก็ไม่ต้องโชว์ปุ่มอีก");
        }

        [Test]
        public void ปิดงานซ้ำไม่ได้เพราะเป็นสถานะสุดท้าย()
        {
            var reqId = ConfirmedRequest();
            _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());

            var result = _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("สถานะสุดท้าย"));
        }

        // ==================== ยกเลิก (D7) ====================

        [Test]
        public void เจ้าของใบยกเลิกได้ตอนสถานะรับแจ้ง_โดยไม่ต้องมีเหตุผล()
        {
            var reqId = NewRequest("10002");

            var result = _workflow.Apply(reqId, RequestAction.Cancel, null, User("10002"));

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.Status, Is.EqualTo(RequestStatus.Cancelled));
        }

        [Test]
        public void User_ยกเลิกใบของคนอื่นไม่ได้()
        {
            var reqId = NewRequest("10002");

            var result = _workflow.Apply(reqId, RequestAction.Cancel, null, User("10004"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ตัวเองเป็นผู้แจ้ง"));
            Assert.That(_requests.Peek(reqId).Status, Is.EqualTo(RequestStatus.Received));
        }

        [Test]
        public void User_ยกเลิกไม่ได้แล้วเมื่อ_Messenger_รับงานไปแล้ว()
        {
            var reqId = ConfirmedRequest();

            var result = _workflow.Apply(reqId, RequestAction.Cancel, "เปลี่ยนใจ", User("10002"));

            Assert.That(result.Success, Is.False);
            Assert.That(_requests.Peek(reqId).Status, Is.EqualTo(RequestStatus.Delivering));
        }

        [Test]
        public void Messenger_ยกเลิกระหว่างส่งได้แต่ต้องมีเหตุผล()
        {
            var reqId = ConfirmedRequest();

            var withoutReason = _workflow.Apply(reqId, RequestAction.Cancel, null, Messenger());
            Assert.That(withoutReason.Success, Is.False);

            var withReason = _workflow.Apply(reqId, RequestAction.Cancel, "ผู้แจ้งขอยกเลิก", Messenger());
            Assert.That(withReason.Success, Is.True, withReason.FirstError);
            Assert.That(withReason.Value.Status, Is.EqualTo(RequestStatus.Cancelled));
        }

        [Test]
        public void Admin_ยกเลิกจากสถานะพักได้()
        {
            var reqId = ConfirmedRequest();
            _workflow.Apply(reqId, RequestAction.Pause, "รอผู้รับติดต่อกลับ", Messenger());

            var result = _workflow.Apply(reqId, RequestAction.Cancel, "ติดต่อไม่ได้", Admin());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.Status, Is.EqualTo(RequestStatus.Cancelled));
        }

        [Test]
        public void ยกเลิกใบที่ปิดงานแล้วไม่ได้()
        {
            var reqId = ConfirmedRequest();
            _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());

            var result = _workflow.Apply(reqId, RequestAction.Cancel, "ยกเลิกย้อนหลัง", Admin());

            Assert.That(result.Success, Is.False);
        }

        // ==================== audit trail ====================

        [Test]
        public void ทุกการเปลี่ยนสถานะถูกบันทึกไว้ครบตามลำดับ()
        {
            var reqId = ConfirmedRequest();
            _workflow.Apply(reqId, RequestAction.Pause, "แวะรับเอกสารเพิ่ม", Messenger());
            _workflow.Apply(reqId, RequestAction.Resume, null, Messenger());
            _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());

            var history = _workflow.GetHistory(reqId, Messenger()).Value;

            Assert.That(history.Select(h => h.ToStatus), Is.EqualTo(new[]
            {
                RequestStatus.Received,      // ตอนสร้างใบงาน
                RequestStatus.Delivering,
                RequestStatus.Paused,
                RequestStatus.Delivering,
                RequestStatus.Completed
            }));

            Assert.That(history.First().FromStatus, Is.Null, "บรรทัดแรกคือการสร้างใบงาน");
        }

        [Test]
        public void ประวัติของใบงานสาขาอื่นดูไม่ได้()
        {
            var reqId = NewRequest("10002", "SDC");

            var result = _workflow.GetHistory(reqId, Messenger("SBK"));

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void User_ดูประวัติได้เฉพาะใบของตัวเอง()
        {
            var reqId = NewRequest("10002");

            Assert.That(_workflow.GetHistory(reqId, User("10002")).Success, Is.True);
            Assert.That(_workflow.GetHistory(reqId, User("10004")).Success, Is.False);
        }

        // ==================== คิวงาน ====================

        [Test]
        public void คิวงานแยกเป็นรอยืนยัน_กำลังวิ่ง_และปิดแล้ว()
        {
            NewRequest();                       // รอยืนยัน
            var running = ConfirmedRequest();   // กำลังวิ่ง
            var closed = ConfirmedRequest();
            _workflow.Apply(closed, RequestAction.Complete, null, Messenger());

            var queue = _workflow.GetQueue(Messenger(), Monday).Value;

            Assert.That(queue.Pending.Count, Is.EqualTo(1));
            Assert.That(queue.Running.Count, Is.EqualTo(1));
            Assert.That(queue.Running.Single().ReqId, Is.EqualTo(running));
            Assert.That(queue.Closed.Count, Is.EqualTo(1));
            Assert.That(queue.TotalCount, Is.EqualTo(3));
        }

        [Test]
        public void คิวงานเห็นเฉพาะสาขาตัวเอง()
        {
            ConfirmedRequest("SDC");
            ConfirmedRequest("SBK");

            var queue = _workflow.GetQueue(Messenger("SDC"), Monday).Value;

            Assert.That(queue.TotalCount, Is.EqualTo(1));
            Assert.That(queue.Running.All(r => r.BranchCode == "SDC"), Is.True);
        }

        [Test]
        public void คิวงานไม่เปิดให้_User()
        {
            NewRequest();

            var result = _workflow.GetQueue(User("10002"), Monday);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("Messenger"));
        }

        [Test]
        public void คิวงานของวันอื่นไม่ปนกัน()
        {
            ConfirmedRequest(sendDate: Monday);
            ConfirmedRequest(sendDate: Tuesday);

            Assert.That(_workflow.GetQueue(Messenger(), Monday).Value.TotalCount, Is.EqualTo(1));
            Assert.That(_workflow.GetQueue(Messenger(), Tuesday).Value.TotalCount, Is.EqualTo(1));
        }

        // ==================== จัดลำดับคิว ====================

        [Test]
        public void เลื่อนงานขึ้นแล้วลำดับสลับกับใบที่อยู่เหนือ()
        {
            var first = ConfirmedRequest();
            var second = ConfirmedRequest();

            var result = _workflow.Move(second, QueueMove.Up, Messenger());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(_requests.Peek(second).Assignment.SequenceOrder, Is.EqualTo(1));
            Assert.That(_requests.Peek(first).Assignment.SequenceOrder, Is.EqualTo(2));
        }

        [Test]
        public void เลื่อนงานลงแล้วลำดับสลับกับใบที่อยู่ถัดไป()
        {
            var first = ConfirmedRequest();
            var second = ConfirmedRequest();

            var result = _workflow.Move(first, QueueMove.Down, Messenger());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(_requests.Peek(first).Assignment.SequenceOrder, Is.EqualTo(2));
            Assert.That(_requests.Peek(second).Assignment.SequenceOrder, Is.EqualTo(1));
        }

        [Test]
        public void เลื่อนขึ้นจากหัวคิวไม่ได้()
        {
            var first = ConfirmedRequest();
            ConfirmedRequest();

            var result = _workflow.Move(first, QueueMove.Up, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("บนสุด"));
        }

        [Test]
        public void เลื่อนลงจากท้ายคิวไม่ได้()
        {
            ConfirmedRequest();
            var last = ConfirmedRequest();

            var result = _workflow.Move(last, QueueMove.Down, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ล่างสุด"));
        }

        [Test]
        public void ใบที่ยังไม่ยืนยันรับงานยังไม่มีลำดับให้จัด()
        {
            var reqId = NewRequest();

            var result = _workflow.Move(reqId, QueueMove.Up, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ยังไม่ถูกยืนยันรับงาน"));
        }

        [Test]
        public void ใบที่ปิดแล้วจัดลำดับใหม่ไม่ได้()
        {
            ConfirmedRequest();
            var closed = ConfirmedRequest();
            _workflow.Apply(closed, RequestAction.Complete, null, Messenger());

            var result = _workflow.Move(closed, QueueMove.Up, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ปิดแล้ว"));
        }

        [Test]
        public void User_จัดลำดับคิวไม่ได้()
        {
            var reqId = ConfirmedRequest();

            var result = _workflow.Move(reqId, QueueMove.Up, User("10002"));

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void จัดลำดับใบงานของสาขาอื่นไม่ได้()
        {
            var reqId = ConfirmedRequest("SDC");

            var result = _workflow.Move(reqId, QueueMove.Down, Messenger("SBK"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ไม่พบใบแจ้งงาน"));
        }

        // ==================== ปุ่มที่แสดงบนหน้าจอ ====================

        [Test]
        public void ปุ่มที่แสดงต้องตรงกับสิทธิ์และสถานะจริง()
        {
            var reqId = NewRequest("10002");
            var request = _requests.Peek(reqId);

            Assert.That(_workflow.AvailableActions(request, Messenger()).Select(t => t.Action),
                Is.EquivalentTo(new[] { RequestAction.Confirm, RequestAction.Cancel }));

            Assert.That(_workflow.AvailableActions(request, User("10002")).Select(t => t.Action),
                Is.EquivalentTo(new[] { RequestAction.Cancel }));

            Assert.That(_workflow.AvailableActions(request, User("10004")), Is.Empty);
        }

        [Test]
        public void ใบงานต่างสาขาไม่มีปุ่มให้กด()
        {
            var reqId = NewRequest("10002", "SDC");
            var request = _requests.Peek(reqId);

            Assert.That(_workflow.AvailableActions(request, Messenger("SBK")), Is.Empty);
        }

        [Test]
        public void ค่า_action_ที่ไม่รู้จักต้องถูกปฏิเสธ()
        {
            var reqId = NewRequest();

            var result = _workflow.Apply(reqId, (RequestAction)999, null, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ไม่รู้จัก"));
        }
    }
}
