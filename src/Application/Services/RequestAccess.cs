using System;
using Messenger.Application.Dtos;
using Messenger.Domain.Entities;

namespace Messenger.Application.Services
{
    /// <summary>
    /// กฎการมองเห็น/ความเป็นเจ้าของที่ service หลายตัวใช้ร่วมกัน
    ///
    /// รวมไว้ที่เดียวเพื่อให้คำว่า "ใบของฉัน" และ "สาขาเดียวกัน" มีคำนิยามเดียว
    /// ทั้งตอนแก้ใบงาน (BR-2) และตอนเปลี่ยนสถานะ (§6)
    /// </summary>
    internal static class RequestAccess
    {
        /// <summary>§5 — Admin/Messenger เห็นงานทั้งสาขา ส่วน User เห็นเฉพาะใบตัวเอง</summary>
        public static bool SeesWholeBranch(UserContext user)
        {
            return user.IsAdmin || user.IsMessenger;
        }

        /// <summary>
        /// เจ้าของใบงาน = "ผู้แจ้ง" ไม่ใช่คนที่กดสร้าง
        /// (D17 — เมื่อแจ้งแทนคนอื่น สิทธิ์แก้/ยกเลิกเป็นของผู้แจ้ง)
        /// </summary>
        public static bool IsOwner(DeliveryRequest request, UserContext user)
        {
            return string.Equals(request.RequesterEmpCode, user.EmpCode, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>BranchCode เป็น CHAR(3) จึงต้องตัดช่องว่างก่อนเทียบเสมอ</summary>
        public static bool SameBranch(string left, string right)
        {
            return string.Equals(Trim(left), Trim(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string Trim(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
