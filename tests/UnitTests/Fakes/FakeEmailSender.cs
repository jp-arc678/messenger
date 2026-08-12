using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Application.Abstractions;

namespace Messenger.UnitTests.Fakes
{
    /// <summary>ช่องทางส่งเมลปลอม — เก็บทุกฉบับไว้ให้เทสต์ตรวจ และแกล้งพังได้</summary>
    public class FakeEmailSender : IEmailSender
    {
        private readonly List<EmailMessage> _sent = new List<EmailMessage>();

        public IReadOnlyList<EmailMessage> Sent => _sent;

        public EmailMessage LastMessage => _sent.LastOrDefault();

        /// <summary>ตั้งข้อความไว้เพื่อจำลองว่า SMTP ล่ม (D26)</summary>
        public string FailWithMessage { get; set; }

        public void Send(EmailMessage message)
        {
            if (FailWithMessage != null)
                throw new InvalidOperationException(FailWithMessage);

            _sent.Add(message);
        }
    }

    /// <summary>template ปลอม — ไม่ตั้งค่าอะไร = ใช้ template ที่ฝังมากับโค้ด</summary>
    public class FakeEmailTemplateSource : IEmailTemplateSource
    {
        private readonly Dictionary<string, string> _templates =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public FakeEmailTemplateSource With(string name, string content)
        {
            _templates[name] = content;
            return this;
        }

        public string TryRead(string templateName)
        {
            string content;
            return _templates.TryGetValue(templateName ?? string.Empty, out content) ? content : null;
        }
    }
}
