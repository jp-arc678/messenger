using System;
using System.IO;
using System.Linq;
using System.Text;
using Messenger.Application.Abstractions;

namespace Messenger.Infrastructure.Storage
{
    /// <summary>
    /// เก็บไฟล์รูปบน filesystem (BR-3 + D25)
    ///
    /// โฟลเดอร์รากตั้งค่าได้ใน Web.config และควรอยู่ "นอก web root" เพื่อไม่ให้
    /// ใครเปิดไฟล์ตรงผ่าน URL ได้ — การเข้าถึงรูปต้องผ่าน controller ที่ตรวจ
    /// สาขาและสิทธิ์ก่อนเสมอ (BR-6)
    ///
    /// ชื่อไฟล์จริงถูกตั้งโดยคลาสนี้เอง ไม่เคยใช้ชื่อที่ผู้ใช้ส่งมา และทุก path
    /// ที่แปลงกลับเป็น full path จะถูกตรวจว่ายังอยู่ใต้โฟลเดอร์รากจริง (กัน path traversal)
    /// </summary>
    public class PhotoFileStorage : IPhotoFileStorage
    {
        private readonly string _rootPath;

        public PhotoFileStorage(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("ต้องระบุโฟลเดอร์รากของที่เก็บรูป", nameof(rootPath));

            _rootPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
        }

        public string Save(byte[] content, string extension, string branchCode, string reqNo)
        {
            if (content == null || content.Length == 0)
                throw new ArgumentException("ไม่มีข้อมูลไฟล์", nameof(content));

            // แยกโฟลเดอร์ตามสาขาและเดือน เพื่อไม่ให้ไฟล์กองรวมกันเป็นแสนไฟล์ในโฟลเดอร์เดียว
            var relativeFolder = Path.Combine(SafeSegment(branchCode), DateTime.Now.ToString("yyyy-MM"));
            var fileName = SafeSegment(reqNo) + "-" + Guid.NewGuid().ToString("N").Substring(0, 12) + extension;
            var relativePath = Path.Combine(relativeFolder, fileName);

            var fullPath = ResolveFullPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, content);

            return relativePath;
        }

        public byte[] Read(string relativePath)
        {
            var fullPath = ResolveFullPath(relativePath);
            return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
        }

        public bool Delete(string relativePath)
        {
            var fullPath = ResolveFullPath(relativePath);
            if (!File.Exists(fullPath))
                return false;

            File.Delete(fullPath);
            return true;
        }

        private string ResolveFullPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("ไม่ได้ระบุที่อยู่ไฟล์", nameof(relativePath));

            var combined = Path.GetFullPath(Path.Combine(_rootPath, relativePath));

            // path ที่หลุดออกนอกโฟลเดอร์ราก (เช่นมี ..\ ปนมา) ถือเป็นการโจมตี
            if (!combined.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("ที่อยู่ไฟล์อยู่นอกโฟลเดอร์ที่กำหนด", nameof(relativePath));

            return combined;
        }

        /// <summary>เหลือไว้เฉพาะตัวอักษร/ตัวเลข/ขีด เพื่อให้ชื่อโฟลเดอร์-ไฟล์ปลอดภัยเสมอ</summary>
        private static string SafeSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            var builder = new StringBuilder(value.Length);
            foreach (var character in value.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                builder.Append(character);

            return builder.Length == 0 ? "unknown" : builder.ToString();
        }
    }
}
