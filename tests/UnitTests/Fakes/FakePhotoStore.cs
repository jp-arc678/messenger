using System;
using System.Collections.Generic;
using System.Linq;
using Messenger.Application.Abstractions;
using Messenger.Domain.Entities;

namespace Messenger.UnitTests.Fakes
{
    /// <summary>
    /// ที่เก็บข้อมูลรูปปลอม — จำลองการบังคับ BR-6 ของ stored procedure จริง
    /// (รูปของใบงานสาขาอื่นต้องมองไม่เห็นและลบไม่ได้)
    ///
    /// ต้องผูกกับ <see cref="FakeDeliveryRequestRepository"/> ตัวเดียวกับที่ service ใช้
    /// เพื่อให้รู้ว่าใบงานแต่ละใบอยู่สาขาไหน
    /// </summary>
    public class FakeDeliveryPhotoRepository : IDeliveryPhotoRepository
    {
        private readonly FakeDeliveryRequestRepository _requests;
        private readonly List<DeliveryPhoto> _photos = new List<DeliveryPhoto>();

        private int _nextPhotoId = 1;

        public FakeDeliveryPhotoRepository(FakeDeliveryRequestRepository requests)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        }

        public int Add(AddPhotoData data)
        {
            if (_requests.GetById(data.ReqId, data.BranchCode) == null)
                return 0;

            var photo = new DeliveryPhoto
            {
                PhotoId = _nextPhotoId++,
                ReqId = data.ReqId,
                PhotoType = data.PhotoType,
                FilePath = data.FilePath,
                FileName = data.FileName,
                FileSizeBytes = data.FileSizeBytes,
                CapturedAt = data.CapturedAt,
                CapturedBy = data.CapturedBy,
                CapturedByName = "พนักงาน " + data.CapturedBy
            };

            _photos.Add(photo);
            return photo.PhotoId;
        }

        public IReadOnlyList<DeliveryPhoto> ListByRequest(int reqId, string branchCode)
        {
            if (_requests.GetById(reqId, branchCode) == null)
                return new List<DeliveryPhoto>();

            return _photos
                .Where(p => p.ReqId == reqId)
                .OrderBy(p => p.CapturedAt)
                .ThenBy(p => p.PhotoId)
                .ToList();
        }

        public DeliveryPhoto GetById(int photoId, string branchCode)
        {
            var photo = _photos.FirstOrDefault(p => p.PhotoId == photoId);
            if (photo == null)
                return null;

            return _requests.GetById(photo.ReqId, branchCode) == null ? null : photo;
        }

        public int CountByRequest(int reqId, string branchCode)
        {
            return ListByRequest(reqId, branchCode).Count;
        }

        public bool Delete(int photoId, string branchCode)
        {
            var photo = GetById(photoId, branchCode);
            if (photo == null)
                return false;

            _photos.Remove(photo);
            return true;
        }
    }

    /// <summary>ที่เก็บไฟล์ปลอมในหน่วยความจำ — ไม่แตะดิสก์จริง</summary>
    public class FakePhotoFileStorage : IPhotoFileStorage
    {
        private readonly Dictionary<string, byte[]> _files =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        private int _counter = 1;

        public int SaveCount { get; private set; }

        public int DeleteCount { get; private set; }

        /// <summary>ตั้งให้ Save ล้มเหลว เพื่อทดสอบทางที่เขียนไฟล์ไม่ได้</summary>
        public bool FailOnSave { get; set; }

        public IReadOnlyCollection<string> StoredPaths => _files.Keys.ToList();

        public string Save(byte[] content, string extension, string branchCode, string reqNo)
        {
            SaveCount++;

            if (FailOnSave)
                return null;

            var path = branchCode + "\\" + reqNo + "-" + _counter++ + extension;
            _files[path] = content;
            return path;
        }

        public byte[] Read(string relativePath)
        {
            byte[] content;
            return _files.TryGetValue(relativePath ?? string.Empty, out content) ? content : null;
        }

        public bool Delete(string relativePath)
        {
            DeleteCount++;
            return _files.Remove(relativePath ?? string.Empty);
        }
    }
}
