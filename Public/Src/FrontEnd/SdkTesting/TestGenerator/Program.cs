// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    /// <nodoc />
    public static class Program
    {
        /// <nodoc />
        public static int Main(string[] arguments)
        {
            var args = new Args(arguments);
            if (args.HelpDisplayed)
            {
                return 0;
            }

            var logger = new Logger();
            TestSuite testSuite;
            if (!TestSuite.TryCreateTestSuite(logger, args.TestFiles, args.LkgFiles, out testSuite))
            {
                Contract.Assume(logger.ErrorCount > 0);
                return 1;
            }

            if (!TestEmitter.WriteTestSuite(logger, testSuite, args.OutputFolder, args.SdksToTest))
            {
                Contract.Assume(logger.ErrorCount > 0);
                return 2;
            }

            return 0;
        }
    }
}
