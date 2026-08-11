using System;
using System.Globalization;
using System.Threading;
using Messenger.Application.Services;
using NUnit.Framework;

namespace Messenger.UnitTests
{
    /// <summary>
    /// BR-8 — รูปแบบเลขใบงาน MSG-{BRANCH}-{YYMM}-{NNNN}
    /// </summary>
    [TestFixture]
    public class ReqNoGeneratorTests
    {
        [Test]
        public void สร้างเลขใบงานได้ตรงตามตัวอย่างใน_CLAUDE_md()
        {
            var august2026 = new DateTime(2026, 8, 11);

            Assert.That(ReqNoGenerator.Build("SDC", august2026, 1), Is.EqualTo("MSG-SDC-2608-0001"));
            Assert.That(ReqNoGenerator.Build("SBK", august2026, 1), Is.EqualTo("MSG-SBK-2608-0001"));
        }

        [TestCase(1, "0001")]
        [TestCase(9, "0009")]
        [TestCase(10, "0010")]
        [TestCase(99, "0099")]
        [TestCase(100, "0100")]
        [TestCase(1000, "1000")]
        [TestCase(9999, "9999")]
        public void เลขลำดับต้องเติมศูนย์ให้ครบ4หลัก(int running, string expectedSuffix)
        {
            var reqNo = ReqNoGenerator.Build("SDC", new DateTime(2026, 8, 11), running);

            Assert.That(reqNo, Is.EqualTo("MSG-SDC-2608-" + expectedSuffix));
        }

        [TestCase(2026, 1, "2601")]
        [TestCase(2026, 9, "2609")]
        [TestCase(2026, 10, "2610")]
        [TestCase(2026, 12, "2612")]
        [TestCase(2027, 1, "2701")]
        public void ส่วน_YYMM_ต้องเป็นปีคริสต์ศักราช2หลักและเดือน2หลัก(int year, int month, string expected)
        {
            Assert.That(ReqNoGenerator.ToYyMm(new DateTime(year, month, 1)), Is.EqualTo(expected));
        }

        [Test]
        public void ต้องไม่ใช้ปีพุทธศักราชแม้ระบบตั้ง_culture_เป็นไทย()
        {
            // ระบบตั้ง culture ของ request เป็นภาษาไทยได้ ถ้าเผลอฟอร์แมตตาม culture
            // ปัจจุบัน จะได้ปี พ.ศ. 2569 กลายเป็น "6908" แทนที่จะเป็น "2608"
            var original = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("th-TH");

                Assert.That(ReqNoGenerator.ToYyMm(new DateTime(2026, 8, 11)), Is.EqualTo("2608"));
                Assert.That(ReqNoGenerator.Build("SDC", new DateTime(2026, 8, 11), 1),
                    Is.EqualTo("MSG-SDC-2608-0001"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void รหัสสาขาต้องถูกแปลงเป็นตัวพิมพ์ใหญ่และตัดช่องว่าง()
        {
            var reqNo = ReqNoGenerator.Build("  sdc ", new DateTime(2026, 8, 11), 7);

            Assert.That(reqNo, Is.EqualTo("MSG-SDC-2608-0007"));
        }

        [Test]
        public void เลขใบงานของคนละสาขาในเดือนเดียวกันต้องต่างกัน()
        {
            // BR-8 — ลำดับแยกตาม (สาขา + YYMM) เลข 1 ของสองสาขาจึงอยู่ร่วมกันได้
            var month = new DateTime(2026, 8, 11);

            Assert.That(ReqNoGenerator.Build("SDC", month, 1),
                Is.Not.EqualTo(ReqNoGenerator.Build("SBK", month, 1)));
        }

        [Test]
        public void เลขลำดับเดียวกันคนละเดือนต้องต่างกัน()
        {
            // BR-8 — reset ทุกเดือน เลข 1 ของสองเดือนจึงต้องแยกกันได้ด้วย YYMM
            Assert.That(ReqNoGenerator.Build("SDC", new DateTime(2026, 8, 1), 1),
                Is.Not.EqualTo(ReqNoGenerator.Build("SDC", new DateTime(2026, 9, 1), 1)));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void เลขลำดับต่ำกว่า1ต้องไม่ยอมรับ(int running)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ReqNoGenerator.Build("SDC", new DateTime(2026, 8, 11), running));
        }

        [Test]
        public void เลขลำดับเกิน9999ต้องไม่ยอมรับ()
        {
            // ถ้าเกิน 4 หลัก รูปแบบตาม BR-8 จะพังเงียบ ๆ จึงต้องโยน exception ให้รู้ตัว
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ReqNoGenerator.Build("SDC", new DateTime(2026, 8, 11), 10000));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void ไม่ระบุรหัสสาขาต้องไม่ยอมรับ(string branchCode)
        {
            Assert.Throws<ArgumentException>(
                () => ReqNoGenerator.Build(branchCode, new DateTime(2026, 8, 11), 1));
        }
    }
}
