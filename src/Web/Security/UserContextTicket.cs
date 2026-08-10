using System;
using System.Web;
using Messenger.Application.Dtos;
using Messenger.Domain.Enums;

namespace Messenger.Web.Security
{
    /// <summary>
    /// แปลง <see cref="UserContext"/> ไป-กลับกับข้อความที่เก็บใน UserData
    /// ของ Forms Authentication ticket
    ///
    /// เก็บไว้ใน ticket (ซึ่งถูกเข้ารหัส) เพื่อไม่ต้อง query DB ทุก request
    /// ข้อแลกเปลี่ยน : ถ้า Admin เปลี่ยน role ให้ใคร คนนั้นต้อง login ใหม่
    /// จึงจะเห็นสิทธิ์ใหม่
    /// </summary>
    public static class UserContextTicket
    {
        private const char Separator = '|';
        private const int FieldCount = 9;

        public static string Serialize(UserContext user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var fields = new[]
            {
                Encode(user.EmpCode),
                Encode(user.FullName),
                Encode(user.DeptCode),
                Encode(user.UnitName),
                Encode(user.PhoneExt),
                Encode(user.Email),
                Encode(user.BranchCode),
                Encode(user.BranchName),
                Encode(RoleCodes.ToCode(user.Role))
            };

            return string.Join(Separator.ToString(), fields);
        }

        /// <summary>คืน null ถ้าข้อความไม่อยู่ในรูปแบบที่คาดไว้ (ticket เก่า/เสีย)</summary>
        public static UserContext Deserialize(string userData)
        {
            if (string.IsNullOrEmpty(userData))
                return null;

            var fields = userData.Split(Separator);
            if (fields.Length != FieldCount)
                return null;

            return new UserContext
            {
                EmpCode = Decode(fields[0]),
                FullName = Decode(fields[1]),
                DeptCode = Decode(fields[2]),
                UnitName = Decode(fields[3]),
                PhoneExt = Decode(fields[4]),
                Email = Decode(fields[5]),
                BranchCode = Decode(fields[6]),
                BranchName = Decode(fields[7]),
                Role = RoleCodes.Parse(Decode(fields[8]))
            };
        }

        // encode ทีละช่องเพื่อกันกรณีข้อมูลมีตัวคั่น '|' ปนอยู่
        private static string Encode(string value)
        {
            return HttpUtility.UrlEncode(value ?? string.Empty);
        }

        private static string Decode(string value)
        {
            return HttpUtility.UrlDecode(value ?? string.Empty);
        }
    }
}
