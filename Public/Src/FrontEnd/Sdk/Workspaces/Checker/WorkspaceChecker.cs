// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using TypeScript.Net.TypeChecking;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Checks a workspace using <see cref="Checker"/>
    /// </summary>
    /// <remarks>
    /// TODO: this will be eventually split into symbol binding and type checking, but for now Checker is a monolithic entity
    /// </remarks>
    public static class WorkspaceChecker
    {
        /// <summary>
        /// Checks all specs in a workspace for non-interactive scenarios, typically regular BuildXL invocation.
        /// </summary>
        /// TODO: Consider exposing an AbsolutePath as part of ISourceFile. We are doing to many conversions back and forth between string and AbsolutePath
        /// This class is an example of this, but there are other cases.
        public static ITypeChecker Check(PathTable pathTable, Workspace workspace, IWorkspaceStatistics stats, int degreeOfParallelism)
        {
            var checker = Create(
                pathTable,
                workspace,
                stats,
                degreeOfParallelism,
                nextMergeId: 0,
                nextNodeId: 0,
                nextSymbolId: 0,
                interactiveMode: false);

            // Triggering the analysis.
            checker.GetDiagnostics();

            return checker;
        }

        /// <summary>
        /// Creates a typechecker without checking all the specs (used by langauge service).
        /// </summary>
        public static ITypeChecker Create(PathTable pathTable, Workspace workspace, IWorkspaceStatistics stats, int degreeOfParallelism, int nextMergeId = 0, int nextNodeId = 0, int nextSymbolId = 0, bool interactiveMode = true)
        {
            // When in incremental mode (which is used by the IDE) then we want the "produce diagnostics" flag to be off.
            // The "produce diagnostic" flag is used to report syntax errors that may be present in the script.
            // One behavior difference of the type checker is that when produce diagnostics is false, the type checker
            // will return contextual types for things such as an object literal expression in a function call EVEN IF
            // the object literal expression cannot be assigned to the argument\parameter type of a call expression.
            var checker = Checker.CreateTypeChecker(
                new WorkspaceTypeCheckerHost(pathTable, workspace, stats),
                produceDiagnostics: true,
                degreeOfParallelism: degreeOfParallelism,
                trackFileToFileDependencies: workspace.TrackFileToFileDependencies,
                interactiveMode: interactiveMode,
                nextMergeId: nextMergeId,
                nextNodeId: nextNodeId,
                nextSymbolId: nextSymbolId);

            return checker;
        }

        private sealed class WorkspaceTypeCheckerHost : TypeCheckerHost
        {
            private readonly PathTable m_pathTable;
            private readonly Workspace m_workspace;
            private readonly IWorkspaceStatistics m_stats;

            /// <nodoc/>
            public WorkspaceTypeCheckerHost(PathTable pathTable, Workspace workspace, IWorkspaceStatistics stats)
            {
                m_pathTable = pathTable;
                m_workspace = workspace;
                m_stats = stats;
            }

            /// <inheritdoc/>
            public override ICompilerOptions GetCompilerOptions()
            {
                return CompilerOptions.Empty;
            }

            /// <inheritdoc/>
            public override ISourceFile[] GetSourceFiles()
            {
                return m_workspace.GetAllSourceFiles();
            }

            /// <inheritdoc/>
            public override ISourceFile GetSourceFile(string fileName)
            {
                var path = AbsolutePath.Create(m_pathTable, fileName);

                if (!m_workspace.ContainsSpec(path))
                {
                    Contract.Assert(false, fileName + " is not contained in the collection of parsed files");
                }

                return m_workspace.GetSourceFile(path);
            }

            /// <inheritdoc/>
            public override bool TryGetOwningModule(string fileName, out ModuleName moduleName)
            {
                var path = AbsolutePath.Create(m_pathTable, fileName);

                ParsedModule parsedModule = m_workspace.TryGetModuleBySpecFileName(path);
                if (parsedModule != null)
                {
                    moduleName = CreateModuleName(parsedModule.Definition);
                    return true;
                }

                moduleName = ModuleName.Invalid;
                return false;
            }

            /// <inheritdoc/>
            public override bool TryGetPreludeModuleName(out ModuleName preludeName)
            {
                if (m_workspace.PreludeModule != null)
                {
                    preludeName = CreateModuleName(m_workspace.PreludeModule.Definition);
                    return true;
                }

                preludeName = ModuleName.Invalid;
                return false;
            }

            /// <inheritdoc />
            public override bool IsPreludeFile(ISourceFile sourceFile)
            {
                if (m_workspace.PreludeModule != null)
                {
                    var filePath = sourceFile.GetAbsolutePath(m_pathTable);
                    return m_workspace.PreludeModule.Specs.ContainsKey(filePath);
                }

                return false;
            }

            private static ModuleName CreateModuleName(ModuleDefinition definition)
            {
                return new ModuleName(
                    name: definition.Descriptor.Name,
                    projectReferencesAreImplicit: definition.ResolutionSemantics == NameResolutionSemantics.ImplicitProjectReferences);
            }

            /// <inheritdoc/>
            public override void ReportSpecTypeCheckingCompleted(ISourceFile node, TimeSpan elapsed)
            {
                m_stats.SpecTypeChecking.Increment(elapsed, node.Path.AbsolutePath);
            }
        }
    }
}
