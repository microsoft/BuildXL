// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Represents the workspace loading state the language server application is currently in.
    /// </summary>
    /// <remarks>
    /// This must be kept in sync with the VSCode and VS extensions.
    /// {vscode extension location} Public\Src\FrontEnd\IDE\VsCode\client\src\workspaceLoadingNotification.ts
    /// </remarks>
    public enum WorkspaceLoadingState
    {
        /// <summary>
        /// Workspace loading is about to start.
        /// </summary>
        Init,

        /// <summary>
        /// Workspace loading is in progress.
        /// </summary>
        InProgress,

        /// <summary>
        /// Workspace loading has succeeded.
        /// </summary>
        Success,

        /// <summary>
        /// Workspace loading has failed.
        /// </summary>
        Failure,
    }

    /// <summary>
    /// Represents the status of workspace loading state sent to IDE (VSCode and VS)
    /// </summary>
    /// <remarks>
    /// This class has a TypeScript implementation used by the VS Code client. The two classes must be kept in sync.
    /// {vscode extension location} Public\Src\FrontEnd\IDE\VsCode\client\src\notifications\workspaceLoadingNotification.ts
    /// </remarks>
    [DataContract]
    public sealed class WorkspaceLoadingParams
    {
        /// <summary>
        /// The overall current workspace loading status the language server is in.
        /// </summary>
        [DataMember(Name = "status")]
        public WorkspaceLoadingState Status { get; set; }

        /// <summary>
        /// The current workspace loading progress is known by BuildXL.
        /// </summary>
        [DataMember(Name = "progressStage")]
        public ProgressStage Stage { get; set; }

        /// <summary>
        /// The number of specs that have been processed.
        /// </summary>
        [DataMember(Name = "numberOfProcessedSpecs")]
        public int NumberOfProcessedSpecs { get; set; }

        /// <summary>
        /// The total number of specs that will be processed.
        /// </summary>
        /// <remarks>
        /// Only valid when <see cref="ProgressStage"/> is not <see cref="ProgressStage.BuildingWorkspaceDefinition"/>
        /// </remarks>
        [DataMember(Name = "totalNumberOfSpecs")]
        public int? TotalNumberOfSpecs { get; set; }

        /// <nodoc />
        public static WorkspaceLoadingParams InProgress(WorkspaceProgressEventArgs ea)
        {
            return new WorkspaceLoadingParams
                   {
                       Status = WorkspaceLoadingState.InProgress,
                       Stage = ea.ProgressStage,
                       NumberOfProcessedSpecs = ea.NumberOfProcessedSpecs,
                       TotalNumberOfSpecs = ea.TotalNumberOfSpecs
                   };
        }

        /// <nodoc />
        public static WorkspaceLoadingParams Fail() => new WorkspaceLoadingParams() { Status = WorkspaceLoadingState.Failure };

        /// <nodoc />
        public static WorkspaceLoadingParams Success() => new WorkspaceLoadingParams() { Status = WorkspaceLoadingState.Success };

        /// <nodoc />
        public static WorkspaceLoadingParams Init() => new WorkspaceLoadingParams() { Status = WorkspaceLoadingState.Init };
    }
}
