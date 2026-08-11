using System;
using System.Collections.Generic;
using System.Configuration;
using System.Web.Mvc;
using Messenger.Application.Abstractions;
using Messenger.Application.Services;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Repositories;
using Messenger.Infrastructure.Sso;
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

            // D3 — SSO ยังเป็น stub ในเฟส 0 เมื่อได้ contract จริงให้สลับบรรทัดนี้บรรทัดเดียว
            ISsoClient sso = new MockSsoClient();

            IClock clock = new SystemClock();

            IAuthService authService = new AuthService(sso, employees, branches);
            IDeliveryRequestService requestService = new DeliveryRequestService(requests, employees, clock);
            IRequestWorkflowService workflowService =
                new RequestWorkflowService(requests, workflowRepository, employees, clock);

            var factories = new Dictionary<Type, Func<object>>
            {
                { typeof(IDbConnectionFactory), () => connectionFactory },
                { typeof(IEmployeeRepository), () => employees },
                { typeof(IBranchRepository), () => branches },
                { typeof(IDeliveryRequestRepository), () => requests },
                { typeof(IRequestWorkflowRepository), () => workflowRepository },
                { typeof(ISsoClient), () => sso },
                { typeof(IClock), () => clock },
                { typeof(IAuthService), () => authService },
                { typeof(IDeliveryRequestService), () => requestService },
                { typeof(IRequestWorkflowService), () => workflowService },

                // controller ที่มี dependency ต้องลงทะเบียนไว้
                // (controller ที่ไม่มี dependency ปล่อยให้ MVC สร้างเองได้)
                { typeof(AccountController), () => new AccountController(authService) },
                { typeof(RequestsController), () => new RequestsController(requestService, workflowService) },
                { typeof(QueueController), () => new QueueController(workflowService, clock) }
            };

            return new MessengerDependencyResolver(factories);
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
