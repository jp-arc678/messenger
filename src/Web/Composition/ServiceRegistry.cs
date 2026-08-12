using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Web.Hosting;
using System.Web.Mvc;
using Messenger.Application.Abstractions;
using Messenger.Application.Services;
using Messenger.Infrastructure.Data;
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

            // D3 — SSO ยังเป็น stub ในเฟส 0 เมื่อได้ contract จริงให้สลับบรรทัดนี้บรรทัดเดียว
            ISsoClient sso = new MockSsoClient();

            IClock clock = new SystemClock();

            IAuthService authService = new AuthService(sso, employees, branches);
            IDeliveryRequestService requestService = new DeliveryRequestService(requests, employees, clock);
            IRequestWorkflowService workflowService =
                new RequestWorkflowService(requests, workflowRepository, employees, clock);
            IPhotoService photoService = new PhotoService(photoRepository, requests, photoStorage, clock);

            var factories = new Dictionary<Type, Func<object>>
            {
                { typeof(IDbConnectionFactory), () => connectionFactory },
                { typeof(IEmployeeRepository), () => employees },
                { typeof(IBranchRepository), () => branches },
                { typeof(IDeliveryRequestRepository), () => requests },
                { typeof(IRequestWorkflowRepository), () => workflowRepository },
                { typeof(IDeliveryPhotoRepository), () => photoRepository },
                { typeof(IPhotoFileStorage), () => photoStorage },
                { typeof(ISsoClient), () => sso },
                { typeof(IClock), () => clock },
                { typeof(IAuthService), () => authService },
                { typeof(IDeliveryRequestService), () => requestService },
                { typeof(IRequestWorkflowService), () => workflowService },
                { typeof(IPhotoService), () => photoService },

                // controller ที่มี dependency ต้องลงทะเบียนไว้
                // (controller ที่ไม่มี dependency ปล่อยให้ MVC สร้างเองได้)
                { typeof(AccountController), () => new AccountController(authService) },
                { typeof(RequestsController), () => new RequestsController(requestService, workflowService, photoService) },
                { typeof(QueueController), () => new QueueController(workflowService, clock) },
                { typeof(PhotosController), () => new PhotosController(photoService) }
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

            if (string.IsNullOrWhiteSpace(configured))
                return HostingEnvironment.MapPath("~/App_Data/Photos");

            configured = configured.Trim();

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
