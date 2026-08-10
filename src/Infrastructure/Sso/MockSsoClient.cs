using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Application.Abstractions;
using Messenger.Application.Dtos;

namespace Messenger.Infrastructure.Sso
{
    /// <summary>
    /// SSO แบบจำลองสำหรับ Phase 0 (D3 — ยังไม่ทราบ contract จริง)
    ///
    /// จงใจเก็บรายชื่อไว้ในหน่วยความจำ ไม่ได้อ่านจากตาราง Employee
    /// เพราะ SSO คือ "ระบบภายนอก" ที่เป็นเจ้าของข้อมูลพนักงาน
    /// ส่วนตาราง Employee เป็นแค่ cache ฝั่งเรา (BR-7)
    ///
    /// เมื่อได้ contract จริงแล้ว ให้เขียน SsoClient ตัวใหม่มาแทนคลาสนี้
    /// โดยไม่ต้องแก้ AuthService
    /// </summary>
    public class MockSsoClient : ISsoClient
    {
        private static readonly IReadOnlyList<SsoUserInfo> KnownUsers = new List<SsoUserInfo>
        {
            // ----- สาขา SDC -----
            New("10001", "สมชาย ใจดี",      "IT",  "ฝ่ายเทคโนโลยีสารสนเทศ", "1101", "somchai@example.co.th",  "SDC"),
            New("10002", "สมหญิง รักงาน",   "ACC", "ฝ่ายบัญชีและการเงิน",   "1201", "somying@example.co.th",  "SDC"),
            New("10003", "ประเสริฐ ว่องไว", "ADM", "ฝ่ายธุรการ",            "1301", "prasert@example.co.th",  "SDC"),
            New("10004", "อารีย์ พากเพียร", "HR",  "ฝ่ายทรัพยากรบุคคล",     "1401", "aree@example.co.th",     "SDC"),

            // ----- สาขา SBK -----
            New("20001", "วิชัย มั่นคง",     "IT",  "ฝ่ายเทคโนโลยีสารสนเทศ", "2101", "wichai@example.co.th",   "SBK"),
            New("20002", "นภา สดใส",        "ACC", "ฝ่ายบัญชีและการเงิน",   "2201", "napa@example.co.th",     "SBK"),
            New("20003", "ธนา เร็วรี่",      "ADM", "ฝ่ายธุรการ",            "2301", "thana@example.co.th",    "SBK"),
            New("20004", "ชูใจ ตั้งใจ",      "HR",  "ฝ่ายทรัพยากรบุคคล",     "2401", "chujai@example.co.th",   "SBK"),

            // ----- พนักงานที่ "ยังไม่มี" ในตาราง Employee -----
            // ใช้พิสูจน์ BR-7 (cache อัตโนมัติตอน login ครั้งแรก)
            // และ D10 (คนใหม่ต้องได้ role U-User ทันที)
            New("10099", "พนักงานใหม่ สาขา SDC", "OPS", "ฝ่ายปฏิบัติการ", "1999", "newbie.sdc@example.co.th", "SDC"),
            New("20099", "พนักงานใหม่ สาขา SBK", "OPS", "ฝ่ายปฏิบัติการ", "2999", "newbie.sbk@example.co.th", "SBK")
        };

        public SsoUserInfo GetUserInfo(string empCode)
        {
            if (string.IsNullOrWhiteSpace(empCode))
                return null;

            var code = empCode.Trim();
            return KnownUsers.FirstOrDefault(u => string.Equals(u.EmpCode, code, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<SsoUserInfo> ListKnownUsers()
        {
            return KnownUsers;
        }

        private static SsoUserInfo New(string empCode, string fullName, string deptCode,
                                       string unitName, string phoneExt, string email, string branchCode)
        {
            return new SsoUserInfo
            {
                EmpCode = empCode,
                FullName = fullName,
                DeptCode = deptCode,
                UnitName = unitName,
                PhoneExt = phoneExt,
                Email = email,
                BranchCode = branchCode
            };
        }
    }
}
