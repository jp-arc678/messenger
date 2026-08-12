using System;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using Messenger.Application.Abstractions;

namespace Messenger.Infrastructure.Email
{
    /// <summary>
    /// ส่งอีเมลผ่าน <see cref="SmtpClient"/> ของ .NET (BR-5 + D28)
    ///
    /// สองโหมดที่ต่างกันแค่ config :
    /// - dev        : ตั้ง pickupDirectory ไว้ → .NET เขียนไฟล์ .eml ลงโฟลเดอร์นั้นแทนการส่งจริง
    ///                เปิดด้วย Outlook/โปรแกรมอ่านเมลได้ ใช้ตรวจเนื้อความได้เต็ม ๆ โดยไม่ต้องมี SMTP
    /// - production : ปล่อย pickupDirectory ว่าง → SmtpClient อ่านค่า host/port จาก
    ///                &lt;system.net&gt;&lt;mailSettings&gt; ใน Web.config ตามปกติ
    ///
    /// ส่งไม่สำเร็จจะโยน exception ออกไปตามสัญญาของ <see cref="IEmailSender"/>
    /// ผู้เรียก (RequestNotificationService) เป็นคนตัดสินใจว่าจะทำอย่างไรต่อ
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        private readonly string _fromAddress;
        private readonly string _fromDisplayName;
        private readonly string _pickupDirectory;

        public SmtpEmailSender(string fromAddress, string fromDisplayName, string pickupDirectory)
        {
            if (string.IsNullOrWhiteSpace(fromAddress))
                throw new ArgumentException("ต้องระบุอีเมลผู้ส่ง", nameof(fromAddress));

            _fromAddress = fromAddress.Trim();
            _fromDisplayName = string.IsNullOrWhiteSpace(fromDisplayName) ? null : fromDisplayName.Trim();
            _pickupDirectory = string.IsNullOrWhiteSpace(pickupDirectory) ? null : pickupDirectory.Trim();
        }

        public void Send(EmailMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var recipients = (message.To ?? new string[0]).Where(NotBlank).ToList();
            if (recipients.Count == 0)
                throw new InvalidOperationException("ไม่มีผู้รับอีเมล");

            using (var mail = new MailMessage())
            {
                mail.From = _fromDisplayName == null
                    ? new MailAddress(_fromAddress)
                    : new MailAddress(_fromAddress, _fromDisplayName, Encoding.UTF8);

                foreach (var address in recipients)
                    mail.To.Add(address.Trim());

                foreach (var address in (message.Cc ?? new string[0]).Where(NotBlank))
                    mail.CC.Add(address.Trim());

                // ข้อความเป็นภาษาไทยทั้งฉบับ จึงต้องบังคับ UTF-8 ทั้งหัวเรื่องและเนื้อความ
                mail.SubjectEncoding = Encoding.UTF8;
                mail.BodyEncoding = Encoding.UTF8;
                mail.Subject = message.Subject ?? string.Empty;
                mail.Body = message.HtmlBody ?? string.Empty;
                mail.IsBodyHtml = true;

                using (var client = new SmtpClient())
                {
                    if (_pickupDirectory != null)
                    {
                        Directory.CreateDirectory(_pickupDirectory);
                        client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
                        client.PickupDirectoryLocation = _pickupDirectory;
                    }

                    client.Send(mail);
                }
            }
        }

        private static bool NotBlank(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }
    }

    /// <summary>
    /// อ่าน template อีเมลจากไฟล์ (D28 — แก้ข้อความได้โดยไม่ต้อง build ใหม่)
    /// ไม่มีไฟล์ = คืน null เพื่อให้ service ใช้ template ที่ฝังมากับโค้ด
    /// </summary>
    public class FileEmailTemplateSource : IEmailTemplateSource
    {
        private readonly string _folder;

        public FileEmailTemplateSource(string folder)
        {
            _folder = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder);
        }

        public string TryRead(string templateName)
        {
            if (_folder == null || string.IsNullOrWhiteSpace(templateName))
                return null;

            // ชื่อ template มาจากโค้ดเท่านั้น แต่กันไว้ไม่ให้หลุดออกนอกโฟลเดอร์อยู่ดี
            var safeName = Path.GetFileName(templateName) + ".html";
            var path = Path.Combine(_folder, safeName);

            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
    }
}
