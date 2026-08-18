using System;
using System.Security.Cryptography;
using System.Threading;
using System.Web;
using System.Web.Management;
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

        /// <summary>
        /// ตอบข้อความที่อ่านรู้เรื่องเมื่อผู้ใช้ส่งไฟล์ใหญ่เกิน <c>maxRequestLength</c> (UAT-01)
        ///
        /// ปัญหา : ASP.NET ตัด request ทิ้งตั้งแต่ตอนอ่าน entity body ซึ่งเกิด **ก่อน**
        /// เข้า controller การตรวจขนาด/ชนิดไฟล์ใน PhotoService จึงไม่มีโอกาสได้ทำงานเลย
        /// ผู้ใช้เห็นแค่หน้า error เปล่า ๆ ไม่รู้ว่าต้องเปลี่ยนไฟล์
        ///
        /// การขยาย maxRequestLength ไม่ใช่คำตอบ เพราะแค่ย้ายเส้นแบ่งไปที่ตัวเลขใหม่
        /// ที่ต้องแก้คือ "บอกผู้ใช้ให้รู้เรื่อง" ไม่ว่าเส้นแบ่งจะอยู่ที่เท่าไร
        /// </summary>
        protected void Application_Error(object sender, EventArgs e)
        {
            var error = Server.GetLastError();
            if (!IsRequestTooLarge(error))
                return;

            Server.ClearError();

            Response.Clear();
            Response.StatusCode = 413;                  // Payload Too Large
            Response.TrySkipIisCustomErrors = true;     // ไม่ให้ IIS เอาหน้า error ของตัวเองมาทับ
            Response.ContentType = "text/html";
            Response.ContentEncoding = System.Text.Encoding.UTF8;
            Response.Write(TooLargePageHtml());

            // CompleteRequest() ไม่ใช่ Response.End() — อย่างหลังทำงานด้วยการโยน
            // ThreadAbortException ซึ่งจะไปโผล่ใน log ราวกับมีข้อผิดพลาดจริงทุกครั้ง
            Context.ApplicationInstance.CompleteRequest();
        }

        private static bool IsRequestTooLarge(Exception error)
        {
            for (var current = error; current != null; current = current.InnerException)
            {
                var http = current as HttpException;
                if (http != null && http.WebEventCode == WebEventCodes.RuntimeErrorPostTooLarge)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// หน้าเปล่า ๆ ที่ไม่พึ่ง MVC เลย — ตอนนี้ request พังไปแล้วจึงเรียก view engine ไม่ได้
        /// ฝั่ง JavaScript ดักสถานะ 413 เองอยู่แล้ว (photo-upload.js) จึงไม่ต้องอ่านหน้านี้
        /// </summary>
        private static string TooLargePageHtml()
        {
            return
                "<!doctype html><html lang=\"th\"><head><meta charset=\"utf-8\" />" +
                "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />" +
                "<title>ไฟล์ใหญ่เกินไป</title></head>" +
                "<body style=\"font-family:system-ui,'Segoe UI',sans-serif;margin:2rem;line-height:1.6\">" +
                "<h1 style=\"font-size:1.25rem\">ไฟล์ที่ส่งมาใหญ่เกินกว่าที่ระบบรับได้</h1>" +
                "<p>ระบบรับรูปได้ไม่เกิน <strong>2 MB</strong> ต่อรูป และตามปกติหน้าเว็บจะย่อรูปให้อัตโนมัติก่อนส่ง</p>" +
                "<p>ถ้าเจอข้อความนี้ แปลว่าไฟล์ถูกส่งขึ้นมาทั้งก้อนโดยไม่ได้ย่อ — มักเกิดจาก" +
                " การเลือกไฟล์ที่<strong>ไม่ใช่รูปภาพ</strong> (เช่น PDF) หรือเบราว์เซอร์ปิด JavaScript ไว้</p>" +
                "<p>กรุณาเลือกไฟล์รูป <strong>JPG</strong> หรือ <strong>PNG</strong> แล้วลองใหม่อีกครั้ง</p>" +
                "<p><a href=\"javascript:history.back()\">ย้อนกลับไปหน้าที่แล้ว</a></p>" +
                "</body></html>";
        }
    }
}
