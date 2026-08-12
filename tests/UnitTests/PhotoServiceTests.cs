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
    /// Phase 3 — รูปยืนยัน (BR-3) และกฎการจัดการรูป D23–D25
    ///
    /// เวลาอ้างอิงคือ จันทร์ที่ 10 ส.ค. 2026 เวลา 09:00 เหมือนชุดอื่น
    /// </summary>
    [TestFixture]
    public class PhotoServiceTests
    {
        private static readonly DateTime MondayMorning = new DateTime(2026, 8, 10, 9, 0, 0);

        private FakeDeliveryRequestRepository _requests;
        private FakeDeliveryPhotoRepository _photoRepository;
        private FakePhotoFileStorage _storage;
        private FakeEmployeeRepository _employees;
        private FakeClock _clock;
        private DeliveryRequestService _requestService;
        private RequestWorkflowService _workflow;
        private PhotoService _photos;

        [SetUp]
        public void SetUp()
        {
            _requests = new FakeDeliveryRequestRepository();
            _photoRepository = new FakeDeliveryPhotoRepository(_requests);
            _storage = new FakePhotoFileStorage();
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
            _photos = new PhotoService(_photoRepository, _requests, _storage, _clock);
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

        /// <summary>ไฟล์ JPEG ปลอมที่มี magic number ถูกต้อง</summary>
        private static byte[] JpegBytes(int size = 1024)
        {
            var content = new byte[Math.Max(size, 3)];
            content[0] = 0xFF;
            content[1] = 0xD8;
            content[2] = 0xFF;
            return content;
        }

        private static byte[] PngBytes()
        {
            return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        }

        private int NewRequest(string requesterEmpCode = "10002", string branchCode = "SDC",
                               params JobType[] jobTypes)
        {
            var types = jobTypes.Length > 0 ? jobTypes : new[] { JobType.SendDoc };

            var result = _requestService.Create(new CreateRequestCommand
            {
                ContactName = "บริษัท ตัวอย่าง จำกัด",
                Address = "123 ถนนทดสอบ",
                Detail = "ส่งเอกสารสัญญา",
                JobTypes = types.Select(t => new JobTypeInput { JobType = t }).ToList()
            }, User(requesterEmpCode, branchCode));

            Assert.That(result.Success, Is.True, result.FirstError);
            return result.Value.ReqId;
        }

        /// <summary>ใบงานที่ยืนยันรับงานแล้ว = สถานะที่อัปโหลดรูปได้ (D23)</summary>
        private int DeliveringRequest(string branchCode = "SDC", params JobType[] jobTypes)
        {
            var reqId = NewRequest(branchCode == "SDC" ? "10002" : "20002", branchCode, jobTypes);
            var confirmed = _workflow.Apply(reqId, RequestAction.Confirm, null, Messenger(branchCode));
            Assert.That(confirmed.Success, Is.True, confirmed.FirstError);
            return reqId;
        }

        private ServiceResult<Messenger.Domain.Entities.DeliveryPhoto> Upload(
            int reqId, UserContext user, PhotoType type = PhotoType.Send, byte[] content = null)
        {
            return _photos.Upload(new UploadPhotoCommand
            {
                ReqId = reqId,
                PhotoType = type,
                FileName = "IMG_0001.jpg",
                Content = content ?? JpegBytes()
            }, user);
        }

        // ==================== อัปโหลด ====================

        [Test]
        public void Messenger_อัปโหลดรูปตอนกำลังส่งได้()
        {
            var reqId = DeliveringRequest();

            var result = Upload(reqId, Messenger());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(result.Value.PhotoType, Is.EqualTo(PhotoType.Send));
            Assert.That(result.Value.CapturedBy, Is.EqualTo("10003"));
            Assert.That(result.Value.CapturedAt, Is.EqualTo(MondayMorning));
            Assert.That(_storage.StoredPaths.Count, Is.EqualTo(1));
        }

        [Test]
        public void Admin_อัปโหลดรูปได้เช่นกัน()
        {
            var reqId = DeliveringRequest();

            Assert.That(Upload(reqId, Admin()).Success, Is.True);
        }

        [Test]
        public void User_อัปโหลดรูปไม่ได้แม้เป็นใบของตัวเอง()
        {
            // §5 — อัปโหลดรูปเป็นสิทธิ์ของ Messenger/Admin เท่านั้น
            var reqId = DeliveringRequest();

            var result = Upload(reqId, User("10002"));

            Assert.That(result.Success, Is.False);
            Assert.That(_storage.SaveCount, Is.EqualTo(0), "ต้องไม่เขียนไฟล์ลงที่เก็บเลย");
        }

        [Test]
        public void อัปโหลดรูปตอนสถานะรับแจ้งยังไม่ได้()
        {
            // D23 — รูปเป็นหลักฐานระหว่างเดินงาน จึงเริ่มอัปได้เมื่อยืนยันรับงานแล้ว
            var reqId = NewRequest();

            var result = Upload(reqId, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("กำลังส่ง"));
        }

        [Test]
        public void อัปโหลดรูปตอนสถานะพักได้()
        {
            var reqId = DeliveringRequest();
            _workflow.Apply(reqId, RequestAction.Pause, "ฝนตกหนัก", Messenger());

            Assert.That(Upload(reqId, Messenger()).Success, Is.True);
        }

        [Test]
        public void ปิดงานแล้วอัปโหลดรูปเพิ่มไม่ได้()
        {
            var reqId = DeliveringRequest();
            _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());

            var result = Upload(reqId, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("เสร็จงานแล้ว"));
        }

        [Test]
        public void อัปโหลดรูปข้ามสาขาไม่ได้()
        {
            // BR-6
            var reqId = DeliveringRequest("SDC");

            var result = Upload(reqId, Messenger("SBK"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ไม่พบใบแจ้งงาน"));
        }

        [Test]
        public void ไฟล์ใหญ่เกิน_2MB_ต้องไม่ผ่าน()
        {
            // BR-3 — client ย่อมาให้แล้ว แต่ server ต้องกันซ้ำเพราะยิงตรงข้ามได้
            var reqId = DeliveringRequest();

            var result = Upload(reqId, Messenger(), PhotoType.Send, JpegBytes(PhotoService.MaxFileSizeBytes + 1));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("ใหญ่เกิน"));
            Assert.That(_storage.SaveCount, Is.EqualTo(0));
        }

        [Test]
        public void ไฟล์ขนาดพอดี_2MB_ยังผ่าน()
        {
            var reqId = DeliveringRequest();

            var result = Upload(reqId, Messenger(), PhotoType.Send, JpegBytes(PhotoService.MaxFileSizeBytes));

            Assert.That(result.Success, Is.True, result.FirstError);
        }

        [Test]
        public void ไฟล์ที่ไม่ใช่รูปต้องไม่ผ่านแม้ตั้งชื่อเป็น_jpg()
        {
            var reqId = DeliveringRequest();
            var notAnImage = new byte[] { 0x4D, 0x5A, 0x90, 0x00 };   // ไฟล์ .exe

            var result = _photos.Upload(new UploadPhotoCommand
            {
                ReqId = reqId,
                PhotoType = PhotoType.Send,
                FileName = "virus.jpg",
                Content = notAnImage
            }, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("JPG"));
        }

        [Test]
        public void รองรับไฟล์_PNG_ด้วย()
        {
            var reqId = DeliveringRequest();

            Assert.That(Upload(reqId, Messenger(), PhotoType.Receive, PngBytes()).Success, Is.True);
        }

        [Test]
        public void ไฟล์ว่างเปล่าต้องไม่ผ่าน()
        {
            var reqId = DeliveringRequest();

            var result = Upload(reqId, Messenger(), PhotoType.Send, new byte[0]);

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void อัปโหลดเกินจำนวนสูงสุดต่อใบไม่ได้()
        {
            var reqId = DeliveringRequest();

            for (var i = 0; i < PhotoService.MaxPhotosPerRequest; i++)
                Assert.That(Upload(reqId, Messenger()).Success, Is.True);

            var overflow = Upload(reqId, Messenger());

            Assert.That(overflow.Success, Is.False);
            Assert.That(overflow.FirstError, Does.Contain("ครบ"));
        }

        [Test]
        public void เขียนไฟล์ไม่สำเร็จต้องไม่มีข้อมูลรูปค้างใน_DB()
        {
            var reqId = DeliveringRequest();
            _storage.FailOnSave = true;

            var result = Upload(reqId, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(_photos.List(reqId, Messenger()).Value, Is.Empty);
        }

        // ==================== ดูรูป ====================

        [Test]
        public void เจ้าของใบงานดูรูปได้แม้อัปโหลดเองไม่ได้()
        {
            var reqId = DeliveringRequest();
            var photo = Upload(reqId, Messenger()).Value;

            var list = _photos.List(reqId, User("10002"));
            var content = _photos.GetContent(photo.PhotoId, User("10002"));

            Assert.That(list.Success, Is.True);
            Assert.That(list.Value.Count, Is.EqualTo(1));
            Assert.That(content.Success, Is.True);
            Assert.That(content.Value.ContentType, Is.EqualTo("image/jpeg"));
        }

        [Test]
        public void User_ที่ไม่ใช่เจ้าของดูรูปไม่ได้()
        {
            var reqId = DeliveringRequest();
            var photo = Upload(reqId, Messenger()).Value;

            Assert.That(_photos.List(reqId, User("10004")).Success, Is.False);
            Assert.That(_photos.GetContent(photo.PhotoId, User("10004")).Success, Is.False);
        }

        [Test]
        public void ดูรูปข้ามสาขาไม่ได้()
        {
            var reqId = DeliveringRequest("SDC");
            var photo = Upload(reqId, Messenger()).Value;

            var result = _photos.GetContent(photo.PhotoId, Messenger("SBK"));

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void ไฟล์หายจากดิสก์ต้องบอกให้รู้()
        {
            var reqId = DeliveringRequest();
            var photo = Upload(reqId, Messenger()).Value;

            _storage.Delete(photo.FilePath);

            var result = _photos.GetContent(photo.PhotoId, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(result.FirstError, Does.Contain("หายไป"));
        }

        // ==================== ลบรูป (D24) ====================

        [Test]
        public void ลบรูปก่อนปิดงานได้_และไฟล์ถูกลบจริง()
        {
            var reqId = DeliveringRequest();
            var photo = Upload(reqId, Messenger()).Value;

            var result = _photos.Delete(photo.PhotoId, Messenger());

            Assert.That(result.Success, Is.True, result.FirstError);
            Assert.That(_photos.List(reqId, Messenger()).Value, Is.Empty);
            Assert.That(_storage.StoredPaths, Is.Empty, "ไฟล์ต้องถูกลบจากที่เก็บด้วย");
        }

        [Test]
        public void ปิดงานแล้วลบรูปไม่ได้()
        {
            var reqId = DeliveringRequest();
            var photo = Upload(reqId, Messenger()).Value;
            _workflow.Apply(reqId, RequestAction.Complete, null, Messenger());

            var result = _photos.Delete(photo.PhotoId, Messenger());

            Assert.That(result.Success, Is.False);
            Assert.That(_photos.List(reqId, Messenger()).Value.Count, Is.EqualTo(1));
        }

        [Test]
        public void User_ลบรูปไม่ได้()
        {
            var reqId = DeliveringRequest();
            var photo = Upload(reqId, Messenger()).Value;

            Assert.That(_photos.Delete(photo.PhotoId, User("10002")).Success, Is.False);
        }

        [Test]
        public void ลบรูปข้ามสาขาไม่ได้()
        {
            var reqId = DeliveringRequest("SDC");
            var photo = Upload(reqId, Messenger()).Value;

            var result = _photos.Delete(photo.PhotoId, Messenger("SBK"));

            Assert.That(result.Success, Is.False);
            Assert.That(_storage.StoredPaths.Count, Is.EqualTo(1));
        }

        // ==================== ปุ่มบนหน้าจอ ====================

        [Test]
        public void ฟอร์มอัปโหลดโผล่เฉพาะคนที่มีสิทธิ์จริง()
        {
            var reqId = DeliveringRequest();
            var request = _requests.Peek(reqId);

            Assert.That(_photos.CanManagePhotos(request, Messenger()), Is.True);
            Assert.That(_photos.CanManagePhotos(request, Admin()), Is.True);
            Assert.That(_photos.CanManagePhotos(request, User("10002")), Is.False);
            Assert.That(_photos.CanManagePhotos(request, Messenger("SBK")), Is.False);
        }
    }
}
