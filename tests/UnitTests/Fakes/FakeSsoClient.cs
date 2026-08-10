using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Application.Abstractions;
using Messenger.Application.Dtos;

namespace Messenger.UnitTests.Fakes
{
    /// <summary>SSO ปลอมที่ควบคุมรายชื่อได้จากในเทสต์</summary>
    public class FakeSsoClient : ISsoClient
    {
        private readonly List<SsoUserInfo> _users = new List<SsoUserInfo>();

        public FakeSsoClient Add(string empCode, string branchCode, string fullName = "ทดสอบ ระบบ")
        {
            _users.Add(new SsoUserInfo
            {
                EmpCode = empCode,
                FullName = fullName,
                DeptCode = "TST",
                UnitName = "ฝ่ายทดสอบ",
                PhoneExt = "9999",
                Email = empCode + "@example.co.th",
                BranchCode = branchCode
            });

            return this;
        }

        public SsoUserInfo GetUserInfo(string empCode)
        {
            return _users.FirstOrDefault(u => string.Equals(u.EmpCode, empCode, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<SsoUserInfo> ListKnownUsers()
        {
            return _users;
        }
    }
}
