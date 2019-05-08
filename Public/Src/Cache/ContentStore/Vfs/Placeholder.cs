using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Vfs
{
    public class Placeholder
    {
        public static T Todo<T>(string message = null, T value = default)
        {
            return value;
        }
    }
}
