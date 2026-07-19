using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenDoc.Services
{
    public interface IDatabaseUnlockService
    {
        bool TryUnlock(string password, out string? errorMessage);
    }
}
