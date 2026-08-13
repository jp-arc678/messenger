using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Application.Abstractions;
using Messenger.Application.Dtos;
using Messenger.Domain.Entities;
using Messenger.Domain.Enums;
using Messenger.Domain.Workflow;

namespace Messenger.Application.Services
{
    /// <summary>
    /// Workflow ของ Messenger (Phase 2)
    ///
    /// กฎที่บังคับในคลาสนี้ :
    /// - §6   เปลี่ยนสถานะได้เฉพาะ transition ที่มีใน <see cref="RequestStateMachine"/>
    ///        และทุกครั้งต้องลง audit trail (ทำใน transaction เดียวกับการเปลี่ยนสถานะ)
    /// - §5   ใครกดปุ่มไหนได้ — อ่านจากตาราง §6 ผ่าน StatusTransition.IsAllowedFor
    /// - D7   User ยกเลิกได้เฉพาะใบตัวเองตอนสถานะ Received
    /// - D11  ลำดับวิ่งงานเป็นของ "วัน + สาขา" และ Messenger ประจำสาขามีคนเดียว
    /// - D22  Admin กดยืนยันแทนได้ แต่ผู้รับงานที่บันทึกคือ Messenger ประจำสาขา
    /// - BR-6 อ่าน/เขียนด้วย branchCode ของผู้ใช้เสมอ ใบงานต่างสาขาจะหาไม่เจอตั้งแต่ต้น
    /// - BR-4 ใบที่มีประเภทงาน "รับเอกสาร" ต้องกดยืนยันรับของก่อนจึงปิดงานได้
    ///        (ไม่บังคับว่าต้องมีรูป ตาม D9)
    /// - BR-5 ปิดงานแล้วส่งอีเมลแจ้งผู้แจ้ง — ส่งไม่ออกก็ไม่ย้อนสถานะ (D26)
    /// </summary>
    public class RequestWorkflowService : IRequestWorkflowService
    {
        /// <summary>ยาวสุดของเหตุผล — คอลัมน์ใน DB เป็น NVARCHAR(1000) เผื่อไว้ให้ note ต่อท้ายได้</summary>
        public const int MaxReasonLength = 500;

        private readonly IDeliveryRequestRepository _requests;
        private readonly IRequestWorkflowRepository _workflow;
        private readonly IEmployeeRepository _employees;
        private readonly IRequestNotificationService _notifications;
        private readonly IClock _clock;

        public RequestWorkflowService(IDeliveryRequestRepository requests,
                                      IRequestWorkflowRepository workflow,
                                      IEmployeeRepository employees,
                                      IRequestNotificationService notifications,
                                      IClock clock)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _employees = employees ?? throw new ArgumentNullException(nameof(employees));
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public ServiceResult<DeliveryRequest> Apply(int reqId, RequestAction action, string reason, UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            // ค่า action ที่ไม่รู้จัก = ฟอร์มถูกดัดแปลงมา ไม่ต้องเดาว่าหมายถึงอะไร
            if (!Enum.IsDefined(typeof(RequestAction), action))
                return ServiceResult<DeliveryRequest>.Fail("ไม่รู้จักการกระทำที่ส่งมา");

            // BR-6 — ใบงานต่างสาขามองไม่เห็นตั้งแต่ชั้น query
            var request = _requests.GetById(reqId, user.BranchCode);
            if (request == null)
                return ServiceResult<DeliveryRequest>.Fail("ไม่พบใบแจ้งงานนี้ในสาขาของคุณ");

            // §6 — เส้นทางที่ไม่มีในตาราง ห้ามเดินเด็ดขาด
            var transition = RequestStateMachine.Find(request.Status, action);
            if (transition == null)
                return ServiceResult<DeliveryRequest>.Fail(NoTransitionMessage(request, action));

            var isOwner = RequestAccess.IsOwner(request, user);
            if (!transition.IsAllowedFor(user.Role, isOwner))
                return ServiceResult<DeliveryRequest>.Fail(NotAllowedMessage(transition, user, isOwner));

            reason = Normalize(reason);

            if (transition.ReasonRequired && reason == null)
                return ServiceResult<DeliveryRequest>.Fail($"กรุณาระบุเหตุผลของการ{transition.DisplayName}");

            if (reason != null && reason.Length > MaxReasonLength)
                return ServiceResult<DeliveryRequest>.Fail($"เหตุผลยาวเกิน {MaxReasonLength} ตัวอักษร");

            // BR-4 — ใบที่มีประเภทงาน "รับเอกสาร" ต้องกดยืนยันรับของก่อนจึงปิดงานได้
            // (ไม่บังคับว่าต้องมีรูป ตาม D9)
            if (action == RequestAction.Complete && request.BlockedByReceiptConfirmation)
            {
                return ServiceResult<DeliveryRequest>.Fail(
                    "ใบแจ้งงานนี้มีประเภทงาน \"รับเอกสาร\" ต้องกดยืนยันว่ารับของแล้วก่อนจึงปิดงานได้");
            }

            return action == RequestAction.Confirm
                ? ConfirmAssignment(request, transition, user)
                : ChangeStatus(request, transition, reason, user);
        }

        public ServiceResult<DeliveryRequest> ConfirmReceipt(int reqId, UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var request = _requests.GetById(reqId, user.BranchCode);
            if (request == null)
                return ServiceResult<DeliveryRequest>.Fail("ไม่พบใบแจ้งงานนี้ในสาขาของคุณ");

            // §5 — เป็นการกระทำระหว่างวิ่งงาน จึงเป็นสิทธิ์ของ Messenger/Admin เท่านั้น
            if (!RequestAccess.SeesWholeBranch(user))
                return ServiceResult<DeliveryRequest>.Fail("การยืนยันรับของเป็นสิทธิ์ของเจ้าหน้าที่ Messenger และผู้ดูแลระบบเท่านั้น");

            if (!request.RequiresReceiptConfirmation)
            {
                return ServiceResult<DeliveryRequest>.Fail(
                    "ใบแจ้งงานนี้ไม่มีประเภทงาน \"รับเอกสาร\" จึงไม่ต้องยืนยันรับของ");
            }

            // D23 — ยืนยันได้เฉพาะช่วงที่งานกำลังเดินอยู่ เหมือนกับการอัปโหลดรูป
            if (!IsRunning(request))
            {
                return ServiceResult<DeliveryRequest>.Fail(
                    $"ยืนยันรับของได้เฉพาะตอนใบงานอยู่ในสถานะ \"กำลังส่ง\" หรือ \"พักการส่ง\" " +
                    $"(ตอนนี้อยู่สถานะ \"{request.StatusDisplayName}\")");
            }

            if (request.ReceiptConfirmed)
                return ServiceResult<DeliveryRequest>.Ok(request);

            var confirmed = _workflow.ConfirmReceipt(new ReceiptConfirmData
            {
                ReqId = reqId,
                BranchCode = user.BranchCode,
                ByEmpCode = user.EmpCode,
                ConfirmedAt = _clock.Now
            });

            if (!confirmed)
            {
                return ServiceResult<DeliveryRequest>.Conflict(
                    "ใบแจ้งงานนี้ถูกยืนยันรับของโดยผู้ใช้อื่นไปแล้ว กรุณาโหลดหน้าใหม่");
            }

            return Reload(reqId, user);
        }

        public bool CanConfirmReceipt(DeliveryRequest request, UserContext user)
        {
            if (request == null || user == null)
                return false;

            if (!RequestAccess.SameBranch(request.BranchCode, user.BranchCode))
                return false;

            return RequestAccess.SeesWholeBranch(user)
                && request.BlockedByReceiptConfirmation
                && IsRunning(request);
        }

        public ServiceResult<QueueDay> GetQueue(UserContext user, DateTime sendDate)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            // §5 — U-User เห็นเฉพาะใบตัวเอง จึงไม่มีหน้าคิวงานของสาขา
            if (!RequestAccess.SeesWholeBranch(user))
                return ServiceResult<QueueDay>.Fail("คิวงานของสาขาเปิดให้เฉพาะ Messenger และผู้ดูแลระบบเท่านั้น");

            var day = sendDate.Date;
            var requests = _requests.List(new RequestListFilter
            {
                BranchCode = user.BranchCode,
                SendDateFrom = day,
                SendDateTo = day
            });

            var queue = new QueueDay
            {
                SendDate = day,
                BranchCode = user.BranchCode,
                BranchName = user.BranchName,

                Pending = requests
                    .Where(r => r.Status == RequestStatus.Received)
                    .OrderBy(r => r.RequestDateTime)
                    .ThenBy(r => r.ReqNo, StringComparer.Ordinal)
                    .ToList(),

                Running = OrderByQueue(requests.Where(IsRunning)).ToList(),

                Closed = requests
                    .Where(r => RequestStateMachine.IsTerminal(r.Status))
                    .OrderBy(r => r.SequenceOrder ?? int.MaxValue)
                    .ThenBy(r => r.ReqNo, StringComparer.Ordinal)
                    .ToList()
            };

            return ServiceResult<QueueDay>.Ok(queue);
        }

        public ServiceResult<DeliveryRequest> Move(int reqId, QueueMove direction, UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            if (!RequestAccess.SeesWholeBranch(user))
                return ServiceResult<DeliveryRequest>.Fail("เฉพาะ Messenger และผู้ดูแลระบบเท่านั้นที่จัดลำดับคิวงานได้");

            var request = _requests.GetById(reqId, user.BranchCode);
            if (request == null)
                return ServiceResult<DeliveryRequest>.Fail("ไม่พบใบแจ้งงานนี้ในสาขาของคุณ");

            if (request.Assignment == null)
                return ServiceResult<DeliveryRequest>.Fail("ใบแจ้งงานนี้ยังไม่ถูกยืนยันรับงาน จึงยังไม่มีลำดับในคิว");

            if (!IsRunning(request))
                return ServiceResult<DeliveryRequest>.Fail("ใบแจ้งงานที่ปิดแล้วจัดลำดับใหม่ไม่ได้");

            // คิวของวันเดียวกัน สาขาเดียวกัน เรียงตามลำดับปัจจุบัน (D11)
            var queue = OrderByQueue(_requests
                    .List(new RequestListFilter
                    {
                        BranchCode = user.BranchCode,
                        SendDateFrom = request.SendDate.Date,
                        SendDateTo = request.SendDate.Date
                    })
                    .Where(IsRunning))
                .ToList();

            var index = queue.FindIndex(r => r.ReqId == reqId);
            if (index < 0)
                return ServiceResult<DeliveryRequest>.Fail("ไม่พบใบแจ้งงานนี้ในคิวของวันดังกล่าว");

            var targetIndex = index + (int)direction;
            if (targetIndex < 0)
                return ServiceResult<DeliveryRequest>.Fail("ใบแจ้งงานนี้อยู่บนสุดของคิวแล้ว");

            if (targetIndex >= queue.Count)
                return ServiceResult<DeliveryRequest>.Fail("ใบแจ้งงานนี้อยู่ล่างสุดของคิวแล้ว");

            if (!_workflow.SwapSequence(reqId, queue[targetIndex].ReqId, user.BranchCode))
            {
                return ServiceResult<DeliveryRequest>.Conflict(
                    "คิวงานเปลี่ยนไปแล้วระหว่างที่หน้าจอนี้เปิดค้างอยู่ กรุณาโหลดหน้าใหม่อีกครั้ง");
            }

            return Reload(reqId, user);
        }

        public IReadOnlyList<StatusTransition> AvailableActions(DeliveryRequest request, UserContext user)
        {
            if (request == null || user == null)
                return new List<StatusTransition>();

            // BR-6 — ใบงานต่างสาขาไม่มีปุ่มอะไรให้กดเลย
            if (!RequestAccess.SameBranch(request.BranchCode, user.BranchCode))
                return new List<StatusTransition>();

            return RequestStateMachine.AllowedFor(request.Status, user.Role, RequestAccess.IsOwner(request, user));
        }

        public ServiceResult<IReadOnlyList<StatusHistoryEntry>> GetHistory(int reqId, UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var request = _requests.GetById(reqId, user.BranchCode);
            if (request == null)
                return ServiceResult<IReadOnlyList<StatusHistoryEntry>>.Fail("ไม่พบใบแจ้งงานนี้ในสาขาของคุณ");

            // §5 — คนที่ดูใบงานไม่ได้ ก็ต้องดูประวัติไม่ได้เช่นกัน
            if (!RequestAccess.CanSee(request, user))
                return ServiceResult<IReadOnlyList<StatusHistoryEntry>>.Fail("คุณไม่มีสิทธิ์ดูใบแจ้งงานนี้");

            return ServiceResult<IReadOnlyList<StatusHistoryEntry>>.Ok(
                _workflow.ListHistory(reqId, user.BranchCode));
        }

        // ---------------- ภายใน ----------------

        private ServiceResult<DeliveryRequest> ConfirmAssignment(DeliveryRequest request,
                                                                 StatusTransition transition,
                                                                 UserContext user)
        {
            var messenger = ResolveMessenger(user);
            if (!messenger.Success)
                return ServiceResult<DeliveryRequest>.Fail(messenger.Errors);

            var assignment = _workflow.Confirm(new ConfirmAssignmentData
            {
                ReqId = request.ReqId,
                BranchCode = user.BranchCode,
                MessengerEmpCode = messenger.Value,
                ByEmpCode = user.EmpCode,
                ConfirmedAt = _clock.Now,
                Note = transition.DisplayName
            });

            if (assignment == null)
                return ServiceResult<DeliveryRequest>.Conflict(RaceLostMessage(transition));

            return Reload(request.ReqId, user);
        }

        private ServiceResult<DeliveryRequest> ChangeStatus(DeliveryRequest request,
                                                            StatusTransition transition,
                                                            string reason,
                                                            UserContext user)
        {
            var changed = _workflow.ChangeStatus(new StatusChangeData
            {
                ReqId = request.ReqId,
                BranchCode = user.BranchCode,

                // สถานะที่เราเห็นตอนตัดสินใจ — ใช้เป็นเงื่อนไขกันคนกดตัดหน้า
                FromStatus = request.Status,
                ToStatus = transition.To,
                ByEmpCode = user.EmpCode,
                ChangedAt = _clock.Now,
                Note = BuildNote(transition, reason),
                Reason = reason
            });

            if (!changed)
                return ServiceResult<DeliveryRequest>.Conflict(RaceLostMessage(transition));

            var reloaded = Reload(request.ReqId, user);

            // BR-5 — จบ process แล้วแจ้งผู้แจ้งทางอีเมล
            // ทำ "หลัง" เปลี่ยนสถานะสำเร็จเสมอ และความล้มเหลวของเมลกลายเป็นแค่คำเตือน (D26)
            if (transition.To == RequestStatus.Completed && reloaded.Success)
            {
                var notification = _notifications.NotifyCompleted(reloaded.Value);
                if (!string.IsNullOrWhiteSpace(notification.Warning))
                {
                    return ServiceResult<DeliveryRequest>.OkWithWarnings(
                        reloaded.Value, new[] { notification.Warning });
                }
            }

            return reloaded;
        }

        /// <summary>
        /// D11 + D22 — ผู้รับงานที่บันทึกคือ Messenger ประจำสาขาเสมอ
        /// ถ้าคนกดยืนยันเป็น Messenger อยู่แล้วก็คือคนนั้น ถ้าเป็น Admin กดแทน
        /// ต้องหา Messenger ของสาขานั้นให้เจอก่อน
        /// </summary>
        private ServiceResult<string> ResolveMessenger(UserContext user)
        {
            if (user.IsMessenger)
                return ServiceResult<string>.Ok(user.EmpCode);

            var messengers = (_employees.ListByBranch(user.BranchCode) ?? new List<Employee>())
                .Where(e => e.Role == Role.Messenger && e.IsActive)
                .ToList();

            if (messengers.Count == 0)
            {
                return ServiceResult<string>.Fail(
                    $"สาขา {user.BranchCode} ยังไม่มีเจ้าหน้าที่ Messenger จึงยืนยันรับงานแทนไม่ได้ " +
                    "กรุณากำหนดสิทธิ์ Messenger ให้พนักงานในสาขาก่อน");
            }

            if (messengers.Count > 1)
            {
                return ServiceResult<string>.Fail(
                    $"สาขา {user.BranchCode} มีเจ้าหน้าที่ Messenger มากกว่า 1 คน " +
                    "ซึ่งขัดกับข้อกำหนดที่ให้มีประจำสาขาละคนเดียว กรุณาแก้ไขสิทธิ์ให้เหลือคนเดียวก่อน");
            }

            return ServiceResult<string>.Ok(messengers[0].EmpCode);
        }

        private ServiceResult<DeliveryRequest> Reload(int reqId, UserContext user)
        {
            var saved = _requests.GetById(reqId, user.BranchCode);
            if (saved == null)
                return ServiceResult<DeliveryRequest>.Fail("ไม่พบใบแจ้งงานนี้ในสาขาของคุณ");

            return ServiceResult<DeliveryRequest>.Ok(saved);
        }

        /// <summary>งานที่ยังวิ่งอยู่ในคิว — กำลังส่ง หรือพักไว้ชั่วคราว</summary>
        private static bool IsRunning(DeliveryRequest request)
        {
            return request.Status == RequestStatus.Delivering || request.Status == RequestStatus.Paused;
        }

        /// <summary>
        /// เรียงตามลำดับวิ่งงาน ใบที่ยังไม่มีลำดับให้ไปอยู่ท้ายสุด
        /// (ปกติจะไม่เกิด เพราะทุกใบที่พ้น Received แล้วต้องมี assignment)
        /// </summary>
        private static IEnumerable<DeliveryRequest> OrderByQueue(IEnumerable<DeliveryRequest> requests)
        {
            return requests
                .OrderBy(r => r.SequenceOrder ?? int.MaxValue)
                .ThenBy(r => r.ReqNo, StringComparer.Ordinal);
        }

        private static string BuildNote(StatusTransition transition, string reason)
        {
            return reason == null ? transition.DisplayName : transition.DisplayName + " : " + reason;
        }

        private static string NoTransitionMessage(DeliveryRequest request, RequestAction action)
        {
            var actionName = RequestStateMachine.All
                .Where(t => t.Action == action)
                .Select(t => t.DisplayName)
                .FirstOrDefault() ?? "เปลี่ยนสถานะ";

            if (RequestStateMachine.IsTerminal(request.Status))
            {
                return $"ใบแจ้งงานนี้อยู่ในสถานะ \"{request.StatusDisplayName}\" " +
                       "ซึ่งเป็นสถานะสุดท้ายแล้ว จึงเปลี่ยนสถานะต่อไม่ได้";
            }

            return $"ใบแจ้งงานที่อยู่ในสถานะ \"{request.StatusDisplayName}\" ทำรายการ\"{actionName}\"ไม่ได้";
        }

        private static string NotAllowedMessage(StatusTransition transition, UserContext user, bool isOwner)
        {
            // กรณีที่เจอบ่อยที่สุด : User กดยกเลิกใบของคนอื่น
            if (user.Role == Role.User && transition.AllowedForOwnerUser && !isOwner)
                return $"คุณ{transition.DisplayName}ได้เฉพาะใบแจ้งงานที่ตัวเองเป็นผู้แจ้งเท่านั้น";

            if (user.Role == Role.User)
            {
                return $"การ{transition.DisplayName}เป็นสิทธิ์ของเจ้าหน้าที่ Messenger " +
                       "และผู้ดูแลระบบเท่านั้น";
            }

            return $"คุณไม่มีสิทธิ์{transition.DisplayName}ใบแจ้งงานนี้";
        }

        private static string RaceLostMessage(StatusTransition transition)
        {
            return $"ใบแจ้งงานนี้ถูกเปลี่ยนสถานะโดยผู้ใช้อื่นไปแล้ว จึง{transition.DisplayName}ซ้ำไม่ได้ " +
                   "กรุณาโหลดหน้าใหม่เพื่อดูสถานะล่าสุด";
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
