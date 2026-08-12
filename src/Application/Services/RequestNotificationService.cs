using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Messenger.Application.Abstractions;
using Messenger.Domain.Entities;

namespace Messenger.Application.Services
{
    /// <summary>
    /// อีเมลแจ้งผู้แจ้งเมื่อปิดงาน (BR-5)
    ///
    /// กฎที่บังคับในคลาสนี้ :
    /// - D26  ส่งไม่ออกไม่ทำให้การปิดงานล้มเหลว — คืนเป็นคำเตือนแทน exception
    /// - D27  To = ผู้แจ้ง · Cc = คนที่กดสร้างใบงาน เฉพาะเมื่อแจ้งแทนคนอื่น (D17)
    /// - D28  เนื้อเมลมาจาก template ที่แก้ไฟล์ได้โดยไม่ต้อง build ใหม่
    ///        ถ้าไม่มีไฟล์ override จะใช้ template ที่ฝังมากับโค้ด
    ///
    /// ค่าทุกตัวที่แทรกลง template ถูก HTML-encode เสมอ เพราะที่อยู่/รายละเอียดงาน
    /// เป็นข้อความอิสระที่ผู้ใช้พิมพ์เอง อาจมี &lt; &gt; &amp; ปนมาได้
    /// </summary>
    public class RequestNotificationService : IRequestNotificationService
    {
        public const string CompletedTemplateName = "RequestCompleted";

        private const string SubjectPrefix = "Subject:";

        /// <summary>template สำรองที่ใช้เมื่อไม่มีไฟล์ override</summary>
        public const string DefaultCompletedTemplate =
@"Subject: [ปิดงาน] ใบแจ้งงาน {{ReqNo}} — {{ContactName}}

<p>เรียน คุณ{{RequesterName}}</p>
<p>ใบแจ้งงานรับ-ส่งเอกสารของท่านดำเนินการเสร็จเรียบร้อยแล้ว</p>
<table cellpadding=""6"" cellspacing=""0"" border=""1"" style=""border-collapse:collapse;"">
  <tr><td><b>เลขใบงาน</b></td><td>{{ReqNo}}</td></tr>
  <tr><td><b>สาขา</b></td><td>{{BranchCode}} — {{BranchName}}</td></tr>
  <tr><td><b>ประเภทงาน</b></td><td>{{JobTypes}}</td></tr>
  <tr><td><b>ผู้ติดต่อ</b></td><td>{{ContactName}}</td></tr>
  <tr><td><b>ที่อยู่</b></td><td>{{Address}}</td></tr>
  <tr><td><b>รายละเอียดงาน</b></td><td>{{Detail}}</td></tr>
  <tr><td><b>วันที่ส่ง</b></td><td>{{SendDate}}</td></tr>
  <tr><td><b>ผู้รับงาน</b></td><td>{{MessengerName}}</td></tr>
  <tr><td><b>ปิดงานเมื่อ</b></td><td>{{CompletedAt}}</td></tr>
</table>
<p style=""color:#666;font-size:12px;"">อีเมลฉบับนี้ส่งจากระบบแจ้งงานรับ-ส่งเอกสารโดยอัตโนมัติ กรุณาอย่าตอบกลับ</p>";

        private readonly IEmailSender _sender;
        private readonly IEmailTemplateSource _templates;
        private readonly IEmployeeRepository _employees;
        private readonly IClock _clock;

        public RequestNotificationService(IEmailSender sender,
                                          IEmailTemplateSource templates,
                                          IEmployeeRepository employees,
                                          IClock clock)
        {
            _sender = sender ?? throw new ArgumentNullException(nameof(sender));
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _employees = employees ?? throw new ArgumentNullException(nameof(employees));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public NotificationResult NotifyCompleted(DeliveryRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var to = new List<string>();
            var cc = new List<string>();

            // D27 — ผู้แจ้งคือผู้รับหลัก
            if (IsUsableAddress(request.RequesterEmail))
                to.Add(request.RequesterEmail.Trim());

            // D17 + D27 — คนกรอกแทนก็ควรรู้ว่างานที่ตัวเองแจ้งให้เสร็จแล้ว
            var creatorEmail = FindCreatorEmail(request);
            if (creatorEmail != null && !SameAddress(creatorEmail, request.RequesterEmail))
                cc.Add(creatorEmail);

            if (to.Count == 0)
            {
                return new NotificationResult
                {
                    Sent = false,
                    Warning = $"ปิดงานเรียบร้อยแล้ว แต่ไม่ได้ส่งอีเมลแจ้ง เพราะผู้แจ้ง " +
                              $"({request.RequesterName}) ไม่มีอีเมลในระบบ"
                };
            }

            var rendered = Render(request);

            var message = new EmailMessage
            {
                To = to,
                Cc = cc,
                Subject = rendered.Subject,
                HtmlBody = rendered.Body
            };

            try
            {
                _sender.Send(message);
            }
            catch (Exception exception)
            {
                // D26 — งานปิดไปแล้ว ห้ามย้อนกลับเพราะเมลไม่ออก
                return new NotificationResult
                {
                    Sent = false,
                    Recipients = to.Concat(cc).ToList(),
                    Warning = "ปิดงานเรียบร้อยแล้ว แต่ส่งอีเมลแจ้งผู้แจ้งไม่สำเร็จ " +
                              $"({exception.Message}) กรุณาแจ้งผู้ดูแลระบบ"
                };
            }

            return new NotificationResult
            {
                Sent = true,
                Recipients = to.Concat(cc).ToList()
            };
        }

        // ---------------- ภายใน ----------------

        private string FindCreatorEmail(DeliveryRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CreatedBy))
                return null;

            // แจ้งในนามตัวเอง — ไม่ต้องหาซ้ำ
            if (string.Equals(request.CreatedBy, request.RequesterEmpCode, StringComparison.OrdinalIgnoreCase))
                return null;

            var creator = _employees.GetByEmpCode(request.CreatedBy);
            return IsUsableAddress(creator?.Email) ? creator.Email.Trim() : null;
        }

        private RenderedEmail Render(DeliveryRequest request)
        {
            var template = _templates.TryRead(CompletedTemplateName);
            if (string.IsNullOrWhiteSpace(template))
                template = DefaultCompletedTemplate;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ReqNo", request.ReqNo },
                { "BranchCode", request.BranchCode },
                { "BranchName", request.BranchName },
                { "RequesterName", request.RequesterName },
                { "RequesterEmpCode", request.RequesterEmpCode },
                { "ContactName", request.ContactName },
                { "Phone", request.Phone },
                { "Address", request.Address },
                { "Detail", request.Detail },
                { "JobTypes", DescribeJobTypes(request) },
                { "SendDate", request.SendDate.ToString("dd/MM/yyyy") },
                { "CompletedAt", _clock.Now.ToString("dd/MM/yyyy HH:mm") },
                { "MessengerName", request.Assignment?.MessengerName }
            };

            var subject = ExtractSubject(ref template) ?? $"[ปิดงาน] ใบแจ้งงาน {request.ReqNo}";

            return new RenderedEmail
            {
                // หัวเรื่องเป็น plain text จึงไม่ต้อง encode HTML แต่ก็ห้ามมีขึ้นบรรทัดใหม่
                Subject = Fill(subject, values, htmlEncode: false).Replace("\r", " ").Replace("\n", " ").Trim(),
                Body = Fill(template, values, htmlEncode: true)
            };
        }

        /// <summary>
        /// ตัดบรรทัด "Subject: ..." ออกจากหัว template แล้วคืนหัวเรื่อง
        /// ไฟล์ที่ไม่มีบรรทัดนี้ถือว่าเป็นเนื้อความล้วน แล้วใช้หัวเรื่องมาตรฐานแทน
        /// </summary>
        private static string ExtractSubject(ref string template)
        {
            var trimmed = template.TrimStart();
            if (!trimmed.StartsWith(SubjectPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var lineEnd = trimmed.IndexOf('\n');
            if (lineEnd < 0)
            {
                template = string.Empty;
                return trimmed.Substring(SubjectPrefix.Length).Trim();
            }

            var subject = trimmed.Substring(SubjectPrefix.Length, lineEnd - SubjectPrefix.Length).Trim();
            template = trimmed.Substring(lineEnd + 1).TrimStart('\r', '\n');
            return subject;
        }

        private static string Fill(string template, IDictionary<string, string> values, bool htmlEncode)
        {
            var builder = new StringBuilder(template);

            foreach (var pair in values)
            {
                var value = pair.Value ?? "-";
                if (htmlEncode)
                    value = ToHtml(value);

                builder.Replace("{{" + pair.Key + "}}", value);
            }

            return builder.ToString();
        }

        /// <summary>encode อักขระพิเศษ แล้วเปลี่ยนขึ้นบรรทัดใหม่เป็น &lt;br /&gt;</summary>
        private static string ToHtml(string value)
        {
            return WebUtility.HtmlEncode(value)
                .Replace("\r\n", "<br />")
                .Replace("\n", "<br />");
        }

        private static string DescribeJobTypes(DeliveryRequest request)
        {
            if (request.JobTypes == null || request.JobTypes.Count == 0)
                return "-";

            return string.Join(", ", request.JobTypes.Select(j =>
                string.IsNullOrWhiteSpace(j.DetailText)
                    ? j.JobTypeDisplayName
                    : j.JobTypeDisplayName + " (" + j.DetailText + ")"));
        }

        private static bool IsUsableAddress(string address)
        {
            // ตรวจแค่พอให้รู้ว่า "พอจะเป็นอีเมล" — ความถูกต้องจริงเป็นเรื่องของ SMTP
            return !string.IsNullOrWhiteSpace(address) && address.Contains("@");
        }

        private static bool SameAddress(string left, string right)
        {
            return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private class RenderedEmail
        {
            public string Subject { get; set; }

            public string Body { get; set; }
        }
    }
}
