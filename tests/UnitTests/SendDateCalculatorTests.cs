using System;
using Messenger.Application.Services;
using NUnit.Framework;

namespace Messenger.UnitTests
{
    /// <summary>
    /// BR-1 — กฎวันที่ส่ง และ D16 — เงื่อนไขวันที่ผู้ใช้เลือกเอง
    ///
    /// วันอ้างอิงที่ใช้ในเทสต์ (สิงหาคม 2026) :
    ///   2026-08-10 = จันทร์   2026-08-14 = ศุกร์
    ///   2026-08-15 = เสาร์    2026-08-16 = อาทิตย์   2026-08-17 = จันทร์
    /// </summary>
    [TestFixture]
    public class SendDateCalculatorTests
    {
        // ---------- ข้อ 1 + 2 : เวลาตัดรอบ 10:00 ----------

        [Test]
        public void บันทึกก่อน10โมง_ได้วันเดียวกัน()
        {
            var result = SendDateCalculator.CalculateDefault(new DateTime(2026, 8, 10, 9, 59, 59));

            Assert.That(result, Is.EqualTo(new DateTime(2026, 8, 10)));
        }

        [Test]
        public void บันทึกเวลา10โมงตรง_ยังได้วันเดียวกัน()
        {
            // D8 — เทียบด้วย > เท่านั้น 10:00:00 ตรงจึงยังไม่เกิน
            var result = SendDateCalculator.CalculateDefault(new DateTime(2026, 8, 10, 10, 0, 0));

            Assert.That(result, Is.EqualTo(new DateTime(2026, 8, 10)));
        }

        [Test]
        public void บันทึกเกิน10โมงแม้แค่1วินาที_เลื่อนเป็นวันถัดไป()
        {
            var result = SendDateCalculator.CalculateDefault(new DateTime(2026, 8, 10, 10, 0, 1));

            Assert.That(result, Is.EqualTo(new DateTime(2026, 8, 11)));
        }

        [Test]
        public void บันทึกบ่าย_เลื่อนเป็นวันถัดไป()
        {
            var result = SendDateCalculator.CalculateDefault(new DateTime(2026, 8, 10, 15, 30, 0));

            Assert.That(result, Is.EqualTo(new DateTime(2026, 8, 11)));
        }

        // ---------- ข้อ 3 : เลี่ยงวันเสาร์-อาทิตย์ ----------

        [Test]
        public void บันทึกเช้าวันเสาร์_เลื่อนเป็นวันจันทร์()
        {
            var result = SendDateCalculator.CalculateDefault(new DateTime(2026, 8, 15, 8, 0, 0));

            Assert.That(result, Is.EqualTo(new DateTime(2026, 8, 17)));
        }

        [Test]
        public void บันทึกเช้าวันอาทิตย์_เลื่อนเป็นวันจันทร์()
        {
            var result = SendDateCalculator.CalculateDefault(new DateTime(2026, 8, 16, 8, 0, 0));

            Assert.That(result, Is.EqualTo(new DateTime(2026, 8, 17)));
        }

        // ---------- กฎทั้งสามข้อต้อง compose กันได้ ----------

        [Test]
        public void ศุกร์บ่าย_ข้ามเสาร์อาทิตย์ไปเป็นจันทร์()
        {
            // ตัวอย่างตรงตาม CLAUDE.md : ศุกร์ 11:00 -> พรุ่งนี้เสาร์ -> เลื่อนเป็นจันทร์
            var result = SendDateCalculator.CalculateDefault(new DateTime(2026, 8, 14, 11, 0, 0));

            Assert.That(result, Is.EqualTo(new DateTime(2026, 8, 17)));
        }

        [Test]
        public void ศุกร์เช้า_ยังได้วันศุกร์เดิม()
        {
            var result = SendDateCalculator.CalculateDefault(new DateTime(2026, 8, 14, 9, 0, 0));

            Assert.That(result, Is.EqualTo(new DateTime(2026, 8, 14)));
        }

        [Test]
        public void เสาร์บ่าย_เลื่อนไปจันทร์ไม่ใช่อังคาร()
        {
            // เสาร์บ่าย -> ข้อ 2 ได้อาทิตย์ -> ข้อ 3 ได้จันทร์
            var result = SendDateCalculator.CalculateDefault(new DateTime(2026, 8, 15, 14, 0, 0));

            Assert.That(result, Is.EqualTo(new DateTime(2026, 8, 17)));
        }

        [Test]
        public void ผลลัพธ์ต้องไม่มีเศษเวลาติดมา()
        {
            var result = SendDateCalculator.CalculateDefault(new DateTime(2026, 8, 10, 15, 45, 30));

            Assert.That(result.TimeOfDay, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void ค่าdefaultต้องไม่เคยตกวันหยุดสุดสัปดาห์()
        {
            // เดินทีละชั่วโมงตลอด 14 วัน เพื่อกันกรณีตกหล่น
            var start = new DateTime(2026, 8, 10, 0, 0, 0);

            for (var hour = 0; hour < 24 * 14; hour++)
            {
                var moment = start.AddHours(hour);
                var sendDate = SendDateCalculator.CalculateDefault(moment);

                Assert.That(SendDateCalculator.IsWeekend(sendDate), Is.False,
                    "บันทึกเมื่อ " + moment.ToString("yyyy-MM-dd HH:mm") +
                    " ได้ sendDate ตกวันหยุด: " + sendDate.ToString("yyyy-MM-dd"));

                Assert.That(sendDate, Is.GreaterThanOrEqualTo(moment.Date),
                    "sendDate ต้องไม่ย้อนหลังกว่าวันที่บันทึก");
            }
        }

        // ---------- D16 : วันที่ผู้ใช้เลือกเอง ----------

        [Test]
        public void ผู้ใช้เลือกวันย้อนหลัง_ต้องไม่ผ่าน()
        {
            var error = SendDateCalculator.ValidateUserPickedDate(
                new DateTime(2026, 8, 9), new DateTime(2026, 8, 10));

            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void ผู้ใช้เลือกวันนี้_ผ่าน()
        {
            var error = SendDateCalculator.ValidateUserPickedDate(
                new DateTime(2026, 8, 10), new DateTime(2026, 8, 10));

            Assert.That(error, Is.Null);
        }

        [TestCase(15)] // เสาร์
        [TestCase(16)] // อาทิตย์
        public void ผู้ใช้เลือกวันหยุดสุดสัปดาห์_ต้องไม่ผ่าน(int day)
        {
            var error = SendDateCalculator.ValidateUserPickedDate(
                new DateTime(2026, 8, day), new DateTime(2026, 8, 10));

            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void ผู้ใช้เลือกวันทำการในอนาคต_ผ่าน()
        {
            var error = SendDateCalculator.ValidateUserPickedDate(
                new DateTime(2026, 8, 17), new DateTime(2026, 8, 10));

            Assert.That(error, Is.Null);
        }

        [Test]
        public void การตรวจวันที่ผู้ใช้เลือก_ต้องไม่เลื่อนวันให้อัตโนมัติ()
        {
            // D16 — ตอนผู้ใช้เลือกเอง ระบบ "ปฏิเสธ" ไม่ใช่ "เลื่อนให้"
            // ถ้าเปลี่ยนเป็นเลื่อนให้เมื่อไหร่ เทสต์นี้จะจับได้
            var saturday = new DateTime(2026, 8, 15);

            Assert.That(SendDateCalculator.ValidateUserPickedDate(saturday, new DateTime(2026, 8, 10)),
                Is.Not.Null);
        }
    }
}
