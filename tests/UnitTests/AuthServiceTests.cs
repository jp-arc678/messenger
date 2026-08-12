using Messenger.Application.Services;
using Messenger.Domain.Enums;
using Messenger.UnitTests.Fakes;
using NUnit.Framework;

namespace Messenger.UnitTests
{
    /// <summary>
    /// Phase 0 — ตรวจว่าการ login ผ่าน SSO และการ resolve สิทธิ์/สาขา ถูกต้อง
    /// (BR-6 branch, BR-7 SSO + cache, D10 role เริ่มต้น)
    /// </summary>
    [TestFixture]
    public class AuthServiceTests
    {
        private static AuthService BuildService(FakeSsoClient sso, FakeEmployeeRepository employees,
                                                FakeBranchRepository branches = null)
        {
            return new AuthService(sso, employees, branches ?? new FakeBranchRepository());
        }

        [Test]
        public void SignIn_คนใหม่ที่ยังไม่มีสิทธิ์ในระบบต้องได้_role_User()
        {
            var sso = new FakeSsoClient().Add("10099", "SDC");
            var employees = new FakeEmployeeRepository();

            var result = BuildService(sso, employees).SignIn("10099");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.User.Role, Is.EqualTo(Role.User));
        }

        [TestCase("A", Role.Admin)]
        [TestCase("M", Role.Messenger)]
        [TestCase("U", Role.User)]
        public void SignIn_ต้อง_resolve_role_ตามที่บันทึกไว้ใน_DB(string storedRoleCode, Role expected)
        {
            var sso = new FakeSsoClient().Add("10001", "SDC");
            var employees = new FakeEmployeeRepository().WithExistingRole("10001", storedRoleCode);

            var result = BuildService(sso, employees).SignIn("10001");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.User.Role, Is.EqualTo(expected));
        }

        [Test]
        public void SignIn_ต้องนำสาขาที่_SSO_ส่งมาใส่ใน_UserContext()
        {
            var sso = new FakeSsoClient().Add("20002", "SBK");
            var employees = new FakeEmployeeRepository();

            var result = BuildService(sso, employees).SignIn("20002");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.User.BranchCode, Is.EqualTo("SBK"));
        }

        [Test]
        public void SignIn_ต้อง_cache_ข้อมูลพนักงานลง_DB_ทุกครั้ง()
        {
            // BR-7 — ตาราง Employee เป็นเพียง cache ของข้อมูลที่ SSO เป็นเจ้าของ
            var sso = new FakeSsoClient().Add("10002", "SDC");
            var employees = new FakeEmployeeRepository();

            BuildService(sso, employees).SignIn("10002");

            Assert.That(employees.UpsertCallCount, Is.EqualTo(1));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void SignIn_ไม่ระบุรหัสพนักงานต้องไม่สำเร็จ(string empCode)
        {
            var result = BuildService(new FakeSsoClient(), new FakeEmployeeRepository()).SignIn(empCode);

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void SignIn_รหัสที่_SSO_ไม่รู้จักต้องไม่สำเร็จ()
        {
            var sso = new FakeSsoClient().Add("10001", "SDC");

            var result = BuildService(sso, new FakeEmployeeRepository()).SignIn("99999");

            Assert.That(result.Success, Is.False);
            Assert.That(result.User, Is.Null);
        }

        [Test]
        public void SignIn_สาขาที่ไม่มีในระบบต้องไม่สำเร็จ()
        {
            // BR-6 — ถ้า SSO ส่งสาขาที่ระบบไม่รู้จักมา ต้องไม่ปล่อยให้ผ่าน
            // เพราะจะทำให้ข้อมูลหลุดออกนอกขอบเขตสาขาที่ควบคุมได้
            var sso = new FakeSsoClient().Add("30001", "XXX");
            var employees = new FakeEmployeeRepository();

            var result = BuildService(sso, employees).SignIn("30001");

            Assert.That(result.Success, Is.False);
            Assert.That(employees.UpsertCallCount, Is.EqualTo(0), "ต้องไม่บันทึกข้อมูลลง DB เมื่อสาขาไม่ถูกต้อง");
        }

        [Test]
        public void SignIn_ต้องตัดช่องว่างหน้าหลังของรหัสพนักงานออก()
        {
            var sso = new FakeSsoClient().Add("10003", "SDC");

            var result = BuildService(sso, new FakeEmployeeRepository()).SignIn("  10003  ");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.User.EmpCode, Is.EqualTo("10003"));
        }

        // ==================== Phase 6 : สาขา/สิทธิ์ต้องมาจาก DB เสมอ ====================

        [Test]
        public void ResolveCurrent_ต้องได้สาขาและสิทธิ์ล่าสุดจาก_DB()
        {
            // จำลองว่า Admin เพิ่งย้ายพนักงานคนนี้ไปสาขา SBK และเปลี่ยน role เป็น Messenger
            // หลังจากที่เจ้าตัว login ค้างไว้ด้วยข้อมูลเดิม
            var employees = new FakeEmployeeRepository().WithEmployee("10002", "SBK", "M");

            var user = BuildService(new FakeSsoClient(), employees).ResolveCurrent("10002");

            Assert.That(user, Is.Not.Null);
            Assert.That(user.BranchCode, Is.EqualTo("SBK"));
            Assert.That(user.Role, Is.EqualTo(Role.Messenger));
        }

        [Test]
        public void ResolveCurrent_คนที่ถูกปิดการใช้งานต้องใช้ระบบต่อไม่ได้()
        {
            var employees = new FakeEmployeeRepository().WithEmployee("10002", "SDC", "U", isActive: false);

            Assert.That(BuildService(new FakeSsoClient(), employees).ResolveCurrent("10002"), Is.Null);
        }

        [Test]
        public void ResolveCurrent_คนที่ไม่มีในระบบแล้วต้องใช้ระบบต่อไม่ได้()
        {
            Assert.That(BuildService(new FakeSsoClient(), new FakeEmployeeRepository()).ResolveCurrent("99999"),
                Is.Null);
        }

        [Test]
        public void ResolveCurrent_สาขาที่ถูกปิดใช้งานต้องใช้ระบบต่อไม่ได้()
        {
            // BR-6 — สาขาถูกปิด = ทุกคนในสาขานั้นเข้าระบบไม่ได้
            var employees = new FakeEmployeeRepository().WithEmployee("30001", "XXX");

            var user = BuildService(new FakeSsoClient(), employees, new FakeBranchRepository("SDC", "SBK"))
                .ResolveCurrent("30001");

            Assert.That(user, Is.Null);
        }

        [Test]
        public void ResolveCurrent_ต้องไม่เรียก_SSO_ซ้ำทุก_request()
        {
            // ถ้าเผลอไปถาม SSO ทุก request ระบบจะช้าและพึ่งพา SSO เกินจำเป็น
            var sso = new FakeSsoClient().Add("10002", "SDC");
            var employees = new FakeEmployeeRepository().WithEmployee("10002", "SDC");

            BuildService(sso, employees).ResolveCurrent("10002");

            Assert.That(employees.UpsertCallCount, Is.EqualTo(0));
        }
    }
}
