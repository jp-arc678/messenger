using System;
using System.IO;
using System.Web;
using System.Web.Mvc;
using Messenger.Application.Services;
using Messenger.Domain.Enums;

namespace Messenger.Web.Controllers
{
    /// <summary>
    /// รูปยืนยันของใบแจ้งงาน (BR-3)
    ///
    /// ไฟล์รูปถูกเก็บนอก web root จึงเปิดตรงผ่าน URL ไม่ได้ ต้องผ่าน
    /// <see cref="Show"/> ซึ่งให้ service ตรวจสาขาและสิทธิ์ก่อนทุกครั้ง (BR-6 + D25)
    /// </summary>
    public class PhotosController : BaseController
    {
        private readonly IPhotoService _photos;

        public PhotosController(IPhotoService photos)
        {
            _photos = photos ?? throw new ArgumentNullException(nameof(photos));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Upload(int reqId, string photoType, HttpPostedFileBase file)
        {
            var type = PhotoTypes.TryParse(photoType);
            if (type == null)
            {
                TempData["Error"] = "กรุณาเลือกประเภทรูป (รูปตอนส่ง หรือ รูปตอนรับ)";
                return RedirectToDetails(reqId);
            }

            if (file == null || file.ContentLength <= 0)
            {
                TempData["Error"] = "ไม่พบไฟล์รูป กรุณาเลือกไฟล์ก่อน";
                return RedirectToDetails(reqId);
            }

            var result = _photos.Upload(new UploadPhotoCommand
            {
                ReqId = reqId,
                PhotoType = type.Value,
                FileName = file.FileName,
                Content = ReadAll(file)
            }, CurrentUser);

            if (result.Success)
                TempData["Message"] = $"อัปโหลด{result.Value.PhotoTypeDisplayName}เรียบร้อยแล้ว";
            else
                TempData["Error"] = string.Join(" · ", result.Errors);

            return RedirectToDetails(reqId);
        }

        [HttpGet]
        public ActionResult Show(int id)
        {
            var result = _photos.GetContent(id, CurrentUser);
            if (!result.Success)
                return new HttpStatusCodeResult(404, "ไม่พบรูป");

            return File(result.Value.Content, result.Value.ContentType);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, int reqId)
        {
            var result = _photos.Delete(id, CurrentUser);

            if (result.Success)
                TempData["Message"] = "ลบรูปเรียบร้อยแล้ว";
            else
                TempData["Error"] = string.Join(" · ", result.Errors);

            return RedirectToDetails(reqId);
        }

        // ---------------- helpers ----------------

        private ActionResult RedirectToDetails(int reqId)
        {
            return RedirectToAction("Details", "Requests", new { id = reqId });
        }

        /// <summary>อ่านไฟล์ทั้งก้อนขึ้นหน่วยความจำ — ปลอดภัยเพราะจำกัดไว้ที่ 2MB (BR-3)</summary>
        private static byte[] ReadAll(HttpPostedFileBase file)
        {
            using (var buffer = new MemoryStream())
            {
                file.InputStream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }
    }
}
