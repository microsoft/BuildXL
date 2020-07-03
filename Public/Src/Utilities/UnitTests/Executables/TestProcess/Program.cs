// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Test.BuildXL.Executables.TestProcess
{
    /// <summary>
    /// Test process that performs filesystem operations
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Runs the process executing filesystem operations specified by command line args
        /// </summary>
        public static void Main(string[] args)
        {
            Executable p = new Executable(args);
            p.Run();
        }
    }
}
