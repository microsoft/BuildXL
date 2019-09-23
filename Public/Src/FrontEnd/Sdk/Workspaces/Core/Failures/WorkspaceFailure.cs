// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;
using NotNull = JetBrains.Annotations.NotNullAttribute;

// TODO: this file is becoming too big, consider splitting into multiple files based on logical grouping.
namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Base class for all workspace-related failures
    /// </summary>
    public abstract class WorkspaceFailure : Failure
    {
        /// <inheritdoc />
        public override BuildXLException CreateException()
        {
            return new BuildXLException(Describe());
        }

        /// <inheritdoc />
        public override BuildXLException Throw()
        {
            throw CreateException();
        }

        /// <nodoc />
        public static WorkspaceFailure SpecOwnedByTwoModules(
            string firstModuleName,
            string firstModulePath,
            string specFullPath,
            string secondModuleName,
            string secondModulePath)
        {
            return new SpecOwnedByTwoModulesFailure(firstModuleName, firstModulePath, specFullPath, secondModuleName, secondModulePath);
        }
    }

    /// <summary>
    /// Miscellaneous failures during workspace parsing/creation.
    /// </summary>
    public sealed class GenericWorkspaceFailure : WorkspaceFailure
    {
        private readonly string m_reason;
        private readonly IReadOnlyCollection<Failure> m_innerFailures;

        /// <nodoc/>
        public GenericWorkspaceFailure(string reason, IReadOnlyCollection<Failure> innerFailures = null)
        {
            m_reason = reason;
            m_innerFailures = innerFailures;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(m_reason);

            if (m_innerFailures != null)
            {
                foreach (var innerFailure in m_innerFailures)
                {
                    builder.AppendLine(innerFailure.Describe());
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Failures were found during parsing
    /// </summary>
    public class ParsingFailure : WorkspaceFailure
    {
        /// <nodoc/>
        [NotNull]
        public ISourceFile SourceFile { get; }

        /// <nodoc/>
        [NotNull]
        public ModuleDescriptor OwningModule { get; }

        /// <nodoc/>
        public ParsingFailure(ModuleDescriptor descriptor, ISourceFile sourceFile)
        {
            Contract.Requires(sourceFile != null);

            OwningModule = descriptor;
            SourceFile = sourceFile;
        }

        /// <nodoc/>
        [NotNull]
        public virtual IReadOnlyList<Diagnostic> ParseDiagnostics => SourceFile.ParseDiagnostics;

        /// <inheritdoc/>
        public override string Describe()
        {
            return "One or more error occurred during parsing. See output for more details.";
        }
    }

    /// <summary>
    /// Disallowed modules were referenced in a source file
    /// </summary>
    /// <remarks>
    /// This is a failure that happens after a source file was created. So the extra diagnostics that represent the disallowed references
    /// are reported on top of the potential diagnostics the source file already has
    /// </remarks>
    public sealed class DisallowedModuleReferenceFailure : ParsingFailure
    {
        private readonly List<Diagnostic> m_diagnostics;

        /// <nodoc />
        public DisallowedModuleReferenceFailure(ModuleDescriptor descriptor, ISourceFile sourceFile, IEnumerable<Diagnostic> disallowedReferences)
            : base(descriptor, sourceFile)
        {
            m_diagnostics = sourceFile.ParseDiagnostics.Union(disallowedReferences).ToList();
        }

        /// <inheritdoc/>
        public override IReadOnlyList<Diagnostic> ParseDiagnostics => m_diagnostics;
    }

    /// <summary>
    /// Linter diagnostic in a source file
    /// </summary>
    /// <remarks>
    /// This is a failure that happens after a source file was created. So the extra diagnostic that represent the linter failure
    /// are reported on top of the potential diagnostics the source file already has
    /// </remarks>
    public sealed class LinterFailure : ParsingFailure
    {
        private readonly List<Diagnostic> m_diagnostics;

        /// <nodoc />
        public LinterFailure(ModuleDescriptor descriptor, ISourceFile sourceFile, Diagnostic diagnostic)
            : base(descriptor, sourceFile)
        {
            m_diagnostics = new List<Diagnostic> { diagnostic };
            m_diagnostics.AddRange(sourceFile.ParseDiagnostics);
        }

        /// <inheritdoc/>
        public override IReadOnlyList<Diagnostic> ParseDiagnostics => m_diagnostics;
    }

    /// <summary>
    /// A cycle is found in module references
    /// </summary>
    public sealed class CycleInModuleReferenceFailure : WorkspaceFailure
    {
        private readonly List<(string moduleName, string pathToFile)> m_cyclicChain;

        /// <nodoc />
        public CycleInModuleReferenceFailure(List<(string moduleName, string pathToFile)> cyclicChain)
        {
            Contract.Requires(cyclicChain != null);
            Contract.Requires(cyclicChain.Count > 0);

            m_cyclicChain = cyclicChain;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            var shortCycleDescription = string.Join(" -> ", m_cyclicChain.Concat(m_cyclicChain.Take(1)).Select(c => I($"'{c.moduleName}'")));
            var fullCycleDescription = string.Join(Environment.NewLine, m_cyclicChain.Select(DescribeEdge));
            return I($"Module dependency graph forms a cycle: {shortCycleDescription}.{Environment.NewLine}{fullCycleDescription}");
        }

        private static string DescribeEdge((string moduleName, string pathToFile) edge)
        {
            return I($"Module '{edge.moduleName}': {edge.pathToFile}");
        }
    }

    /// <summary>
    /// Failures were found during binding
    /// </summary>
    public sealed class BindingFailure : WorkspaceFailure
    {
        /// <nodoc/>
        [NotNull]
        public ISourceFile SourceFile { get; }

        /// <nodoc/>
        [NotNull]
        public ModuleDescriptor OwningModule { get; }

        /// <nodoc/>
        public BindingFailure(ModuleDescriptor descriptor, ISourceFile sourceFile)
        {
            Contract.Requires(sourceFile != null);
            Contract.Requires(sourceFile.BindDiagnostics.Count != 0);

            OwningModule = descriptor;
            SourceFile = sourceFile;
        }

        /// <nodoc/>
        [NotNull]
        public IReadOnlyList<Diagnostic> BindingDiagnostics => SourceFile.BindDiagnostics;

        /// <inheritdoc/>
        public override string Describe()
        {
            return "One or more error occurred during file analysis. See output for more details.";
        }
    }

    /// <nodoc />
    public static class ParseAndBindingFailureExtensions
    {
        /// <summary>
        /// Returns a list of parsing or binding diagnostics, or empty list if the <paramref name="failure"/> is not a binding or parsing failure.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1800:DoNotCastUnnecessarily")]
        public static IReadOnlyList<Diagnostic> TryGetDiagnostics([NotNull] this Failure failure)
        {
            Contract.Requires(failure != null, "failure != null");

            switch (failure)
            {
                case ParsingFailure pf: return pf.ParseDiagnostics;
                case BindingFailure bf: return bf.BindingDiagnostics;
                default: return CollectionUtilities.EmptyArray<Diagnostic>();
            }
        }
    }

    /// <summary>
    /// A spec to be parsed couldn't be read.
    /// </summary>
    public sealed class CannotReadSpecFailure : WorkspaceFailure
    {
        /// <summary>
        /// Sub-reason for not being able to read the spec
        /// </summary>
        public enum CannotReadSpecReason
        {
            /// <nodoc/>
            SpecDoesNotExist,

            /// <nodoc/>
            PathIsADirectory,

            /// <nodoc/>
            IoException,

            /// <nodoc/>
            ContentUnavailable,
        }

        private readonly string m_specPath;
        private readonly CannotReadSpecReason m_reason;

        /// <nodoc/>
        public CannotReadSpecFailure(string specPath, CannotReadSpecReason reason)
        {
            m_specPath = specPath;
            m_reason = reason;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"Cannot read spec '{m_specPath}'. Reason: {Enum.GetName(typeof(CannotReadSpecReason), m_reason)}");
        }
    }

    /// <summary>
    /// A configuration file could not be found
    /// </summary>
    public sealed class ConfigFileNotFoundFailure : WorkspaceFailure
    {
        private readonly string m_initialSearchPath;

        /// <nodoc/>
        public ConfigFileNotFoundFailure(string initialSearchPath)
        {
            m_initialSearchPath = initialSearchPath;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"A configuration file was not found. Search started at '{m_initialSearchPath}'");
        }
    }

    /// <summary>
    /// Configuration file is malformed
    /// </summary>
    public sealed class MalformedConfigurationFailure : WorkspaceFailure
    {
        private readonly string m_reason;

        /// <nodoc/>
        public MalformedConfigurationFailure(string reason)
        {
            m_reason = reason;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"Configuration file is malformed. {m_reason}");
        }
    }

    /// <summary>
    /// Configuration file is malformed
    /// </summary>
    public sealed class MalformedModuleConfigurationFailure : WorkspaceFailure
    {
        private readonly string m_reason;

        /// <nodoc/>
        public MalformedModuleConfigurationFailure(string reason)
        {
            m_reason = reason;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"Module configuration file is malformed. {m_reason}");
        }
    }

    /// <summary>
    /// Resolver kind is not known
    /// </summary>
    public sealed class UnknownResolverKind : WorkspaceFailure
    {
        private readonly string m_kind;

        /// <nodoc/>
        public UnknownResolverKind(string kind)
        {
            Contract.Requires(!string.IsNullOrEmpty(kind));

            m_kind = kind;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"Unknown resolver kind '{m_kind}'");
        }
    }

    /// <summary>
    /// Spec is not owned by a resolver
    /// </summary>
    public sealed class SpecNotOwnedByResolverFailure : WorkspaceFailure
    {
        private readonly string m_path;

        /// <nodoc/>
        public SpecNotOwnedByResolverFailure(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            m_path = path;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"Spec '{m_path}' is not owned by this resolver");
        }
    }

    /// <summary>
    /// Spec is not owned by a resolver
    /// </summary>
    public sealed class ModuleNotOwnedByThisResolver : WorkspaceFailure
    {
        private readonly ModuleDescriptor m_moduleDescriptor;

        /// <nodoc/>
        public ModuleNotOwnedByThisResolver(ModuleDescriptor moduleDescriptor)
        {
            m_moduleDescriptor = moduleDescriptor;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"Module '{m_moduleDescriptor.DisplayName}' is not owned by this resolver");
        }
    }

    /// <summary>
    /// A project reference points to a spec that is not part of the same module
    /// TODO: consider adding better provenance information (even though these type of
    /// references wil be gone in V2...)
    /// </summary>
    public sealed class SpecNotUnderAModuleFailure : WorkspaceFailure
    {
        private readonly ISourceFile m_sourceFile;
        private readonly string m_reference;
        private readonly string m_moduleName;

        /// <nodoc/>
        public SpecNotUnderAModuleFailure(ISourceFile sourceFile, string reference, string moduleName)
        {
            m_sourceFile = sourceFile;
            m_reference = reference;
            m_moduleName = moduleName;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"Project '{m_sourceFile.FileName}' under package '{m_moduleName}' is referencing project '{m_reference}', which is not under the same package.");
        }
    }

    /// <summary>
    /// Module resolver could not resolve all expected modules
    /// </summary>
    public sealed class CouldNotResolveAllExpectedModules : WorkspaceFailure
    {
        private readonly IWorkspaceModuleResolver m_moduleResolver;
        private readonly int m_expectedToResolve;
        private readonly int m_actuallyResolved;

        /// <nodoc/>
        public CouldNotResolveAllExpectedModules(IWorkspaceModuleResolver moduleResolver, int expectedToResolve, int actuallyResolved)
        {
            m_moduleResolver = moduleResolver;
            m_expectedToResolve = expectedToResolve;
            m_actuallyResolved = actuallyResolved;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"{m_moduleResolver.DescribeExtent()} was expected to resolve {m_expectedToResolve} modules, but actually resolved {m_actuallyResolved}");
        }
    }

    /// <summary>
    /// A configuration file could not be found
    /// </summary>
    public sealed class MalformedGlobExpressionFailure : WorkspaceFailure
    {
        private readonly string m_error;

        /// <nodoc/>
        public MalformedGlobExpressionFailure(string error)
        {
            m_error = error;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"Glob/GlobR call expression is malformed: {m_error}");
        }
    }

    /// <summary>
    /// One build specification file is owned by more than one module.
    /// </summary>
    public sealed class SpecOwnedByTwoModulesFailure : WorkspaceFailure
    {
        private readonly string m_firstModuleName;
        private readonly string m_firstModulePath;
        private readonly string m_specFullPath;
        private readonly string m_secondModuleName;
        private readonly string m_secondModulePath;

        /// <nodoc/>
        public SpecOwnedByTwoModulesFailure(string firstModuleName, string firstModulePath, string specFullPath, string secondModuleName, string secondModulePath)
        {
            Contract.Requires(!string.IsNullOrEmpty(firstModuleName));
            Contract.Requires(!string.IsNullOrEmpty(firstModulePath));
            Contract.Requires(!string.IsNullOrEmpty(specFullPath));
            Contract.Requires(!string.IsNullOrEmpty(secondModuleName));
            Contract.Requires(!string.IsNullOrEmpty(secondModulePath));

            m_firstModuleName = firstModuleName;
            m_firstModulePath = firstModulePath;
            m_specFullPath = specFullPath;
            m_secondModuleName = secondModuleName;
            m_secondModulePath = secondModulePath;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return
                I($"Build specification '{m_specFullPath}' is owned by two modules: '{m_firstModuleName}' and '{m_secondModuleName}'.{Environment.NewLine}") +
                I($"To resolve the issue, remove ownership for module configuration files at '{m_firstModulePath}' or at '{m_secondModulePath}'.");
        }
    }
}
