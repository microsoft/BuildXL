// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.ContentStore.Synchronization
{
    /// <summary>
    /// Synchronous access to a directory via a lock file.
    /// </summary>
    public sealed class DirectoryLockFile : IDisposable
    {
        // Don't leave ugly byte-order-mark. Default StreamWriter is configured this way,
        // but we need the non-default StreamWriter to set leaveOpen=true.
        private static readonly Encoding UTF8WithoutBom = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private readonly Tracer _tracer = new Tracer(nameof(DirectoryLockFile));
        private readonly AbsolutePath _lockFilePath;
        private readonly IAbsFileSystem _fileSystem;
        private readonly TimeSpan _pollingInterval;
        private Stream _lockFile;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DirectoryLockFile"/> class.
        /// </summary>
        public DirectoryLockFile(IAbsFileSystem fileSystem, AbsolutePath lockFilePath, TimeSpan pollingInterval)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(lockFilePath != null);

            _fileSystem = fileSystem;
            _lockFilePath = lockFilePath;
            _pollingInterval = pollingInterval;
        }

        /// <summary>
        ///     AcquireAsync the lock, waiting as long as it takes or until the configured timeout.
        /// </summary>
        public async Task<LockAcquisitionResult> AcquireAsync(Context context, TimeSpan waitTimeout)
        {
            _tracer.Info(context, $"Acquiring lock file=[{_lockFilePath}]");

            _fileSystem.CreateDirectory(_lockFilePath.Parent);

            DateTime timeOutTime = DateTime.UtcNow + waitTimeout;
            Exception lastException = null;
            int? lastCompetingProcessId = null;
            while (DateTime.UtcNow < timeOutTime)
            {
                try
                {
                    // Anything other than FileShare.None is effectively ignored in Unix
                    FileShare fileShare = BuildXL.Utilities.OperatingSystemHelper.IsUnixOS ? FileShare.None : FileShare.Read;

                    _lockFile = await _fileSystem.OpenSafeAsync(
                        _lockFilePath, FileAccess.Write, FileMode.OpenOrCreate, fileShare);

                    using (var writer = new StreamWriter(_lockFile, UTF8WithoutBom, bufferSize: 4096, leaveOpen: true))
                    {
                        await writer.WriteLineAsync(
                            $"Lock acquired at {DateTime.UtcNow:O} by computer [{Environment.MachineName}] running command line [{Environment.CommandLine}] with process id [{Process.GetCurrentProcess().Id}]"
                        );
                    }
           
                    _tracer.Info(context, $"Acquired lock file=[{_lockFilePath}]");

                    await _lockFile.FlushAsync();

                    return LockAcquisitionResult.Acquired();
                }
                catch (IOException ioException)
                {
                    lastException = ioException;
                }
                catch (UnauthorizedAccessException accessException)
                {
                    lastException = accessException;
                }

                try
                {
                    string contents = await _fileSystem.TryReadFileAsync(_lockFilePath);
                    if (contents != null)
                    {
                        _tracer.Diagnostic(context, $"Lock file=[{_lockFilePath}] contains [{contents}]");
                        lastCompetingProcessId = TryExtractProcessIdFromLockFilesContent(contents);
                    }
                }
                catch (Exception readLockFileException)
                {
                    string message = readLockFileException is UnauthorizedAccessException ae ? ae.Message : readLockFileException.ToString();
                    // This is just extra cautious. We shouldn't fail hard being unable to get this diagnostic information.
                    _tracer.Info(
                        context,
                        $"Unable to read contents of lock file=[{_lockFilePath}] because [{message}]");
                }

                await Task.Delay(_pollingInterval);
            }

            string lastProcessIdText = lastCompetingProcessId == null ? string.Empty : " Competing process Id: " + lastCompetingProcessId;
            _tracer.Info(
                context,
                $"Timed out trying to acquire lock file=[{_lockFilePath}].{lastProcessIdText} Last exception was=[{lastException}]");

            return LockAcquisitionResult.Failed(waitTimeout, lastCompetingProcessId, TryGetProcessName(lastCompetingProcessId), lastException);
        }

        /// <inheritdoc />
        public void Dispose()
        {          
            if (_lockFile != null)
            {
                _lockFile.SetLength(0);
                _lockFile.Dispose();
                _lockFile = null;
            }
        }

        private static readonly Regex _extractProcessIdRegex = new Regex(@"] with process id \[(?<process_id>\d+)\]");

        private static int? TryExtractProcessIdFromLockFilesContent(string text)
        {
            var matchResult = _extractProcessIdRegex.Match(text);
            if (matchResult.Success)
            {
                if (int.TryParse(matchResult.Groups["process_id"].ToString(), out int value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string TryGetProcessName(int? pid)
        {
            try
            {
                if (pid != null)
                {
                    return Process.GetProcessById(pid.Value).ProcessName;
                }
            }
            // In case the process is gone by this time.
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }

            return null;
        }
    }
}
