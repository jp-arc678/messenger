using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Messenger.Application.Abstractions;
using Messenger.Application.Dtos;
using Messenger.Application.Services;
using Messenger.Domain.Entities;
using Messenger.Web.ViewModels;

namespace Messenger.Web.Controllers
{
    /// <summary>
    /// คิวงานของสาขา (Phase 2)
    ///
    /// หน้าจอนี้เปิดให้เฉพาะ Messenger/Admin — แต่การกันสิทธิ์ตัวจริงอยู่ที่
    /// service layer (GetQueue/Move จะไม่สำเร็จถ้าเป็น U-User) controller
    /// แค่พาไปหน้าที่เหมาะสมเมื่อไม่มีสิทธิ์
    /// </summary>
    public class QueueController : BaseController
    {
        private readonly IRequestWorkflowService _workflow;
        private readonly IClock _clock;

        public QueueController(IRequestWorkflowService workflow, IClock clock)
        {
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        [HttpGet]
        public ActionResult Index(string date)
        {
            var day = RequestFormViewModel.ParseDate(date) ?? _clock.Today;

            var result = _workflow.GetQueue(CurrentUser, day);
            if (!result.Success)
            {
                TempData["Error"] = result.FirstError;
                return RedirectToAction("Index", "Requests");
            }

            return View(BuildViewModel(result.Value));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Move(int id, QueueMove direction, string date)
        {
            var result = _workflow.Move(id, direction, CurrentUser);

            if (result.Success)
                TempData["Message"] = $"จัดลำดับใบแจ้งงาน {result.Value.ReqNo} เรียบร้อยแล้ว";
            else
                TempData["Error"] = result.FirstError;

            return RedirectToAction("Index", new { date });
        }

        // ---------------- helpers ----------------

        private QueueViewModel BuildViewModel(QueueDay day)
        {
            var running = day.Running.ToList();

            return new QueueViewModel
            {
                Day = day,
                DateText = QueueViewModel.FormatDate(day.SendDate),
                PreviousDateText = QueueViewModel.FormatDate(day.SendDate.AddDays(-1)),
                NextDateText = QueueViewModel.FormatDate(day.SendDate.AddDays(1)),

                Pending = day.Pending.Select(ToRow).ToList(),

                // ปุ่มเลื่อนขึ้น/ลงมีความหมายเฉพาะเมื่อมีใบอื่นให้สลับด้วย
                Running = running
                    .Select((request, index) =>
                    {
                        var row = ToRow(request);
                        row.CanMoveUp = index > 0;
                        row.CanMoveDown = index < running.Count - 1;
                        return row;
                    })
                    .ToList(),

                Closed = day.Closed.Select(ToRow).ToList(),

                Message = TempData["Message"] as string,
                WarningMessage = TempData["Warning"] as string,
                ErrorMessage = TempData["Error"] as string
            };
        }

        private QueueRowViewModel ToRow(DeliveryRequest request)
        {
            return new QueueRowViewModel
            {
                Request = request,
                Actions = _workflow.AvailableActions(request, CurrentUser)
            };
        }
    }
}
