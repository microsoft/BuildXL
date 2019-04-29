// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Pips;
using BuildXL.Native.IO;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Describes a group of processes that have been launched during the build with the same executable. Maps a process executable to a collection of pips that ran the given executable.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification = "This class should not be called a Collection")]
    public sealed class ProcessDescriptor
    {
        #region Internal properties

        /// <summary>
        /// Internal dictionary that links pips to instances of this process.
        /// </summary>
        internal readonly ConcurrentDictionary<PipDescriptor, IReadOnlyCollection<ProcessInstanceDescriptor>> PipsThatExecuteTheProcessDictionary
            = new ConcurrentDictionary<PipDescriptor, IReadOnlyCollection<ProcessInstanceDescriptor>>();
        #endregion

        #region Public  properties

        /// <summary>
        /// The name with full path of the executable used to launch the process.
        /// </summary>
        public string ProcessExecutable { get; }

        /// <summary>
        /// Dictionary that links pips that ran the given executable to actual process instances.
        /// </summary>
        public IReadOnlyDictionary<PipDescriptor, IReadOnlyCollection<ProcessInstanceDescriptor>> PipsThatExecuteTheProcess { get { return PipsThatExecuteTheProcessDictionary; } }
        #endregion

        #region Internal methods
        internal void AddPip(PipDescriptor pip, uint processId, string processArgs, TimeSpan kernelTime, TimeSpan userTime, IOCounters ioCounters, DateTime creationTime, DateTime exiTime, uint exitCode, uint parentProcessId)
        {
            ProcessInstanceDescriptor processInstance = new ProcessInstanceDescriptor(processId, ProcessExecutable, processArgs, kernelTime, userTime, ioCounters, creationTime, exiTime, exitCode, parentProcessId);
            IReadOnlyCollection<ProcessInstanceDescriptor> items = PipsThatExecuteTheProcessDictionary.GetOrAdd(pip, (key) => new ConcurrentHashSet<ProcessInstanceDescriptor>());

            // This is pretty ugly: Doing down casting here so we can add elements to our read only collection
            // The collection is read only because we do no want to allow the Users of the SDK to change it. Unfortunately the only way .NET allows me to define such dictionary
            // is to specify its elements as a IReadOnlyCollection and down cast every time I need to modify it
            // Down casting here is pretty safe though. The collection is only created in this method and we know that it is always a ConcurrentDictionary.
            (items as ConcurrentHashSet<ProcessInstanceDescriptor>).Add(processInstance);
            pip.ReportedProcessesHashset.Add(processInstance);
        }

        /// <summary>
        /// Internal constructor
        /// </summary>
        /// <param name="processExecutable">The name of the process this object will describe</param>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Path strings are all lower case in the SDK")]
        internal ProcessDescriptor(string processExecutable)
        {
            ProcessExecutable = processExecutable.ToLowerInvariant();
        }
        #endregion
    }
}
