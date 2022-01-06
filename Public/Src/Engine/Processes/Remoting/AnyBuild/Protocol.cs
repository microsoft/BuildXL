// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Constants, helper methods, and descriptions for the protocol format between the Shim and AnyBuild.
    /// </summary>
    /// <remarks>
    /// The protocol between AnyBuild and the shim each use a length-prefixed string format. This is designed
    /// not for minimizing size but to minimize encoding time - we need to get message information between
    /// processes quickly, and memcpy is faster than UTF-8 conversion.
    ///
    /// The first 4 bytes in each packet are a direct copy of the int32 count of bytes in the remainder of
    /// the message. The message payload is UTF-16 characters as a string. There is no null termination of
    /// strings except where a message requires it.
    /// 
    /// Example when sending the string "Smessage" (a stdout message from AnyBuild to Shim) across the wire is below.
    /// The string is 8 characters long, so it requires 16 bytes in the payload, so the prefix integer is 16.
    ///
    ///     ----Message size--- ---'S'--- ---'m'--- ---'e'--- ---'s'--- ---'s'--- ---'a'--- ---'g'--- ---'e'---
    ///     0x10 0x00 0x00 0x00 0x53 0x00 0x6D 0x00 0x65 0x00 0x73 0x00 0x73 0x00 0x61 0x00 0x67 0x00 0x65 0x00
    ///
    /// .
    /// </remarks>
    internal static class Protocol
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
        /// It includes the following semicolon-delimited fields:
        /// - A process return/exit code
        /// - A disposition, one of 'C' for a cache hit, 'R' for remoting, 'L' for local execution.
        /// </summary>
        public const char ProcessCompleteMessagePrefix = 'C';

        public static string CreateProcessCompleteMessageSuffix(int exitCode, CommandExecutionDisposition disposition)
        {
            return $"{exitCode};{Disposition.ToProtocolDisposition(disposition)}";
        }

        /// <summary>
        /// Execution dispositions. See <see cref="ProcessCompleteMessagePrefix"/>.
        /// </summary>
        public static class Disposition
        {
            public const char CacheHit = 'C';
            public const char Remoted = 'R';
            public const char RanLocally = 'L';

            public static char ToProtocolDisposition(CommandExecutionDisposition disposition)
            {
                return disposition switch
                {
                    CommandExecutionDisposition.CacheHit => CacheHit,
                    CommandExecutionDisposition.Remoted => Remoted,
                    CommandExecutionDisposition.RanLocally => RanLocally,
                    _ => throw new InvalidDataException($"Unexpected disposition {disposition}"),
                };
            }

            public static CommandExecutionDisposition ToCommandExecutionDisposition(char protocolDisposition)
            {
                return protocolDisposition switch
                {
                    CacheHit => CommandExecutionDisposition.CacheHit,
                    Remoted => CommandExecutionDisposition.Remoted,
                    RanLocally => CommandExecutionDisposition.RanLocally,
                    _ => throw new InvalidDataException($"Unexpected disposition {protocolDisposition}"),
                };
            }
        }

        /// <summary>
        /// Shim -> AnyBuild string message prefix that runs a process. The following string is a null character
        /// delimited set of fields in the following order:
        ///
        /// * ProcessId - int.ToString() of the process ID of the shimmed process.
        /// * ParentProcessId - int.ToString() of the parent process ID of the shimmed process.
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
        /// R123\0456\0c:\windows\system32\cmd.exe\0/k\0c:\foo\06337782663572\0PATH=c:\windows\0SOMEENV=SomeVal\0\07785\06337782668997\0
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
