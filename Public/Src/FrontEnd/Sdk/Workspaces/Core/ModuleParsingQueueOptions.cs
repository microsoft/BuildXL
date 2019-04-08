// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks.Dataflow;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Provides options for configuring a <see cref="ModuleParsingQueue"/>
    /// </summary>
    public sealed class ModuleParsingQueueOptions : ExecutionDataflowBlockOptions
    {
        /// <summary>
        /// When true, cancels parsing as soon as there is any failure.
        /// When false, tries to complete parsing as much as possible.
        /// </summary>
        public bool CancelOnFirstFailure { get; set; }
    }
}
