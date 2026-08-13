using System;
using System.Globalization;
using System.IO;
using System.Web;
using System.Web.Mvc;

namespace Messenger.Web
{
    /// <summary>
    /// สร้าง URL ของไฟล์ static (css/js) พร้อม "ลายเซ็นเวอร์ชัน" ต่อท้าย
    /// เช่น <c>/Content/site.css?v=638901234567890123</c>
    ///
    /// ทำไมต้องมี: IIS ส่งไฟล์ static มาโดยไม่มี <c>Cache-Control</c> เบราว์เซอร์จึงใช้
    /// heuristic caching ของตัวเอง แล้วอาจหยิบไฟล์เก่าใน cache มาใช้ต่อโดยไม่ถาม server เลย
    /// อาการที่เจอจริง: หลังแก้ <c>site.css</c> เพิ่มกฎของช่องวันที่ (D34) เครื่องที่ยังถือ
    /// css เก่าอยู่จะไม่มีกฎ <c>.date-field-picker > input[type="date"] { opacity: 0 }</c>
    /// ทำให้ <c>&lt;input type="date"&gt;</c> ตัวจริงโผล่ขึ้นมาให้เห็น และวาดเป็น mm/dd/yyyy
    /// ตามภาษาของเบราว์เซอร์ — คือกลับไปเป็นอาการเดิมที่ D34 ตั้งใจแก้
    ///
    /// พอ query string เปลี่ยนตามเวลาแก้ไขไฟล์ URL ก็กลายเป็นคนละตัวในสายตา cache
    /// เบราว์เซอร์จึงถูกบังคับให้โหลดใหม่เอง ผู้ใช้ไม่ต้อง Ctrl+F5 และไม่ต้องคอยบอกให้ล้าง cache
    /// </summary>
    public static class AssetUrl
    {
        /// <summary>
        /// คืน URL ของไฟล์ static พร้อม <c>?v=</c> ที่อิงเวลาแก้ไขไฟล์ล่าสุด
        /// ถ้าหาไฟล์ไม่เจอ (หรืออ่านไม่ได้) จะคืน URL ธรรมดาแทน ไม่ทำให้หน้าเว็บพัง
        /// </summary>
        /// <param name="url">UrlHelper ของ view ที่เรียกใช้</param>
        /// <param name="virtualPath">path แบบ <c>~/Content/site.css</c></param>
        public static string Asset(this UrlHelper url, string virtualPath)
        {
            if (url == null)
            {
                throw new ArgumentNullException("url");
            }

            if (string.IsNullOrWhiteSpace(virtualPath))
            {
                throw new ArgumentException("ต้องระบุ virtualPath", "virtualPath");
            }

            var resolved = url.Content(virtualPath);
            var stamp = VersionStamp(virtualPath);

            if (stamp == null)
            {
                return resolved;
            }

            return resolved + (resolved.IndexOf('?') >= 0 ? "&v=" : "?v=") + stamp;
        }

        /// <summary>
        /// อ่านเวลาแก้ไขไฟล์ล่าสุดเป็น tick — ตั้งใจอ่านสดทุกครั้งที่ render ไม่ cache ไว้
        /// เพราะตอน dev จะได้แก้ css/js แล้วกด refresh เห็นผลทันทีโดยไม่ต้อง restart แอป
        /// (ราคาคือ stat ไฟล์ไม่กี่ครั้งต่อ request ซึ่งไม่มีนัยสำคัญกับระบบภายใน)
        /// </summary>
        private static string VersionStamp(string virtualPath)
        {
            try
            {
                var physicalPath = HostingPath(virtualPath);
                if (physicalPath == null || !File.Exists(physicalPath))
                {
                    return null;
                }

                return File.GetLastWriteTimeUtc(physicalPath)
                    .Ticks
                    .ToString(CultureInfo.InvariantCulture);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string HostingPath(string virtualPath)
        {
            var context = HttpContext.Current;
            if (context == null || context.Server == null)
            {
                return null;
            }

            return context.Server.MapPath(virtualPath);
        }
    }
}
