using System;
using System.Collections.Generic;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;

namespace Messenger.Application.Abstractions
{
    /// <summary>ข้อมูลรูป 1 ใบที่จะบันทึกลง DB (ตัวไฟล์ถูกเขียนลง filesystem ไปแล้ว)</summary>
    public class AddPhotoData
    {
        public int ReqId { get; set; }

        public string BranchCode { get; set; }

        public PhotoType PhotoType { get; set; }

        /// <summary>path แบบสัมพัทธ์กับโฟลเดอร์รากของที่เก็บรูป</summary>
        public string FilePath { get; set; }

        public string FileName { get; set; }

        public int FileSizeBytes { get; set; }

        public DateTime CapturedAt { get; set; }

        public string CapturedBy { get; set; }
    }

    /// <summary>
    /// รูปยืนยันของใบแจ้งงาน — เก็บเฉพาะ metadata + path (BR-3)
    ///
    /// ทุก method รับ <c>branchCode</c> และต้องใช้เป็นเงื่อนไขใน SQL เสมอ (BR-6)
    /// รูปของใบงานสาขาอื่นต้องมองไม่เห็นและลบไม่ได้
    /// </summary>
    public interface IDeliveryPhotoRepository
    {
        /// <summary>คืน 0 ถ้าใบงานไม่ได้อยู่ในสาขานี้</summary>
        int Add(AddPhotoData data);

        IReadOnlyList<DeliveryPhoto> ListByRequest(int reqId, string branchCode);

        /// <summary>คืน null ถ้าไม่พบรูป หรือรูปอยู่กับใบงานของสาขาอื่น</summary>
        DeliveryPhoto GetById(int photoId, string branchCode);

        int CountByRequest(int reqId, string branchCode);

        bool Delete(int photoId, string branchCode);
    }
}
