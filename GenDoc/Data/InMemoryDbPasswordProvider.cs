using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenDoc.Data
{
    public class InMemoryDbPasswordProvider : IDbPasswordProvider
    {
        public string? Password { get; private set; }

        public void SetPassword(string password) => Password = password;
        public void Clear() => Password = null;
    }
}
