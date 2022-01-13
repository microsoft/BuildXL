// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// #define FEATURE_ANYBUILD_PROCESS_REMOTING
#if FEATURE_ANYBUILD_PROCESS_REMOTING

using System;
using AnyBuild;

#nullable enable

namespace BuildXL.Processes.Remoting.AnyBuild
{
    /// <summary>
    /// Adapter for AnyBuild <see cref="IRemoteProcessResult"/>.
    /// </summary>
    internal class AnyBuildRemoteProcessPipResult : IRemoteProcessPipResult
    {
        /// <inheritdoc/>
        public bool ShouldRunLocally { init; get; }

        /// <inheritdoc/>
        public int? ExitCode { init; get; }

        /// <inheritdoc/>
        public string? StdOut { init;  get; }

        /// <inheritdoc/>
        public string? StdErr { init;  get; }

        /// <inheritdoc/>
        public RemoteResultDisposition Disposition { init; get; }

        /// <summary>
        /// Converts an instance of <see cref="IRemoteProcessResult"/> to <see cref="AnyBuildRemoteProcessPipResult"/>.
        /// </summary>
        public static AnyBuildRemoteProcessPipResult FromAnyBuildResult(IRemoteProcessResult result) =>
            new ()
            {
                ShouldRunLocally = result.ShouldRunLocally,
                ExitCode = result.ExitCode,
                StdOut = result.StdOut,
                StdErr = result.StdErr,
                Disposition = result.Disposition switch
                {
                    CommandExecutionDisposition.Unknown => RemoteResultDisposition.Unknown,
                    CommandExecutionDisposition.CacheHit => RemoteResultDisposition.CacheHit,
                    CommandExecutionDisposition.Remoted => RemoteResultDisposition.Remoted,
                    CommandExecutionDisposition.RanLocally => RemoteResultDisposition.RanLocally,
                    _ => throw new InvalidOperationException()
                }
            };
    }
}

#endif