using Messenger.Domain.Enums;
using NUnit.Framework;

namespace Messenger.UnitTests
{
    /// <summary>
    /// D10 — 1 คนมีได้ 1 role และค่าเริ่มต้นต้องเป็น User เสมอ
    /// การแปลงรหัสจาก DB จึงต้องไม่มีทางคืนค่าอื่นเมื่อข้อมูลขาดหาย
    /// </summary>
    [TestFixture]
    public class RoleCodesTests
    {
        [TestCase("A", Role.Admin)]
        [TestCase("a", Role.Admin)]
        [TestCase("U", Role.User)]
        [TestCase("u", Role.User)]
        [TestCase("M", Role.Messenger)]
        [TestCase("m", Role.Messenger)]
        [TestCase(" A ", Role.Admin)]
        public void Parse_แปลงรหัสที่ถูกต้องได้ทั้งตัวพิมพ์เล็กและใหญ่(string roleCode, Role expected)
        {
            Assert.That(RoleCodes.Parse(roleCode), Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("X")]
        [TestCase("Admin")]
        public void Parse_รหัสที่ว่างหรือไม่รู้จักต้องได้_User_เสมอ(string roleCode)
        {
            Assert.That(RoleCodes.Parse(roleCode), Is.EqualTo(Role.User));
        }

        [TestCase(Role.Admin, "A")]
        [TestCase(Role.User, "U")]
        [TestCase(Role.Messenger, "M")]
        public void ToCode_แปลงกลับเป็นรหัสที่เก็บใน_DB(Role role, string expected)
        {
            Assert.That(RoleCodes.ToCode(role), Is.EqualTo(expected));
        }

        [Test]
        public void ค่าเริ่มต้นของ_Role_enum_ต้องเป็น_User()
        {
            // กันการเผลอสลับลำดับสมาชิก enum ในอนาคต
            // ค่า default(Role) ถูกใช้เป็น "สิทธิ์ต่ำสุด" โดยปริยาย
            Assert.That(default(Role), Is.EqualTo(Role.User));
        }
    }
}
