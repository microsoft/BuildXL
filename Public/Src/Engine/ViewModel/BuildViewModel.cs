// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Utilities;

namespace BuildXL.ViewModel
{
    /// <summary>
    /// Provides access to internals of the build engine used to provide the user experience.
    /// This can be for the fancy console to provide progress or to emit azure devops markers.
    /// </summary>
    public class BuildViewModel
    {
        private Func<IEnumerable<PipReference>> m_retrieveExecutingProcessPips;

        /// <nodoc />
        public PipExecutionContext Context { get; private set; }

        /// <summary>
        /// Optional field to collect a build summary page
        /// </summary>
        public BuildSummary BuildSummary { get; set; }

        /// <summary>
        /// Gets the currently execution pips
        /// </summary>
        /// <returns></returns>
        public IEnumerable<PipReference> RetrieveExecutingProcessPips()
        {
            if (m_retrieveExecutingProcessPips != null)
            {
                return m_retrieveExecutingProcessPips();
            }

            return Enumerable.Empty<PipReference>();
        }

        /// <summary>
        /// Sets the context with contains PathTable, and other global objects
        /// </summary>
        public void SetContext(PipExecutionContext context)
        {
            Context = context;
        }

        /// <summary>
        /// Sets the function from the scheduler that provides the console logger access to currently executing pips
        /// </summary>
        public void SetSchedulerDetails(Func<IEnumerable<PipReference>> retrieveExecutingProcessPips)
        {
            m_retrieveExecutingProcessPips = retrieveExecutingProcessPips;
        }
    }
}
