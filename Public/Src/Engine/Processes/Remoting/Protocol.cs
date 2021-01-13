// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// CODESYNC: AnyBuild src/Client/Shim.Shared/Protocol.cs
    /// </summary>
    public static class Protocol
    {
        /// <summary>
        /// AnyBuild -> Shim message that tells the Shim to run the process locally.
        /// </summary>
        public const char RunBuildLocallyMessage = 'L';

        /// <summary>
        /// AnyBuild -> Shim string message prefix that includes a stdout output. e.g. "SHello world".
        /// </summary>
        public const char StdoutMessagePrefix = 'S';

        /// <summary>
        /// AnyBuild -> Shim string message prefix that includes a stdout output. e.g. "ESome error occurred".
        /// </summary>
        public const char StderrMessagePrefix = 'E';

        /// <summary>
        /// AnyBuild -> Shim string message prefix that indicates the remote process has completed.
        /// It includes a return code, e.g. "C0" for a zero return code.
        /// </summary>
        public const char ProcessCompleteMessagePrefix = 'C';

        /// <summary>
        /// Shim -> AnyBuild string message prefix that runs a process. The following string is a null character
        /// delimited set of fields in the following order:
        ///
        /// * Command - the fully qualified path to the command to run.
        /// * Args - the argument string for the command.
        /// * WorkingDir - the working directory in which to run the command.
        /// * ShimProcessStartTimeTicks - a long.ToString() of the UTC tick count when the shim process started.
        /// * PEB - a Windows process Environment Block format string. Each var=value string is null-terminated,
        ///   and the PEB ends with an empty null-terminated string.
        /// * ShimChannelConnectTicks - a long.ToString() of the ticks measured while connecting to AnyBuild.
        /// * ShimCallStartTimeTicks - a long.ToString() of the UTC tick count when the shim process started.
        ///
        /// Example:
        /// Rc:\windows\system32\cmd.exe\0/k\0c:\foo\06337782663572\0PATH=c:\windows\0SOMEENV=SomeVal\0\07785\06337782668997\0
        /// .
        /// </summary>
        public const char RunProcessMessagePrefix = 'R';

        /// <summary>
        /// Shim -> AnyBuild string message prefix that includes an error log message. 
        /// </summary>
        public const char LogErrorMessagePrefix = 'E';

        /// <summary>
        /// Shim -> AnyBuild string message prefix that includes a warning log message. 
        /// </summary>
        public const char LogWarningMessagePrefix = 'W';

        /// <summary>
        /// Shim -> AnyBuild string message prefix that includes an info log message. 
        /// </summary>
        public const char LogInfoMessagePrefix = 'I';

        /// <summary>
        /// Shim -> AnyBuild string message prefix that includes a debug log message. 
        /// </summary>
        public const char LogDebugMessagePrefix = 'D';

        /// <summary>
        /// Shim -> AnyBuild string message prefix that includes a debug log message. 
        /// </summary>
        public const char LogFinestMessagePrefix = 'F';
    }
}
