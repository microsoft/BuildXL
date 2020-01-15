// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// InfiniteWaiter.exe - Executable which launches and blocks forever.

using System.Threading;

namespace Test.BuildXL.Executables.InfiniteWaiter
{
    class Program
    {
        static void Main(string[] args)
        {
            Thread.Sleep(Timeout.Infinite);
        }
    }
}
