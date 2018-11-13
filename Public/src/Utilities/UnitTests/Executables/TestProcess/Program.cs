// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test.BuildXL.Executables.TestProcess
{
    /// <summary>
    /// Test process that performs filesystem operations
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Runs the process executing filesystem oeprations specified by command line args
        /// </summary>
        public static void Main(string[] args)
        {
            Executable p = new Executable(args);
            p.Run();
        }
    }
}
