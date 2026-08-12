namespace Messenger.Application.Abstractions
{
    /// <summary>
    /// ที่เก็บไฟล์รูปจริง (BR-3 — เก็บบน filesystem ไม่เก็บ binary ใน DB)
    ///
    /// service layer รู้จักแค่ interface นี้ จึงทดสอบกฎการอัปโหลดได้โดยไม่ต้องแตะดิสก์จริง
    /// path ที่รับ-ส่งเป็นแบบ "สัมพัทธ์กับโฟลเดอร์ราก" เสมอ (D25)
    /// </summary>
    public interface IPhotoFileStorage
    {
        /// <summary>
        /// เขียนไฟล์ลงที่เก็บแล้วคืน path สัมพัทธ์ที่จะบันทึกลง DB
        /// implementation เป็นคนตั้งชื่อไฟล์จริงเอง เพื่อไม่ให้ชื่อจากผู้ใช้
        /// ไปกำหนดตำแหน่งไฟล์บนดิสก์ได้ (กัน path traversal)
        /// </summary>
        string Save(byte[] content, string extension, string branchCode, string reqNo);

        /// <summary>คืน null ถ้าไฟล์หายไปจากดิสก์ (ข้อมูลใน DB อาจค้างอยู่)</summary>
        byte[] Read(string relativePath);

        /// <summary>คืน false ถ้าไม่มีไฟล์นั้นอยู่แล้ว</summary>
        bool Delete(string relativePath);
    }
}
