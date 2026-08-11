using System;
using System.Linq;
using Messenger.Domain.Enums;
using Messenger.Domain.Workflow;
using NUnit.Framework;

namespace Messenger.UnitTests
{
    /// <summary>
    /// Phase 2 — state machine ตามตาราง CLAUDE.md §6
    ///
    /// เทสต์ชุดนี้คือสำเนาของตาราง §6 ในรูปแบบที่รันได้ ถ้ามีใครเพิ่ม/แก้ transition
    /// ในโค้ดโดยไม่แก้ CLAUDE.md (หรือกลับกัน) เทสต์ต้องแดงทันที
    /// </summary>
    [TestFixture]
    public class RequestStateMachineTests
    {
        /// <summary>ตาราง §6 ทั้งตาราง : from → to</summary>
        private static readonly (RequestStatus From, RequestStatus To)[] AllowedPairs =
        {
            (RequestStatus.Received,   RequestStatus.Delivering),
            (RequestStatus.Received,   RequestStatus.Cancelled),
            (RequestStatus.Delivering, RequestStatus.Paused),
            (RequestStatus.Delivering, RequestStatus.Completed),
            (RequestStatus.Delivering, RequestStatus.Cancelled),
            (RequestStatus.Paused,     RequestStatus.Delivering),
            (RequestStatus.Paused,     RequestStatus.Cancelled)
        };

        [Test]
        public void ตารางมีเส้นทางครบตามเอกสารมาตรา6_ไม่ขาดไม่เกิน()
        {
            Assert.That(RequestStateMachine.All.Count, Is.EqualTo(AllowedPairs.Length));

            foreach (var pair in AllowedPairs)
            {
                Assert.That(RequestStateMachine.Find(pair.From, pair.To), Is.Not.Null,
                    $"ขาดเส้นทาง {pair.From} → {pair.To}");
            }
        }

        [Test]
        public void เส้นทางที่ไม่มีในเอกสารมาตรา6_ต้องหาไม่เจอทุกคู่()
        {
            var statuses = Enum.GetValues(typeof(RequestStatus)).Cast<RequestStatus>().ToList();

            foreach (var from in statuses)
            {
                foreach (var to in statuses)
                {
                    if (AllowedPairs.Contains((from, to)))
                        continue;

                    Assert.That(RequestStateMachine.Find(from, to), Is.Null,
                        $"ไม่ควรมีเส้นทาง {from} → {to} แต่กลับหาเจอ");
                }
            }
        }

        [TestCase(RequestStatus.Completed, true)]
        [TestCase(RequestStatus.Cancelled, true)]
        [TestCase(RequestStatus.Received, false)]
        [TestCase(RequestStatus.Delivering, false)]
        [TestCase(RequestStatus.Paused, false)]
        public void สถานะปลายทางไปต่อไม่ได้(RequestStatus status, bool expectedTerminal)
        {
            Assert.That(RequestStateMachine.IsTerminal(status), Is.EqualTo(expectedTerminal));
        }

        // ==================== เหตุผลบังคับ (§6) ====================

        [TestCase(RequestStatus.Delivering, RequestAction.Pause, true)]
        [TestCase(RequestStatus.Delivering, RequestAction.Cancel, true)]
        [TestCase(RequestStatus.Paused, RequestAction.Cancel, true)]
        [TestCase(RequestStatus.Received, RequestAction.Cancel, false)]
        [TestCase(RequestStatus.Received, RequestAction.Confirm, false)]
        [TestCase(RequestStatus.Delivering, RequestAction.Complete, false)]
        [TestCase(RequestStatus.Paused, RequestAction.Resume, false)]
        public void เหตุผลบังคับเฉพาะการพักและการยกเลิกหลังเริ่มส่งแล้ว(
            RequestStatus from, RequestAction action, bool reasonRequired)
        {
            var transition = RequestStateMachine.Find(from, action);

            Assert.That(transition, Is.Not.Null);
            Assert.That(transition.ReasonRequired, Is.EqualTo(reasonRequired));
        }

        // ==================== ใครทำได้ (§5 + D7) ====================

        [TestCase(Role.Messenger, true)]
        [TestCase(Role.Admin, true)]
        [TestCase(Role.User, false)]
        public void ยืนยันรับงานเป็นสิทธิ์ของ_Messenger_และ_Admin_เท่านั้น(Role role, bool allowed)
        {
            var confirm = RequestStateMachine.Find(RequestStatus.Received, RequestAction.Confirm);

            // แม้จะเป็นเจ้าของใบงานเอง User ก็ยืนยันรับงานไม่ได้
            Assert.That(confirm.IsAllowedFor(role, isOwner: true), Is.EqualTo(allowed));
        }

        [Test]
        public void User_ยกเลิกได้เฉพาะใบตัวเองตอนสถานะ_Received()
        {
            var cancelAtReceived = RequestStateMachine.Find(RequestStatus.Received, RequestAction.Cancel);

            Assert.That(cancelAtReceived.IsAllowedFor(Role.User, isOwner: true), Is.True);
            Assert.That(cancelAtReceived.IsAllowedFor(Role.User, isOwner: false), Is.False,
                "User ต้องยกเลิกใบของคนอื่นไม่ได้");
        }

        [TestCase(RequestStatus.Delivering)]
        [TestCase(RequestStatus.Paused)]
        public void User_ยกเลิกไม่ได้แล้วหลัง_Messenger_รับงาน(RequestStatus from)
        {
            // D7 — พ้น Received แล้วเป็นเรื่องของ Messenger/Admin เท่านั้น
            var cancel = RequestStateMachine.Find(from, RequestAction.Cancel);

            Assert.That(cancel.IsAllowedFor(Role.User, isOwner: true), Is.False);
            Assert.That(cancel.IsAllowedFor(Role.Messenger, isOwner: false), Is.True);
            Assert.That(cancel.IsAllowedFor(Role.Admin, isOwner: false), Is.True);
        }

        [Test]
        public void ปุ่มที่แสดงต้องตรงกับสิทธิ์ของแต่ละ_role()
        {
            var messenger = RequestStateMachine.AllowedFor(RequestStatus.Received, Role.Messenger, isOwner: false);
            Assert.That(messenger.Select(t => t.Action),
                Is.EquivalentTo(new[] { RequestAction.Confirm, RequestAction.Cancel }));

            var owner = RequestStateMachine.AllowedFor(RequestStatus.Received, Role.User, isOwner: true);
            Assert.That(owner.Select(t => t.Action), Is.EquivalentTo(new[] { RequestAction.Cancel }));

            var otherUser = RequestStateMachine.AllowedFor(RequestStatus.Received, Role.User, isOwner: false);
            Assert.That(otherUser, Is.Empty);

            var closed = RequestStateMachine.AllowedFor(RequestStatus.Completed, Role.Admin, isOwner: false);
            Assert.That(closed, Is.Empty, "สถานะ terminal ต้องไม่มีปุ่มอะไรเหลือ");
        }

        [Test]
        public void ทุก_transition_ต้องมีข้อความบนปุ่ม()
        {
            Assert.That(RequestStateMachine.All.All(t => !string.IsNullOrWhiteSpace(t.DisplayName)), Is.True);
        }
    }
}
