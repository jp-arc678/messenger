namespace Messenger.Domain.Entities
{
    /// <summary>
    /// สาขา — หน่วยแยกข้อมูล (isolation) ของทั้งระบบ ตาม BR-6
    /// ปัจจุบันมี 2 สาขาคือ SDC และ SBK ใช้ database ร่วมกัน
    /// </summary>
    public class Branch
    {
        public string BranchCode { get; set; }

        public string BranchName { get; set; }

        public bool IsActive { get; set; }
    }
}
