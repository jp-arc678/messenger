using System.Data;

namespace Messenger.Infrastructure.Data
{
    /// <summary>
    /// สร้าง connection ไปยัง MessengerDb
    /// แยกเป็น interface เพื่อให้ repository ทดสอบ/สลับ implementation ได้
    /// </summary>
    public interface IDbConnectionFactory
    {
        /// <summary>คืน connection ที่ "ยังไม่เปิด" — ผู้เรียกรับผิดชอบ using/dispose เอง</summary>
        IDbConnection CreateConnection();
    }
}
