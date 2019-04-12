// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Represents the stages of workspace loading (used for IDE\Langauge Server integration)
    /// </summary>
    public enum ProgressStage
    {
        /// <summary>
        /// Indicates that the workspace definition is being computed.
        /// </summary>
        BuildingWorkspaceDefinition,

        /// <summary>
        /// Indicates that the specs are being parsed.
        /// </summary>
        Parse,

        /// <summary>
        /// Indicates the specs are being analyzed.
        /// </summary>
        Analysis,

        /// <summary>
        /// Indicates the specs are being converted.
        /// </summary>
        Conversion,
    }

    /// <summary>
    /// Event arguments class used to workspace loading send progress notifications.
    /// </summary>
    /// <remarks>
    /// Typically sent to the langauge server and IDE such as VSCode and VS.
    /// This must be kept in sync with the VSCode and VS extensions.
    /// {vscode extension location} Public\Src\FrontEnd\IDE\VsCode\client\src\workspaceLoadingNotification.ts
    /// </remarks>
    public class WorkspaceProgressEventArgs : EventArgs
    {
        /// <summary>
        /// The current stage of workspace parsing.
        /// </summary>
        public ProgressStage ProgressStage { get; private set; }

        /// <summary>
        /// The number of specs that have been processed.
        /// </summary>
        public int NumberOfProcessedSpecs { get; private set; }

        /// <summary>
        /// The total number of specs to be processed.
        /// </summary>
        /// <remarks>
        /// This field is only valid when the <see cref="ProgressStage"/> is not <see cref="ProgressStage.BuildingWorkspaceDefinition"/>
        /// </remarks>
        public int? TotalNumberOfSpecs { get; private set; }

        /// <nodoc/>
        public static WorkspaceProgressEventArgs Create(ProgressStage progressStage, int numberOfProcessedSpecs, int? totalNumberOfSpecs = null)
        {
            return new WorkspaceProgressEventArgs() { ProgressStage = progressStage, NumberOfProcessedSpecs = numberOfProcessedSpecs, TotalNumberOfSpecs = totalNumberOfSpecs };
        }
    }
}
