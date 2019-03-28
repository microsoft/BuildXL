// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Scheduler.Graph;

namespace PipExecutionSimulator
{
    public enum PriorityMode
    {
        Aggregate,
        ActualStartTime
    }

    public struct PipSpan
    {
        public NodeId Id;
        public int Thread;
        public ulong StartTime;
        public ulong Duration;

        public ulong EndTime
        {
            get
            {
                return StartTime + Duration;
            }
        }
    }

    /// <summary>
    /// Enumeration representing the types of pips.
    /// </summary>
    public enum PipType : byte
    {
        /// <summary>
        /// Unknown
        /// </summary>
        None,

        /// <summary>
        /// A write file pip.
        /// </summary>
        WriteFile,

        /// <summary>
        /// A copy file pip.
        /// </summary>
        CopyFile,

        /// <summary>
        /// A process pip.
        /// </summary>
        Process,

        /// <summary>
        /// A legacy msbuild project.
        /// </summary>
        LegacyMSBuildProject,

        /// <summary>
        /// A value pip
        /// </summary>
        Value,

        /// <summary>
        /// A specfile pip
        /// </summary>
        SpecFile,

        /// <summary>
        /// A module pip
        /// </summary>
        Module,

        /// <summary>
        /// A pip representing the hashing of a source file
        /// </summary>
        HashSourceFile,

        /// <summary>
        /// A pip representing the completion of a directory (after which it is immutable).
        /// </summary>
        SealDirectory
    }
}
