// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Qualifier;

namespace BuildXL.Ide.Generator
{
    /// <summary>
    /// CSharp project that represents a spec file
    /// </summary>
    internal abstract class MsbuildFile
    {
        public const string QualifierConfigurationPropertyName = "configuration";
        public const string QualifierTargetFrameworkPropertyName = "targetFramework";

        protected readonly Context Context;

        protected List<AbsolutePath> m_inputs;

        /// <summary>
        /// The name of the project
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811")]
        public string Name { get; private set; }

        /// <summary>
        /// The project Guid
        /// </summary>
        public string Guid { get; }

        /// <summary>
        /// The relative path of the csproj file to the enlistment root
        /// </summary>
        public RelativePath RelativePath { get; }

        /// <summary>
        /// The full path to the project file (which is under Out/VS/Projects)
        /// </summary>
        public AbsolutePath Path { get; private set; }

        /// <summary>
        /// The full path to the spec file
        /// </summary>
        public AbsolutePath SpecFilePath { get; }

        /// <summary>
        /// The directory of the spec file
        /// </summary>
        public AbsolutePath SpecDirectory { get; }

        /// <summary>
        /// Represents the csc processes
        /// </summary>
        /// <remarks>
        /// For each qualifier, we have a separate process pip so we are creating projects for each of them.
        /// We will merge them later to find the unconditioned and conditoned properties and items.
        /// </remarks>
        public Dictionary<QualifierId, Project> ProjectsByQualifier { get; private set; }

        /// <summary>
        /// Project dependencies
        /// </summary>
        public List<MsbuildFile> ProjectReferences { get; private set; }

        /// <summary>
        /// The Sdk to emit
        /// </summary>
        public string Sdk { get; protected set;}

        internal MsbuildFile(
            Context context,
            AbsolutePath specFilePath,
            string projectExtension)
        {
            ProjectsByQualifier = new Dictionary<QualifierId, Project>();
            ProjectReferences = new List<MsbuildFile>();

            Context = context;
            Name = specFilePath.GetName(Context.PathTable).RemoveExtension(Context.StringTable).ToString(Context.StringTable);
            SpecFilePath = specFilePath;
            SpecDirectory = SpecFilePath.GetParent(Context.PathTable);

            // Add '.g' suffix only if the projects files will be in the spec root
            if (Context.CanWriteToSrc)
            {
                projectExtension = ".g" + projectExtension;
            }

            // Relative to enlistment root
            RelativePath = Context.GetRelativePath(SpecFilePath).ChangeExtension(Context.StringTable, PathAtom.Create(Context.StringTable, projectExtension));

            Guid = GenerateGuid(RelativePath.ToString(Context.StringTable));
            Path = Context.ProjectsRoot.Combine(Context.PathTable, RelativePath);
        }

        private static string GenerateGuid(string path)
        {
#pragma warning disable CA5351 // Do not use insecure cryptographic algorithm MD5.
            using (var hash = MD5.Create())
#pragma warning restore CA5351 // Do not use insecure cryptographic algorithm MD5.
            {
                byte[] bytesToHash = Encoding.UTF8.GetBytes(path);
                var resultingHash = hash.ComputeHash(bytesToHash, 0, bytesToHash.Length);

                return new Guid(resultingHash).ToString("B", CultureInfo.CurrentCulture).ToUpperInvariant();
            }
        }

        internal Project CreateProject(Process process)
        {
            var project = new Project(process.Provenance.QualifierId);
            
            // All projects in a msbuild file must use the same BuildXL value.
            // TODO: Check whether this is the same BuildXL value as the other projects in this msbuild file
            var value = process.Provenance.OutputValueSymbol.ToString(Context.SymbolTable);
            project.SetProperty("DominoValue", value);
            project.SetProperty("ProjectGuid", Guid);
            project.SetProperty("SpecRoot", SpecDirectory);

            var relativeSpecFile = Context.GetRelativePath(SpecFilePath).ToString(Context.StringTable);
            project.SetProperty("DominoSpecFile", relativeSpecFile);

            project.AddItem("None", SpecFilePath);

            var dirString = SpecFilePath.GetParent(Context.PathTable).ToString(Context.PathTable);
            var packagePath = System.IO.Path.Combine(dirString, "package" + Names.DotDscExtension);
            var packageConfigPath = System.IO.Path.Combine(dirString, Names.PackageConfigDsc);
            if (System.IO.File.Exists(packagePath))
            {
                project.AddItem("DominoPackageFile", AbsolutePath.Create(Context.PathTable, packagePath));
            }

            if (System.IO.File.Exists(packageConfigPath))
            {
                project.AddItem("DominoPackageConfigFile", AbsolutePath.Create(Context.PathTable, packageConfigPath));
            }

            return project;
        }

        internal abstract string GenerateConditionalForProject(Project project);

        internal virtual void VisitDirectory(SealDirectory sealDirectory)
        {
        }

        internal virtual void EndVisitingProject()
        {
        }

        internal void VisitProcesses(IEnumerable<ProcessWithType> categorizedPips)
        {
            foreach (var categorizedPip in categorizedPips)
            {
                var pipCategory = categorizedPip.Type;
                var process = categorizedPip.Process;
                VisitProcess(process, pipCategory);
            }
        }

        internal abstract void VisitProcess(Process process, ProcessType pipCategory);

        internal void AddSourceItem(AbsolutePath path, Project project, string tag, bool linkToGeneratedFolder = false)
        {
            var item = project.AddItem(tag, path);
            if (!Context.CanWriteToSrc)
            {
                // We only need link if the file goes outside of the projects cone.
                // If the item path and the link are the same path. VisualStudio will not render the file in the project tree.
                RelativePath relativePath;
                if (SpecDirectory.TryGetRelative(Context.PathTable, path, out relativePath) && relativePath.GetAtoms().Length > 1)
                {
                    item.SetMetadata("Link", relativePath.ToString(Context.StringTable));
                }
            }
            else if (linkToGeneratedFolder)
            {
                item.SetMetadata("Link", System.IO.Path.Combine(".generated", path.GetName(Context.PathTable).ToString(Context.StringTable)));
            }
        }

        internal Item AddPackageReference(Project project, string package, string version)
        {
            var item = project.AddItem("PackageReference", package);
            item.SetMetadata("version", version);
            return item;
        }

        internal object GetObjectValue(PipFragment arg)
        {
            if (arg.FragmentType == PipFragmentType.StringLiteral)
            {
                return arg.GetStringIdValue().ToString(Context.StringTable);
            }

            if (arg.FragmentType == PipFragmentType.NestedFragment)
            {
                return arg.GetNestedFragmentValue();
            }

            return GetPathValue(arg);
        }

        internal AbsolutePath GetPathValue(PipFragment arg)
        {
            Contract.Requires(arg.FragmentType == PipFragmentType.AbsolutePath);

            var path = arg.GetPathValue();
            m_inputs.Remove(path);

            return path;
        }

        // Creating a string that represents command line arguments for debug purposes
        [SuppressMessage("Microsoft.Performance", "CA1811")]
        internal void ProcessNestedArgument(PipData nested, StringBuilder builder)
        {
            foreach (var arg in nested)
            {
                switch (arg.FragmentType)
                {
                    case PipFragmentType.StringLiteral:
                        builder.AppendLine(arg.GetStringIdValue().ToString(Context.StringTable));
                        break;
                    case PipFragmentType.AbsolutePath:
                        builder.AppendLine(arg.GetPathValue().ToString(Context.PathTable));
                        break;
                    case PipFragmentType.NestedFragment:
                        builder.AppendLine();
                        builder.AppendLine();
                        ProcessNestedArgument(arg.GetNestedFragmentValue(), builder);
                        break;
                }
            }
        }

        /// <summary>
        /// Sets the project output directory
        /// </summary>
        internal void SetOutputDirectory(Project project, AbsolutePath outputDirectory, OutputDirectoryType outputDirectoryType)
        {
            string buildFilter;
            if (outputDirectoryType == OutputDirectoryType.TestDeployment)
            {
                var relativeOutputDir = Context.GetRelativePath(outputDirectory).ToString(Context.StringTable);
                buildFilter = $"output='Mount[SourceRoot]\\{System.IO.Path.Combine(relativeOutputDir, "*")}'";
            }
            else
            {
                var relativeSpecFile = Context.GetRelativePath(SpecFilePath).ToString(Context.StringTable);
                buildFilter = $"spec='Mount[SourceRoot]\\{relativeSpecFile}'";
            }

            project.SetOutputDirectory(outputDirectory, outputDirectoryType, buildFilter);
        }

        internal bool TryGetQualifierProperty(Project project, string propName, out string propValue)
        {
            return Context.QualifierTable.GetQualifier(project.QualifierId).TryGetValue(Context.StringTable, propName, out propValue);
        }

        internal bool QualifierPropertyEquals(Qualifier qualifier, string propertyName, string propertyValue)
        {
            return qualifier.TryGetValue(Context.StringTable, propertyName, out var value)
                && AreEqual(value, propertyValue);
        }

        internal bool AreEqual(string lhs, string rhs)
        {
            return StringComparer.InvariantCultureIgnoreCase.Equals(lhs, rhs);
        }

        internal bool AreNotEqual(string lhs, string rhs)
        {
            return !AreEqual(lhs, rhs);
        }
    }

    internal readonly struct EmbeddedResource
    {
        public readonly AbsolutePath ResxFile;
        public readonly AbsolutePath CsFile;

        public EmbeddedResource(AbsolutePath resxFile, AbsolutePath csFile)
        {
            ResxFile = resxFile;
            CsFile = csFile;
        }
    }
}
