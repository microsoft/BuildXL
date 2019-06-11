// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace DetoursTesting
{
    /// <summary>
    /// Used by SubstituteProcessExecutionTests to test shimming. Receives the shimmed process
    /// command line including the executable name and arguments as the command line arguments,
    /// and the shimmed process current working directory and environment.
    /// </summary>
    public static class TestSubstituteProcessExecutionShimProgram
    {
        public static int Main(string[] args)
        {
            // Echo out the command line for validation in tests.
            Console.WriteLine("TestShim: Entered with command line: " + Environment.CommandLine);
            return 0;
        }
    }
}
