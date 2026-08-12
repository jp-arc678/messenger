using System;
using System.Linq;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;
using Messenger.Application.Services;
using Messenger.UnitTests.Fakes;
using NUnit.Framework;

namespace Messenger.UnitTests
{
    /// <summary>
    /// Phase 4 — อีเมลแจ้งผู้แจ้งเมื่อปิดงาน (BR-5) และกฎ D26–D28
    ///
    /// เทสต์ชุดนี้ทดสอบ "การประกอบและส่งเมล" ล้วน ๆ ส่วนการผูกเข้ากับการปิดงาน
    /// อยู่ใน RequestWorkflowServiceTests
    /// </summary>
    [TestFixture]
    public class RequestNotificationServiceTests
    {
        private static readonly DateTime MondayMorning = new DateTime(2026, 8, 10, 9, 0, 0);

        private FakeEmailSender _sender;
        private FakeEmailTemplateSource _templates;
        private FakeEmployeeRepository _employees;
        private FakeClock _clock;
        private RequestNotificationService _service;

        [SetUp]
        public void SetUp()
        {
            _sender = new FakeEmailSender();
            _templates = new FakeEmailTemplateSource();
            _employees = new FakeEmployeeRepository()
                .WithEmployee("10002", "SDC")
                .WithEmployee("10004", "SDC");
            _clock = new FakeClock(MondayMorning);
            _service = new RequestNotificationService(_sender, _templates, _employees, _clock);
        }

        private static DeliveryRequest CompletedRequest(string requesterEmail = "somying@example.co.th",
                                                        string createdBy = "10002")
        {
            return new DeliveryRequest
            {
                ReqId = 1,
                ReqNo = "MSG-SDC-2608-0001",
                BranchCode = "SDC",
                BranchName = "สำนักงานสาขา SDC",
                RequesterEmpCode = "10002",
                RequesterName = "สมหญิง รักงาน",
                RequesterEmail = requesterEmail,
                ContactName = "บริษัท ตัวอย่าง จำกัด",
                Address = "123 ถนนทดสอบ",
                Detail = "ส่งเอกสารสัญญา",
                Phone = "021234567",
                SendDate = new DateTime(2026, 8, 10),
                Status = RequestStatus.Completed,
                CreatedBy = createdBy,
                JobTypes = new[]
                {
                    new RequestJobType { JobType = JobType.SendDoc, DetailText = "ซองน้ำตาล 1 ซอง" }
                },
                Assignment = new MessengerAssignment
                {
                    MessengerEmpCode = "10003",
                    MessengerName = "ประเสริฐ ว่องไว",
                    SequenceOrder = 1
                }
            };
        }

        // ==================== ผู้รับ (D27) ====================

        [Test]
        public void ส่งถึงผู้แจ้งเป็นผู้รับหลัก()
        {
            var result = _service.NotifyCompleted(CompletedRequest());

            Assert.That(result.Sent, Is.True, result.Warning);
            Assert.That(_sender.LastMessage.To, Is.EqualTo(new[] { "somying@example.co.th" }));
            Assert.That(_sender.LastMessage.Cc, Is.Empty, "แจ้งในนามตัวเองไม่ต้อง CC ใคร");
        }

        [Test]
        public void แจ้งแทนคนอื่นต้อง_CC_คนกรอกด้วย()
        {
            // D17 + D27 — คนกรอกแทนควรรู้ว่างานที่ตัวเองแจ้งให้เสร็จแล้ว
            _employees.WithEmployeeEmail("10004", "areeya@example.co.th");

            var result = _service.NotifyCompleted(CompletedRequest(createdBy: "10004"));

            Assert.That(result.Sent, Is.True, result.Warning);
            Assert.That(_sender.LastMessage.Cc, Is.EqualTo(new[] { "areeya@example.co.th" }));
        }

        [Test]
        public void ไม่_CC_ซ้ำเมื่อคนกรอกใช้อีเมลเดียวกับผู้แจ้ง()
        {
            _employees.WithEmployeeEmail("10004", "somying@example.co.th");

            _service.NotifyCompleted(CompletedRequest(createdBy: "10004"));

            Assert.That(_sender.LastMessage.Cc, Is.Empty);
        }

        [Test]
        public void คนกรอกไม่มีอีเมลก็ยังส่งถึงผู้แจ้งได้()
        {
            _employees.WithEmployeeEmail("10004", null);

            var result = _service.NotifyCompleted(CompletedRequest(createdBy: "10004"));

            Assert.That(result.Sent, Is.True, result.Warning);
            Assert.That(_sender.LastMessage.Cc, Is.Empty);
        }

        [Test]
        public void ผู้แจ้งไม่มีอีเมลต้องไม่ส่งและได้คำเตือน()
        {
            var result = _service.NotifyCompleted(CompletedRequest(requesterEmail: null));

            Assert.That(result.Sent, Is.False);
            Assert.That(result.Warning, Does.Contain("ไม่มีอีเมล"));
            Assert.That(_sender.Sent, Is.Empty);
        }

        // ==================== ส่งไม่ออก (D26) ====================

        [Test]
        public void ส่งไม่ออกต้องได้คำเตือนไม่ใช่ระเบิด()
        {
            _sender.FailWithMessage = "SMTP ปลายทางไม่ตอบ";

            var result = _service.NotifyCompleted(CompletedRequest());

            Assert.That(result.Sent, Is.False);
            Assert.That(result.Warning, Does.Contain("SMTP ปลายทางไม่ตอบ"));
            Assert.That(result.Warning, Does.Contain("ปิดงานเรียบร้อยแล้ว"));
        }

        // ==================== เนื้อความ (D28) ====================

        [Test]
        public void เนื้อเมลมีข้อมูลสำคัญของใบงานครบ()
        {
            _service.NotifyCompleted(CompletedRequest());

            var body = _sender.LastMessage.HtmlBody;

            Assert.That(body, Does.Contain("MSG-SDC-2608-0001"));
            Assert.That(body, Does.Contain("บริษัท ตัวอย่าง จำกัด"));
            Assert.That(body, Does.Contain("10/08/2026"));            // วันที่ส่ง เป็น ค.ศ. ตาม D19
            Assert.That(body, Does.Contain("ประเสริฐ ว่องไว"));        // ผู้รับงาน
            Assert.That(body, Does.Contain("ส่งเอกสาร"));             // ประเภทงาน
            Assert.That(_sender.LastMessage.Subject, Does.Contain("MSG-SDC-2608-0001"));
        }

        [Test]
        public void ค่าที่ผู้ใช้พิมพ์ต้องถูก_escape_ก่อนใส่ลง_HTML()
        {
            var request = CompletedRequest();
            request.ContactName = "<script>alert('x')</script>";

            _service.NotifyCompleted(request);

            Assert.That(_sender.LastMessage.HtmlBody, Does.Not.Contain("<script>"));
            Assert.That(_sender.LastMessage.HtmlBody, Does.Contain("&lt;script&gt;"));
        }

        [Test]
        public void ขึ้นบรรทัดใหม่ในที่อยู่ถูกแปลงเป็น_br()
        {
            var request = CompletedRequest();
            request.Address = "123 ถนนทดสอบ\nแขวงทดสอบ";

            _service.NotifyCompleted(request);

            Assert.That(_sender.LastMessage.HtmlBody, Does.Contain("123 ถนนทดสอบ<br />แขวงทดสอบ"));
        }

        [Test]
        public void ใช้_template_จากไฟล์แทนของในโค้ดได้()
        {
            _templates.With(RequestNotificationService.CompletedTemplateName,
                "Subject: งานเสร็จแล้ว {{ReqNo}}\r\n\r\n<p>เรียน {{RequesterName}} — {{ContactName}}</p>");

            _service.NotifyCompleted(CompletedRequest());

            Assert.That(_sender.LastMessage.Subject, Is.EqualTo("งานเสร็จแล้ว MSG-SDC-2608-0001"));
            Assert.That(_sender.LastMessage.HtmlBody,
                Is.EqualTo("<p>เรียน สมหญิง รักงาน — บริษัท ตัวอย่าง จำกัด</p>"));
        }

        [Test]
        public void template_ที่ไม่มีบรรทัด_Subject_ใช้หัวเรื่องมาตรฐาน()
        {
            _templates.With(RequestNotificationService.CompletedTemplateName, "<p>{{ReqNo}} เสร็จแล้ว</p>");

            _service.NotifyCompleted(CompletedRequest());

            Assert.That(_sender.LastMessage.Subject, Does.Contain("MSG-SDC-2608-0001"));
            Assert.That(_sender.LastMessage.HtmlBody, Is.EqualTo("<p>MSG-SDC-2608-0001 เสร็จแล้ว</p>"));
        }

        [Test]
        public void ตัวแปรที่ไม่มีค่าแสดงขีดแทนช่องว่าง()
        {
            var request = CompletedRequest();
            request.Assignment = null;   // ไม่มีผู้รับงาน (กรณีข้อมูลเก่า)

            _service.NotifyCompleted(request);

            Assert.That(_sender.LastMessage.HtmlBody, Does.Not.Contain("{{MessengerName}}"));
        }

        [Test]
        public void หัวเรื่องต้องไม่มีการขึ้นบรรทัดใหม่()
        {
            // กัน header injection : หัวเรื่องที่มี \n ทำให้แทรก header อื่นเข้าไปได้
            _templates.With(RequestNotificationService.CompletedTemplateName,
                "Subject: หัวเรื่อง {{ContactName}}\r\n\r\n<p>body</p>");

            var request = CompletedRequest();
            request.ContactName = "บริษัท\r\nBcc: attacker@example.com";

            _service.NotifyCompleted(request);

            Assert.That(_sender.LastMessage.Subject, Does.Not.Contain("\n"));
            Assert.That(_sender.LastMessage.Subject, Does.Not.Contain("\r"));
        }
    }
}
