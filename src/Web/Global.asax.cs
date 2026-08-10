using System;
using System.Security.Cryptography;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.Security;
using Messenger.Web.Composition;
using Messenger.Web.Security;

namespace Messenger.Web
{
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            DependencyResolver.SetResolver(ServiceRegistry.Build());
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
        }

        /// <summary>
        /// ประกอบ <see cref="MessengerPrincipal"/> ขึ้นใหม่จาก Forms Authentication ticket
        /// ทุก request เพื่อให้ controller/view เข้าถึงสาขาและ role ของผู้ใช้ได้
        /// </summary>
        protected void Application_PostAuthenticateRequest(object sender, EventArgs e)
        {
            var cookie = Request.Cookies[FormsAuthentication.FormsCookieName];
            if (cookie == null || string.IsNullOrEmpty(cookie.Value))
                return;

            FormsAuthenticationTicket ticket;
            try
            {
                ticket = FormsAuthentication.Decrypt(cookie.Value);
            }
            catch (ArgumentException)
            {
                // cookie เสียหาย/ถูกแก้ → ถือว่ายังไม่ได้ login
                return;
            }
            catch (CryptographicException)
            {
                return;
            }

            if (ticket == null || ticket.Expired)
                return;

            var user = UserContextTicket.Deserialize(ticket.UserData);
            if (user == null)
                return;

            var principal = new MessengerPrincipal(user);
            Context.User = principal;
            Thread.CurrentPrincipal = principal;
        }
    }
}
