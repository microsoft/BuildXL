// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Processes
{
    /// <summary>
    /// Abstract class for launching sandboxed process pip externally.
    /// </summary>
    public abstract class ExternalSandboxedProcessPipExecutor
    {
        /// <summary>
        /// Sandboxed process info.
        /// </summary>
        public readonly SandboxedProcessInfo SandboxedProcessInfo;

        /// <summary>
        /// Temporary directory cleaner.
        /// </summary>
        protected readonly ITempDirectoryCleaner TempDirectoryCleaner;

        /// <summary>
        /// Cancellation token.
        /// </summary>
        protected readonly CancellationToken CancellationToken;

        /// <summary>
        /// Initializes sandboxed process info.
        /// </summary>
        protected ExternalSandboxedProcessPipExecutor(
            SandboxedProcessInfo sandboxedProcessInfo,
            CancellationToken cancellationToken,
            ITempDirectoryCleaner tempDirectoryCleaner)
        {
            Contract.Requires(sandboxedProcessInfo != null);

            SandboxedProcessInfo = sandboxedProcessInfo;
            CancellationToken = cancellationToken;
            TempDirectoryCleaner = tempDirectoryCleaner;
        }

        /// <summary>
        /// Executes an instance of <see cref="SandboxedProcessInfo"/>.
        /// </summary>
        public abstract Task<Result> ExecuteAsync();

        /// <summary>
        /// Serializes sandboxed process info to file.
        /// </summary>
        protected Task<Possible<Unit>> TrySerializeInfoToFileAsync(string file)
        {
            return Task.Run<Possible<Unit>>(() =>
            {
                var maybeDeleted = FileUtilities.TryDeletePathIfExists(file, TempDirectoryCleaner);

                if (!maybeDeleted.Succeeded)
                {
                    return maybeDeleted.Failure;
                }

                try
                {
                    using (FileStream stream = File.OpenWrite(file))
                    {
                        CancellationToken.ThrowIfCancellationRequested();

                        SandboxedProcessInfo.Serialize(stream);
                    }
                }
                catch (OperationCanceledException canceledException)
                {
                    return new Failure<string>(canceledException.Message);
                }
                catch (IOException ioException)
                {
                    return new Failure<string>(ioException.Message);
                }

                return Unit.Void;
            });
        }

        /// <summary>
        /// Deserializes result from file.
        /// </summary>
        protected Task<Possible<SandboxedProcessResult>> TryDeserializeResultFromFileAsync(string file)
        {
            return Task.Run<Possible<SandboxedProcessResult>>(() =>
            {
                if (!FileUtilities.FileExistsNoFollow(file))
                {
                    return new Failure<string>($"File '{file}' does not exist");
                }

                try
                {
                    using (FileStream stream = File.OpenRead(file))
                    {
                        CancellationToken.ThrowIfCancellationRequested();
                        return SandboxedProcessResult.Deserialize(stream);
                    }
                }
                catch (OperationCanceledException canceledException)
                {
                    return new Failure<string>(canceledException.Message);
                }
                catch (IOException ioException)
                {
                    return new Failure<string>(ioException.Message);
                }
            });
        }

        /// <summary>
        /// Gets <see cref="SandboxedProcessExecutorExitCode"/> from a raw external process exit code.
        /// </summary>
        protected SandboxedProcessExecutorExitCode FromExternalProcessExitCode(int externalRawProcessExitCode)
            => Enum.IsDefined(typeof(SandboxedProcessExecutorExitCode), externalRawProcessExitCode)
            ? (SandboxedProcessExecutorExitCode)externalRawProcessExitCode
            : SandboxedProcessExecutorExitCode.InternalError;

        /// <summary>
        /// Result of external execution.
        /// </summary>
        public class Result
        {
            /// <summary>
            /// Exit code.
            /// </summary>
            public readonly SandboxedProcessExecutorExitCode ExitCode;

            /// <summary>
            /// Sandboxed process result.
            /// </summary>
            public readonly SandboxedProcessResult SandboxedProcessResult;

            /// <summary>
            /// Standard output.
            /// </summary>
            public readonly string StandardOutput;

            /// <summary>
            /// Standard error.
            /// </summary>
            public readonly string StandardError;

            /// <summary>
            /// Failure that is caused when calling <see cref="ExecuteAsync"/>.
            /// </summary>
            public readonly Failure ExecutionFailure;

            /// <summary>
            /// Checks if calling <see cref="ExecuteAsync"/> succeeded.
            /// </summary>
            public bool Succeeded => ExecutionFailure == null;

            private Result(
                SandboxedProcessExecutorExitCode exitCode,
                SandboxedProcessResult sandboxedProcessResult,
                string standardOutput,
                string standardError,
                Failure executionFailure)
            {
                ExitCode = exitCode;
                SandboxedProcessResult = sandboxedProcessResult;
                StandardOutput = standardOutput;
                StandardError = standardError;
                ExecutionFailure = executionFailure;
            }

            /// <summary>
            /// Creates a result for failure in calling <see cref="ExecuteAsync"/>.
            /// </summary>
            public static Result CreateForExecutionFailure(Failure failure)
            {
                Contract.Requires(failure != null);

                return new Result(
                    default,
                    null,
                    null,
                    null,
                    failure);
            }

            /// <summary>
            /// Creates a result for failure in calling <see cref="ExecuteAsync"/>.
            /// </summary>
            public static Result CreateForExecutionFailure(
                SandboxedProcessExecutorExitCode exitCode,
                string standardOutput,
                string standardError,
                Failure failure)
            {
                Contract.Requires(failure != null);

                return new Result(
                    exitCode,
                    null,
                    standardOutput,
                    standardError,
                    failure);
            }

            /// <summary>
            /// Creates a result for successful call to <see cref="ExecuteAsync"/>.
            /// </summary>
            public static Result CreateForSuccessfulExecution(
                SandboxedProcessExecutorExitCode exitCode,
                SandboxedProcessResult sandboxedProcessResult,
                string standardOutput,
                string standardError)
            {
                Contract.Requires(sandboxedProcessResult != null);
                return new Result(
                    exitCode,
                    sandboxedProcessResult,
                    standardOutput,
                    standardError,
                    null);
            }
        }
    }
}
