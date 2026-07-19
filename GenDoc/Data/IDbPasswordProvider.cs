using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenDoc.Data
{
    public interface IDbPasswordProvider
    {
        string? Password { get; }
        void SetPassword(string password);
        void Clear();
    }
}
