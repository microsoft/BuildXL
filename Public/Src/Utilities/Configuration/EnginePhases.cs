// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Phases of BuildXL engine.
    /// </summary>
    [SuppressMessage("Microsoft.Usage", "CA2217:DoNotMarkEnumsWithFlags")]
    [Flags]
    public enum EnginePhases : ushort
    {
        /// <summary>
        /// No phase is run.
        /// </summary>
        None = 0x00,

        /// <summary>
        /// Parses the configuration files
        /// </summary>
        ParseConfigFiles = 1,

        /// <summary>
        /// Initialize Resolvers
        /// </summary>
        InitializeResolvers = ParseConfigFiles << 1 | ParseConfigFiles,

        /// <summary>
        /// Parse a workspace
        /// </summary>
        ParseWorkspace = InitializeResolvers << 1 | InitializeResolvers,

        /// <summary>
        /// Computes semantic information for a workspace
        /// </summary>
        AnalyzeWorkspace = ParseWorkspace << 1 | ParseWorkspace, // Used only by DScript

        /// <summary>
        /// Parses the module files
        /// </summary>
        ParseModuleFiles = AnalyzeWorkspace << 1 | AnalyzeWorkspace,  // Used only by Xml

        /// <summary>
        /// Parses and loads the observer files
        /// </summary>
        ParseObserverFiles = ParseModuleFiles << 1 | ParseModuleFiles, // Used only by Xml

        /// <summary>
        /// Parses and loads the qualifier files
        /// </summary>
        ParseQualifierFiles = ParseObserverFiles << 1 | ParseObserverFiles, // Used only by Xml

        /// <summary>
        /// Parses the spec files
        /// </summary>
        ParseSpecFiles = ParseQualifierFiles << 1 | ParseQualifierFiles, // Used only by Xml

        /// <summary>
        /// Run engine until all parsing is done.
        /// </summary>
        Parse = ParseSpecFiles << 1 | ParseSpecFiles, // Used only by Xml

        /// <summary>
        /// Construction of the evaluation model (ast conversion).
        /// </summary>
        ConstructEvaluationModel = Parse << 1 | Parse, // Used only by DScript

        /// <summary>
        /// Run engine until evaluation of unresolved values is done.
        /// </summary>
        Evaluate = ConstructEvaluationModel << 1 | ConstructEvaluationModel,

        /// <summary>
        /// Run engine until scheduling is done.
        /// </summary>
        Schedule = Evaluate << 1 | Evaluate,

        /// <summary>
        /// Run engine until execution is done.
        /// </summary>
        Execute = Schedule << 1 | Schedule,
    }
}
