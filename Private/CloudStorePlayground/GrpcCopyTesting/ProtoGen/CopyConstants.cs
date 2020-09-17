using System;
using System.Collections.Generic;
using System.Text;

namespace Helloworld
{
    public static class CopyConstants
    {
        public static readonly int BufferSize = 4096;

        public static readonly int PortNumber = 5005;

        public static readonly int BigSize = 40960;

        public static readonly string ExceptionHeader = "exception".ToLowerInvariant();

        public static readonly string MessageHeader = "message".ToLowerInvariant();

        public static readonly string CompressionHeader = "compression".ToLowerInvariant();
    }

    public class ThrottledException : Exception
    {
        public ThrottledException() : base() { }

        public ThrottledException(string message) : base(message) { }

    }
}
