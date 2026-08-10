using System.Collections.Generic;
using Messenger.Domain.Entities;

namespace Messenger.Application.Abstractions
{
    /// <summary>เข้าถึงข้อมูลสาขา (master data)</summary>
    public interface IBranchRepository
    {
        IReadOnlyList<Branch> ListActive();
    }
}
