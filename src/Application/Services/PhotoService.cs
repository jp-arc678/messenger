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
    /// รูปยืนยันของใบแจ้งงาน (Phase 3)
    ///
    /// กฎที่บังคับในคลาสนี้ :
    /// - BR-3  รูปเก็บบน filesystem (DB เก็บแค่ path) · ขนาดไม่เกิน 2MB
    ///         ฝั่ง client ย่อรูปมาให้แล้ว แต่ server ต้องตรวจซ้ำเสมอ
    ///         เพราะการย่อฝั่ง client เลี่ยงได้ด้วยการยิง request ตรง
    /// - BR-6  อ่าน/เขียนด้วย branchCode ของผู้ใช้เท่านั้น
    /// - §5    อัปโหลด/ลบได้เฉพาะ Messenger/Admin ส่วนการ "ดู" ใช้สิทธิ์เดียวกับการดูใบงาน
    /// - D23   อัปโหลด/ลบได้เฉพาะตอนใบงานอยู่สถานะ Delivering หรือ Paused
    /// - D24   ลบได้ก่อนปิดงานเท่านั้น (เงื่อนไขเดียวกับ D23) และลบไฟล์จริงบนดิสก์ด้วย
    ///
    /// หมายเหตุ BR-4/D9 : รูปเป็น optional เสมอ ไม่ใช่เงื่อนไขของการปิดงาน
    /// เงื่อนไขปิดงานคือการกดยืนยันรับของ ซึ่งอยู่ใน RequestWorkflowService
    /// </summary>
    public class PhotoService : IPhotoService
    {
        /// <summary>ขนาดสูงสุดต่อรูป (BR-3)</summary>
        public const int MaxFileSizeBytes = 2 * 1024 * 1024;

        /// <summary>กันการอัปรัว ๆ จนใบงานเดียวมีรูปเป็นร้อย</summary>
        public const int MaxPhotosPerRequest = 20;

        private readonly IDeliveryPhotoRepository _photos;
        private readonly IDeliveryRequestRepository _requests;
        private readonly IPhotoFileStorage _storage;
        private readonly IClock _clock;

        public PhotoService(IDeliveryPhotoRepository photos,
                            IDeliveryRequestRepository requests,
                            IPhotoFileStorage storage,
                            IClock clock)
        {
            _photos = photos ?? throw new ArgumentNullException(nameof(photos));
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public ServiceResult<DeliveryPhoto> Upload(UploadPhotoCommand command, UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            if (command == null)
                return ServiceResult<DeliveryPhoto>.Fail("ไม่มีข้อมูลรูปที่จะอัปโหลด");

            var request = _requests.GetById(command.ReqId, user.BranchCode);
            if (request == null)
                return ServiceResult<DeliveryPhoto>.Fail("ไม่พบใบแจ้งงานนี้ในสาขาของคุณ");

            var permission = CheckCanManage(request, user);
            if (permission != null)
                return ServiceResult<DeliveryPhoto>.Fail(permission);

            if (!Enum.IsDefined(typeof(PhotoType), command.PhotoType))
                return ServiceResult<DeliveryPhoto>.Fail("กรุณาเลือกประเภทรูป");

            if (command.Content == null || command.Content.Length == 0)
                return ServiceResult<DeliveryPhoto>.Fail("ไม่พบไฟล์รูป กรุณาเลือกไฟล์ก่อน");

            if (command.Content.Length > MaxFileSizeBytes)
            {
                return ServiceResult<DeliveryPhoto>.Fail(
                    $"ไฟล์ใหญ่เกิน {MaxFileSizeBytes / 1024 / 1024} MB ({command.Content.Length / 1024} KB) " +
                    "กรุณาถ่ายใหม่หรือย่อรูปก่อนอัปโหลด");
            }

            // ตรวจจาก "ไส้ในของไฟล์" ไม่ใช่จากนามสกุลหรือ content-type ที่ผู้ใช้ส่งมา
            var extension = DetectImageExtension(command.Content);
            if (extension == null)
                return ServiceResult<DeliveryPhoto>.Fail("รองรับเฉพาะไฟล์รูปแบบ JPG และ PNG เท่านั้น");

            if (_photos.CountByRequest(command.ReqId, user.BranchCode) >= MaxPhotosPerRequest)
            {
                return ServiceResult<DeliveryPhoto>.Fail(
                    $"ใบแจ้งงานนี้มีรูปครบ {MaxPhotosPerRequest} รูปแล้ว กรุณาลบรูปที่ไม่ใช้ก่อน");
            }

            var filePath = _storage.Save(command.Content, extension, request.BranchCode, request.ReqNo);
            if (string.IsNullOrWhiteSpace(filePath))
                return ServiceResult<DeliveryPhoto>.Fail("บันทึกไฟล์รูปไม่สำเร็จ");

            var photoId = _photos.Add(new AddPhotoData
            {
                ReqId = command.ReqId,
                BranchCode = user.BranchCode,
                PhotoType = command.PhotoType,
                FilePath = filePath,
                FileName = CleanFileName(command.FileName),
                FileSizeBytes = command.Content.Length,
                CapturedAt = _clock.Now,
                CapturedBy = user.EmpCode
            });

            if (photoId == 0)
            {
                // บันทึกลง DB ไม่สำเร็จ อย่าปล่อยไฟล์กำพร้าทิ้งไว้บนดิสก์
                _storage.Delete(filePath);
                return ServiceResult<DeliveryPhoto>.Fail("บันทึกข้อมูลรูปไม่สำเร็จ");
            }

            var saved = _photos.GetById(photoId, user.BranchCode);
            return ServiceResult<DeliveryPhoto>.Ok(saved);
        }

        public ServiceResult<IReadOnlyList<DeliveryPhoto>> List(int reqId, UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var request = _requests.GetById(reqId, user.BranchCode);
            if (request == null)
                return ServiceResult<IReadOnlyList<DeliveryPhoto>>.Fail("ไม่พบใบแจ้งงานนี้ในสาขาของคุณ");

            if (!CanView(request, user))
                return ServiceResult<IReadOnlyList<DeliveryPhoto>>.Fail("คุณไม่มีสิทธิ์ดูใบแจ้งงานนี้");

            return ServiceResult<IReadOnlyList<DeliveryPhoto>>.Ok(_photos.ListByRequest(reqId, user.BranchCode));
        }

        public ServiceResult<PhotoContent> GetContent(int photoId, UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var photo = _photos.GetById(photoId, user.BranchCode);
            if (photo == null)
                return ServiceResult<PhotoContent>.Fail("ไม่พบรูปนี้ในสาขาของคุณ");

            var request = _requests.GetById(photo.ReqId, user.BranchCode);
            if (request == null || !CanView(request, user))
                return ServiceResult<PhotoContent>.Fail("คุณไม่มีสิทธิ์ดูรูปของใบแจ้งงานนี้");

            var content = _storage.Read(photo.FilePath);
            if (content == null)
                return ServiceResult<PhotoContent>.Fail("ไฟล์รูปหายไปจากที่เก็บไฟล์");

            return ServiceResult<PhotoContent>.Ok(new PhotoContent
            {
                Content = content,
                ContentType = ContentTypeOf(photo.FilePath),
                FileName = photo.FileName
            });
        }

        public ServiceResult<bool> Delete(int photoId, UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var photo = _photos.GetById(photoId, user.BranchCode);
            if (photo == null)
                return ServiceResult<bool>.Fail("ไม่พบรูปนี้ในสาขาของคุณ");

            var request = _requests.GetById(photo.ReqId, user.BranchCode);
            if (request == null)
                return ServiceResult<bool>.Fail("ไม่พบใบแจ้งงานของรูปนี้");

            var permission = CheckCanManage(request, user);
            if (permission != null)
                return ServiceResult<bool>.Fail(permission);

            if (!_photos.Delete(photoId, user.BranchCode))
                return ServiceResult<bool>.Fail("ลบรูปไม่สำเร็จ อาจถูกลบไปแล้ว");

            // ลบแถวใน DB ก่อนแล้วค่อยลบไฟล์ : ถ้าลบไฟล์พลาด จะเหลือแค่ไฟล์กำพร้า
            // ซึ่งไม่มีใครเห็น ดีกว่ากรณีกลับกันที่หน้าจอจะโชว์รูปที่เปิดไม่ได้
            _storage.Delete(photo.FilePath);

            return ServiceResult<bool>.Ok(true);
        }

        public bool CanManagePhotos(DeliveryRequest request, UserContext user)
        {
            return request != null && user != null && CheckCanManage(request, user) == null;
        }

        // ---------------- ภายใน ----------------

        /// <summary>คืน null = ทำได้ · คืนข้อความ = ทำไม่ได้พร้อมเหตุผล</summary>
        private static string CheckCanManage(DeliveryRequest request, UserContext user)
        {
            if (!RequestAccess.SameBranch(request.BranchCode, user.BranchCode))
                return "ไม่พบใบแจ้งงานนี้ในสาขาของคุณ";

            // §5 — ผู้แจ้งไม่มีสิทธิ์แตะรูป แม้จะเป็นใบของตัวเอง
            if (!RequestAccess.SeesWholeBranch(user))
                return "การจัดการรูปยืนยันเป็นสิทธิ์ของเจ้าหน้าที่ Messenger และผู้ดูแลระบบเท่านั้น";

            // D23/D24 — รูปเป็นหลักฐานระหว่างเดินงาน จึงแก้ไขได้เฉพาะช่วงที่งานยังเดินอยู่
            if (request.Status != RequestStatus.Delivering && request.Status != RequestStatus.Paused)
            {
                return $"จัดการรูปได้เฉพาะตอนใบงานอยู่ในสถานะ \"กำลังส่ง\" หรือ \"พักการส่ง\" " +
                       $"(ตอนนี้อยู่สถานะ \"{request.StatusDisplayName}\")";
            }

            return null;
        }

        private static bool CanView(DeliveryRequest request, UserContext user)
        {
            return RequestAccess.SeesWholeBranch(user) || RequestAccess.IsOwner(request, user);
        }

        /// <summary>
        /// ดูเลข magic ต้นไฟล์เพื่อยืนยันว่าเป็นรูปจริง
        /// คืนนามสกุลที่ควรใช้ หรือ null ถ้าไม่ใช่ JPG/PNG
        /// </summary>
        private static string DetectImageExtension(byte[] content)
        {
            if (content.Length >= 3 &&
                content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
            {
                return ".jpg";
            }

            if (content.Length >= 8 &&
                content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47 &&
                content[4] == 0x0D && content[5] == 0x0A && content[6] == 0x1A && content[7] == 0x0A)
            {
                return ".png";
            }

            return null;
        }

        private static string ContentTypeOf(string filePath)
        {
            return filePath != null && filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : "image/jpeg";
        }

        /// <summary>
        /// เก็บชื่อไฟล์เดิมไว้แค่เพื่ออ้างอิง จึงตัดส่วนที่เป็น path ทิ้งทั้งหมด
        /// (ชื่อจริงบนดิสก์ตั้งโดย IPhotoFileStorage ไม่ใช่ผู้ใช้)
        /// </summary>
        private static string CleanFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            var name = fileName.Split('\\', '/').Last().Trim();
            return name.Length > 255 ? name.Substring(name.Length - 255) : name;
        }
    }
}
