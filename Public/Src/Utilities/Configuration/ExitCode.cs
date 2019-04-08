// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Exit codes
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public readonly struct ExitCode
    {
        /// <summary>
        /// Numeric exit code based on ExitKind
        /// </summary>
        public static int FromExitKind(ExitKind exitKind)
        {
            switch (exitKind)
            {
                case ExitKind.InvalidCommandLine:
                    return 1;
                case ExitKind.BuildNotRequested:
                case ExitKind.BuildSucceeded:
                    return 0;
                case ExitKind.BuildFailedWithGeneralErrors:
                case ExitKind.BuildFailedWithPipErrors:
                case ExitKind.BuildFailedWithFileMonErrors:
                case ExitKind.BuildFailedWithMissingOutputErrors:
                case ExitKind.BuildFailedSpecificationError:
                case ExitKind.NoPipsMatchFilter:
                case ExitKind.BuildCancelled:
                case ExitKind.UserError:
                    return 1;
                case ExitKind.ConnectionToAppServerLost:
                case ExitKind.AppServerFailedToStart:
                case ExitKind.Aborted:
                    return 2;
                case ExitKind.InfrastructureError:
                    return 3;
                case ExitKind.OutOfDiskSpace:
                case ExitKind.DataErrorDriveFailure:
                    return 4;
                case ExitKind.BuildFailedTelemetryShutdownException:
                    return 5;
                case ExitKind.InternalError:
                    return -1;
                default:
                    Contract.Assert(false, "Unknown ExitKind" + exitKind.ToString());
                    throw new ArgumentException("Unknown ExitKind" + exitKind.ToString());
            }
        }
    }
}
