using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Web.Hosting;
using System.Web.Mvc;
using Messenger.Application.Abstractions;
using Messenger.Application.Services;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Email;
using Messenger.Infrastructure.Export;
using Messenger.Infrastructure.Repositories;
using Messenger.Infrastructure.Sso;
using Messenger.Infrastructure.Storage;
using Messenger.Web.Controllers;

namespace Messenger.Web.Composition
{
    /// <summary>
    /// Composition root — จุดเดียวในระบบที่ประกอบ implementation จริงเข้าด้วยกัน
    /// ชั้นอื่นรู้จักแต่ interface เท่านั้น
    /// </summary>
    public static class ServiceRegistry
    {
        public const string ConnectionStringName = "MessengerDb";

        /// <summary>ชื่อ appSetting ที่ชี้โฟลเดอร์เก็บไฟล์รูป (D25)</summary>
        public const string PhotoStorageRootSetting = "PhotoStorageRoot";

        // ---- อีเมล (BR-5 · D28) ----
        public const string EmailFromSetting = "EmailFrom";
        public const string EmailFromNameSetting = "EmailFromName";
        public const string EmailPickupDirectorySetting = "EmailPickupDirectory";
        public const string EmailTemplateFolderSetting = "EmailTemplateFolder";

        /// <summary>ค่าที่ใส่ใน EmailPickupDirectory เพื่อบอกว่า "ส่งจริงผ่าน SMTP"</summary>
        private const string NoPickupDirectory = "none";

        public static IDependencyResolver Build()
        {
            var connectionString = ReadConnectionString();

            // service เหล่านี้ไม่มี state จึงใช้ instance เดียวตลอดอายุ application ได้
            // (connection ถูกสร้างใหม่ทุกครั้งที่เรียก repository)
            IDbConnectionFactory connectionFactory = new SqlConnectionFactory(connectionString);
            IEmployeeRepository employees = new EmployeeRepository(connectionFactory);
            IBranchRepository branches = new BranchRepository(connectionFactory);
            IDeliveryRequestRepository requests = new DeliveryRequestRepository(connectionFactory);
            IRequestWorkflowRepository workflowRepository = new RequestWorkflowRepository(connectionFactory);
            IDeliveryPhotoRepository photoRepository = new DeliveryPhotoRepository(connectionFactory);
            IPhotoFileStorage photoStorage = new PhotoFileStorage(ResolvePhotoStorageRoot());

            // BR-5 + D28 — dev เขียนไฟล์ .eml, production ส่งผ่าน SMTP ตาม mailSettings
            IEmailSender emailSender = new SmtpEmailSender(
                fromAddress: ConfigurationManager.AppSettings[EmailFromSetting] ?? "messenger-noreply@localhost",
                fromDisplayName: ConfigurationManager.AppSettings[EmailFromNameSetting],
                pickupDirectory: ResolveEmailPickupDirectory());

            IEmailTemplateSource emailTemplates = new FileEmailTemplateSource(ResolveEmailTemplateFolder());

            // D3 — SSO ยังเป็น stub ในเฟส 0 เมื่อได้ contract จริงให้สลับบรรทัดนี้บรรทัดเดียว
            ISsoClient sso = new MockSsoClient();

            IClock clock = new SystemClock();

            IAuthService authService = new AuthService(sso, employees, branches);
            IDeliveryRequestService requestService = new DeliveryRequestService(requests, employees, clock);
            IRequestNotificationService notificationService =
                new RequestNotificationService(emailSender, emailTemplates, employees, clock);
            IRequestWorkflowService workflowService =
                new RequestWorkflowService(requests, workflowRepository, employees, notificationService, clock);
            IPhotoService photoService = new PhotoService(photoRepository, requests, photoStorage, clock);
            IReportService reportService = new ReportService(requests, clock);
            IReportExporter reportExporter = new ExcelReportExporter();

            var factories = new Dictionary<Type, Func<object>>
            {
                { typeof(IDbConnectionFactory), () => connectionFactory },
                { typeof(IEmployeeRepository), () => employees },
                { typeof(IBranchRepository), () => branches },
                { typeof(IDeliveryRequestRepository), () => requests },
                { typeof(IRequestWorkflowRepository), () => workflowRepository },
                { typeof(IDeliveryPhotoRepository), () => photoRepository },
                { typeof(IPhotoFileStorage), () => photoStorage },
                { typeof(IEmailSender), () => emailSender },
                { typeof(IEmailTemplateSource), () => emailTemplates },
                { typeof(IRequestNotificationService), () => notificationService },
                { typeof(ISsoClient), () => sso },
                { typeof(IClock), () => clock },
                { typeof(IAuthService), () => authService },
                { typeof(IDeliveryRequestService), () => requestService },
                { typeof(IRequestWorkflowService), () => workflowService },
                { typeof(IPhotoService), () => photoService },
                { typeof(IReportService), () => reportService },
                { typeof(IReportExporter), () => reportExporter },

                // controller ที่มี dependency ต้องลงทะเบียนไว้
                // (controller ที่ไม่มี dependency ปล่อยให้ MVC สร้างเองได้)
                { typeof(AccountController), () => new AccountController(authService) },
                { typeof(RequestsController), () => new RequestsController(requestService, workflowService, photoService) },
                { typeof(QueueController), () => new QueueController(workflowService, clock) },
                { typeof(PhotosController), () => new PhotosController(photoService) },
                { typeof(ReportsController), () => new ReportsController(reportService, reportExporter, clock) }
            };

            return new MessengerDependencyResolver(factories);
        }

        /// <summary>
        /// หาโฟลเดอร์เก็บรูปจาก Web.config (D25)
        ///
        /// ค่าว่าง = ใช้ ~\App_Data\Photos ของเว็บ (สะดวกตอน dev เพราะไม่ต้องตั้งอะไรเลย)
        /// ถ้าตั้งเป็น path สัมพัทธ์ จะอ้างอิงจากโฟลเดอร์ของเว็บเสมอ ส่วน path เต็ม
        /// (เช่น D:\MessengerPhotos) ใช้ตรง ๆ — แบบหลังคือที่แนะนำสำหรับ production
        /// </summary>
        private static string ResolvePhotoStorageRoot()
        {
            var configured = ConfigurationManager.AppSettings[PhotoStorageRootSetting];

            return string.IsNullOrWhiteSpace(configured)
                ? HostingEnvironment.MapPath("~/App_Data/Photos")
                : MapConfiguredPath(configured.Trim());
        }

        /// <summary>
        /// โฟลเดอร์ที่จะเขียนไฟล์ .eml แทนการส่งจริง (D28)
        ///
        /// คืน null = ส่งจริงผ่าน SMTP ตาม &lt;mailSettings&gt; ใน Web.config
        /// ซึ่งเกิดขึ้นเมื่อใส่ค่า "none" หรือลบ key ทิ้ง
        /// </summary>
        private static string ResolveEmailPickupDirectory()
        {
            var configured = ConfigurationManager.AppSettings[EmailPickupDirectorySetting];

            if (configured == null)
                return null;

            configured = configured.Trim();

            if (string.Equals(configured, NoPickupDirectory, StringComparison.OrdinalIgnoreCase))
                return null;

            if (configured.Length == 0)
                return HostingEnvironment.MapPath("~/App_Data/Mail");

            return MapConfiguredPath(configured);
        }

        private static string ResolveEmailTemplateFolder()
        {
            var configured = ConfigurationManager.AppSettings[EmailTemplateFolderSetting];

            return string.IsNullOrWhiteSpace(configured)
                ? HostingEnvironment.MapPath("~/App_Data/EmailTemplates")
                : MapConfiguredPath(configured.Trim());
        }

        /// <summary>path เต็มใช้ตรง ๆ ส่วน path สัมพัทธ์อ้างอิงจากโฟลเดอร์ของเว็บ</summary>
        private static string MapConfiguredPath(string configured)
        {
            if (configured.StartsWith("~"))
                return HostingEnvironment.MapPath(configured);

            if (Path.IsPathRooted(configured))
                return configured;

            return HostingEnvironment.MapPath("~/" + configured.TrimStart('\\', '/'));
        }

        private static string ReadConnectionString()
        {
            var setting = ConfigurationManager.ConnectionStrings[ConnectionStringName];
            if (setting == null || string.IsNullOrWhiteSpace(setting.ConnectionString))
            {
                throw new ConfigurationErrorsException(
                    $"ไม่พบ connection string ชื่อ '{ConnectionStringName}' ใน Web.config");
            }

            return setting.ConnectionString;
        }
    }
}
