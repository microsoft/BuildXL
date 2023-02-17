// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.JavaScript
{
    /// <nodoc/>
    public class JavaScriptUtilities
    {
        /// <summary>
        /// Returns the command line tool that each OS needs to use to create a new environment
        /// </summary>
        public static AbsolutePath GetCommandLineToolPath(PathTable pathTable) => 
            AbsolutePath.Create(pathTable, OperatingSystemHelper.IsWindowsOS
                ? Environment.GetEnvironmentVariable("COMSPEC")
                : Environment.GetEnvironmentVariable("SHELL") ?? "/usr/bin/bash");

        /// <summary>
        /// Appends the OS-specific arguments associated with the command line tool
        /// </summary>
        public static void AddCmdArguments(ProcessBuilder processBuilder) => 
            processBuilder.ArgumentsBuilder.Add(PipDataAtom.FromString(OperatingSystemHelper.IsWindowsOS ? "/C" : "-c"));

        /// <summary>
        /// Prepares the given arguments to be passed to a OS-specific command line tool
        /// </summary>
        public static string GetCmdArguments(string args) => 
            OperatingSystemHelper.IsWindowsOS ? $"/C \"{args}\"" : $"-c {CommandLineEscaping.EscapeAsCommandLineWord(args)}";
    }
}
