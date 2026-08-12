using System.Collections.Generic;
using Messenger.Application.Dtos;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.Application.Services
{
    /// <summary>ข้อมูลรูป 1 ใบที่ส่งมาจากฟอร์ม</summary>
    public class UploadPhotoCommand
    {
        public int ReqId { get; set; }

        public PhotoType PhotoType { get; set; }

        public string FileName { get; set; }

        public byte[] Content { get; set; }
    }

    /// <summary>ไฟล์รูปที่พร้อมส่งกลับไปแสดงบนหน้าจอ</summary>
    public class PhotoContent
    {
        public byte[] Content { get; set; }

        public string ContentType { get; set; }

        public string FileName { get; set; }
    }

    /// <summary>
    /// รูปยืนยันของใบแจ้งงาน (BR-3)
    ///
    /// กฎทั้งหมด (ใครอัปได้ · สถานะไหน · ไฟล์แบบไหน · ใหญ่แค่ไหน) อยู่ที่นี่
    /// controller มีหน้าที่แค่แปลงไฟล์ที่อัปเข้ามาเป็น command
    /// </summary>
    public interface IPhotoService
    {
        ServiceResult<DeliveryPhoto> Upload(UploadPhotoCommand command, UserContext user);

        /// <summary>รูปทั้งหมดของใบงาน — คนที่ดูใบงานไม่ได้ก็ดูรูปไม่ได้</summary>
        ServiceResult<IReadOnlyList<DeliveryPhoto>> List(int reqId, UserContext user);

        /// <summary>อ่านไฟล์รูปเพื่อส่งออกหน้าจอ (ตรวจสิทธิ์เหมือน <see cref="List"/>)</summary>
        ServiceResult<PhotoContent> GetContent(int photoId, UserContext user);

        ServiceResult<bool> Delete(int photoId, UserContext user);

        /// <summary>ผู้ใช้คนนี้อัปโหลด/ลบรูปของใบงานนี้ได้หรือไม่ (ใช้ตัดสินว่าจะโชว์ฟอร์มไหม)</summary>
        bool CanManagePhotos(DeliveryRequest request, UserContext user);
    }
}
