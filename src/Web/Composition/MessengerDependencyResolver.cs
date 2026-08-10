using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;

namespace Messenger.Web.Composition
{
    /// <summary>
    /// ตัว resolve dependency แบบง่ายที่สุดของ MVC 5
    ///
    /// จงใจไม่ใช้ DI container สำเร็จรูป เพื่อไม่เพิ่ม dependency เข้ามาในเฟส 0
    /// (CLAUDE.md §10 ข้อ 8) — ถ้าภายหลังจำนวน service มากขึ้นจนดูแลยาก
    /// ค่อยพิจารณาเปลี่ยนเป็น container จริง
    ///
    /// type ที่ไม่ได้ลงทะเบียนจะคืน null ซึ่ง MVC จะ fallback ไปสร้างเองด้วย
    /// constructor ว่าง (พฤติกรรมมาตรฐาน)
    /// </summary>
    public class MessengerDependencyResolver : IDependencyResolver
    {
        private readonly IDictionary<Type, Func<object>> _factories;

        public MessengerDependencyResolver(IDictionary<Type, Func<object>> factories)
        {
            _factories = factories ?? throw new ArgumentNullException(nameof(factories));
        }

        public object GetService(Type serviceType)
        {
            return _factories.TryGetValue(serviceType, out var factory) ? factory() : null;
        }

        public IEnumerable<object> GetServices(Type serviceType)
        {
            var service = GetService(serviceType);
            return service == null ? Enumerable.Empty<object>() : new[] { service };
        }
    }
}
