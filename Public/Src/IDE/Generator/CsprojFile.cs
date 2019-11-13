// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Ide.Generator
{
    /// <summary>
    /// CSharp project that represents a spec file
    /// </summary>
    internal sealed class CsprojFile : MsbuildFile
    {
        /// <summary>
        /// Represents the resgen processes
        /// </summary>
        public Dictionary<QualifierId, List<EmbeddedResource>> ResourcesByQualifier { get; }

        private bool m_isTestProject;
        private bool m_isXunitTestProject;

        private readonly HashSet<string> m_projectTypeGuids = new HashSet<string>();

        internal CsprojFile(
            Context context,
            AbsolutePath specFilePath)
            : base(context, specFilePath, ".csproj")
        {
            var directory = specFilePath.GetParent(context.PathTable);

            // The list of files which are used by this process.
            // We will use this data to find non-compiled "Content" items.
            // I filter *.dll files because there were some dlls as inputs (e.g., System.dll in the project file directory) for the OSGTools
            m_inputs = context.EnumeratePipGraphFilesUnderDirectory(directory).Where(a => a.GetExtension(context.PathTable) != context.DllExtensionName).ToList();
            ResourcesByQualifier = new Dictionary<QualifierId, List<EmbeddedResource>>();
        }

        internal override void VisitProcess(Process process, ProcessType pipCategory)
        {
            var qualifier = Context.QualifierTable.GetQualifier(process.Provenance.QualifierId);

            // only consider processes targeting current os in debug configuration
            // also, additionally exclude projects targeting net451
            var currentRuntime = OperatingSystemHelper.IsMacOS ? "osx-x64" : "win-x64";
            if (!QualifierPropertyEquals(qualifier, "targetRuntime", currentRuntime)
                || !QualifierPropertyEquals(qualifier, QualifierConfigurationPropertyName, "debug")
                || QualifierPropertyEquals(qualifier, QualifierTargetFrameworkPropertyName, "net451"))
            {
                return;
            }

            var friendlyQualifier = process.Provenance.QualifierId;

            switch (pipCategory)
            {
                case ProcessType.XUnit:
                    WriteXunitDiscoverPackage();
                    ExtractOutputPathFromUnitTest(process, friendlyQualifier, 1);
                    break;
                case ProcessType.VsTest:
                    ExtractOutputPathFromUnitTest(process, friendlyQualifier, 0);
                    break;
                case ProcessType.Csc:
                    Project project = CreateProject(process);
                    PopulatePropertiesAndItems(project, process);
                    ProjectsByQualifier[friendlyQualifier] = project;

                    break;

                case ProcessType.ResGen:
                    ExtractResourceFromResGen(process, friendlyQualifier);
                    break;
            }
        }

        private void WriteXunitDiscoverPackage()
        {
            if (!m_isXunitTestProject)
            {
                var projectDir = Path.GetParent(Context.PathTable).ToString(Context.PathTable);
                Directory.CreateDirectory(projectDir);
                var path = System.IO.Path.Combine(projectDir, "packages.config");
                MsbuildWriter.WriteFile("BuildXL.Ide.Generator.CommonBuildFiles.packages.config", path);
                m_isXunitTestProject = true;
            }
        }

        internal override void VisitDirectory(SealDirectory sealDirectory)
        {
            if (ProjectsByQualifier.TryGetValue(sealDirectory.Provenance.QualifierId, out var project))
            {
                if (sealDirectory.Tags.Contains(Context.AssemblyDeploymentTag))
                {
                    SetOutputDirectory(project, sealDirectory.Directory.Path, OutputDirectoryType.AssemblyDeployment);
                }

                if (sealDirectory.Tags.Contains(Context.TestDeploymentTag))
                {
                    MakeTestProject();
                    SetOutputDirectory(project, sealDirectory.Directory.Path, OutputDirectoryType.TestDeployment);
                }
            }
        }

        private void ExtractOutputPathFromUnitTest(Process process, QualifierId qualifier, int position)
        {
            MakeTestProject();

            var arguments = Context.GetArgumentsDataFromProcess(process);
            Project project;
            if (ProjectsByQualifier.TryGetValue(qualifier, out project))
            {
                int i = 0;
                foreach (var arg in arguments)
                {
                    var type = arg.FragmentType;
                    if (i++ == position && type == PipFragmentType.AbsolutePath)
                    {
                        var path = GetPathValue(arg).GetParent(Context.PathTable);
                        SetOutputDirectory(project, path, OutputDirectoryType.TestDeployment);
                    }
                }
            }
        }

        private void MakeTestProject()
        {
            m_isTestProject = true;
            m_projectTypeGuids.Add("{60dc8134-eba5-43b8-bcc9-bb4bc16c2548}");
        }

        private void ExtractResourceFromResGen(Process process, QualifierId qualifier)
        {
            var arguments = Context.GetArgumentsDataFromProcess(process);

            // Assuming that there is only one .resx and one .resources file per resgen process.
            var resxFile = AbsolutePath.Invalid;
            var resourcesFile = AbsolutePath.Invalid;

            foreach (var arg in arguments)
            {
                var type = arg.FragmentType;
                if (type == PipFragmentType.AbsolutePath)
                {
                    var path = GetPathValue(arg);
                    var extension = path.GetExtension(Context.PathTable);
                    if (extension == Context.ResxExtensionName)
                    {
                        resxFile = path;
                    }
                    else if (extension == Context.ResourcesExtensionName)
                    {
                        resourcesFile = path;
                    }
                }
            }

            if (resxFile.IsValid && resourcesFile.IsValid)
            {
                // Change resources file to cs file (e.g., Resource.Designer.resources to Resource.Designer.cs)
                var resource = new EmbeddedResource(resxFile, resourcesFile.ChangeExtension(Context.PathTable, Context.CsExtensionName));
                AddResourceWithQualifier(qualifier, resource);
            }
        }

        private void AddResourceWithQualifier(QualifierId qualifier, EmbeddedResource resource)
        {
            if (ResourcesByQualifier.ContainsKey(qualifier))
            {
                ResourcesByQualifier[qualifier].Add(resource);
            }
            else
            {
                ResourcesByQualifier.Add(qualifier, new List<EmbeddedResource> { resource });
            }
        }

        private void PopulatePropertiesAndItems(Project project, Process process)
        {
            var arguments = Context.GetArgumentsDataFromProcess(process);
            IterateCscArguments(project, arguments);
            AddContentItems(project);
            AddEmbeddedResources(project);
        }

        internal override void EndVisitingProject()
        {
            foreach (var project in ProjectsByQualifier.Values)
            {
                var additionalProjectTypeGuidString = m_projectTypeGuids.Count != 0 ?
                    string.Join(";", m_projectTypeGuids.OrderBy(s => s)) + ";" :
                    string.Empty;
                project.SetProperty("ProjectTypeGuids", additionalProjectTypeGuidString + "{DABA23A1-650F-4EAB-AC72-A2AF90E10E37};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}");

                if (m_isTestProject)
                {
                    project.AddItem("Service", "{82A7F48D-3B50-4B1E-B82E-3ADA8210C358}");
                }
            }

            base.EndVisitingProject();
        }

        internal override string GenerateConditionalForProject(Project project)
        {
            var conjuncts = new List<string>();
            if (TryGetQualifierProperty(project, QualifierConfigurationPropertyName, out var conf))
            {
                conjuncts.Add($"'$(Configuration)' == '{conf}'");
            }

            if (TryGetQualifierProperty(project, QualifierTargetFrameworkPropertyName, out var targetFramework))
            {
                conjuncts.Add($"'$(TargetFramework)' == '{targetFramework}'");
            }

            return conjuncts.Any()
                ? string.Join(" And ", conjuncts)
                : "True";
        }

        private void AddContentItems(Project project)
        {
            foreach (var path in m_inputs)
            {
                AddSourceItem(path, project, "Content");
            }
        }

        private void AddEmbeddedResources(Project project)
        {
            List<EmbeddedResource> resources;
            if (!ResourcesByQualifier.TryGetValue(project.QualifierId, out resources))
            {
                return;
            }

            // Assuming that we iterate the processes in a topologically sorted way.
            // Dependencies of a process such as resgen have already been processed above
            // so we already extracted resources from the resgen dependencies.
            foreach (var resource in resources)
            {
                var item = project.AddItem("EmbeddedResource", resource.ResxFile);
                item.SetMetadata("LastGenOutput", resource.CsFile);

                // TODO: Find the compiled item (e.g., <Compile Include="Resource.Designer.cs">) and set its 'DependentUpon' metadata.
                // This will allow us to hide the automatically generated cs file (e.g., Resource.Designer.cs) in the file list in the VS.
                // var compiledItem = project.Items.FirstOrDefault(a => a.Key == "Compile").FirstOrDefault(a => (AbsolutePath)a.Include == resource.CsFile);
                // compiledItem?.SetMetadata("DependentUpon", resource.ResxFile);
            }
        }

        private void IterateCscArguments(Project project, PipData arguments, bool isNested = false)
        {
            Action<object> action = null;

            foreach (var arg in arguments)
            {
                var type = arg.FragmentType;

                if (action != null)
                {
                    action(GetObjectValue(arg));
                    action = null;
                }
                else if (type == PipFragmentType.AbsolutePath && !isNested)
                {
                    var path = GetPathValue(arg);
                    // paths under the project file are automatically added by the sdk
                    if (!path.IsWithin(Context.PathTable, Path.GetParent(Context.PathTable)))
                    {
                        AddSourceItem(path, project, "Compile");
                    }
                }
                else if (type == PipFragmentType.StringLiteral)
                {
                    var strValue = arg.GetStringIdValue().ToString(Context.StringTable);
                    switch (strValue)
                    {
                        case "/analyzer:":
                            action = (obj) => project.AddItem("Analyzer", (AbsolutePath)obj);
                            break;
                        case "/r:":
                        case "/link:":
                            action = (obj) => project.AddRawReference((AbsolutePath)obj);
                            break;
                        case "/langversion:":
                            action = (obj) => project.SetProperty("LangVersion", (string)obj);
                            break;
                        case "/target:":
                            action = (obj) => project.SetProperty("OutputType", (string)obj);
                            break;
                        case "/keyfile:":
                            action = (obj) =>
                                     {
                                         project.SetProperty("AssemblyOriginatorKeyFile", (AbsolutePath)obj);
                                         project.SetProperty("SignAssembly", bool.TrueString);
                                         project.SetProperty("DelaySign", bool.FalseString);
                                     };
                            break;
                        case "/define:":
                            action = (obj) => project.SetProperty("DefineConstants", (string)obj);
                            break;
                        case "/unsafe":
                            action = (obj) =>
                                     {
                                         if ((string)obj == "+")
                                         {
                                             project.SetProperty("AllowUnsafeBlocks", bool.TrueString);
                                         }
                                     };
                            break;
                        case "/unsafe+":
                            project.SetProperty("AllowUnsafeBlocks", bool.TrueString);
                            break;
                        case "/recurse:":
                            action = (obj) =>
                                     {
                                         var expandedPathWithWildCard = ((PipData)obj).ToString(Context.PathTable);
                                         var msbuildMatchString = expandedPathWithWildCard.Replace("*", "**");
                                         project.AddItem("Compile", msbuildMatchString);

                                         // if (!Context.CanWriteToSrc)
                                         // {
                                         //    // We only need link if the file goes outside of the projects cone.
                                         //    // If the item path and the link are the same path. VisualStudio will not render the file in the project tree.
                                         //    RelativePath relativePath;
                                         //    if (SpecDirectory.TryGetRelative(Context.PathTable, path, out relativePath) && relativePath.GetAtoms().Length > 1)
                                         //    {
                                         //        item.SetMetadata("Link", relativePath.ToString(Context.StringTable));
                                         //    }
                                         // }
                                     };
                            break;
                        case "/out:":
                            action = (obj) =>
                                     {
                                         var assemblyPath = (AbsolutePath)obj;

                                         // TODO: There should be only one assembly name for a csproj file
                                         var outputFileWithoutExt = assemblyPath.GetName(Context.PathTable).RemoveExtension(Context.StringTable);
                                         project.SetProperty("AssemblyName", outputFileWithoutExt);
                                         project.SetProperty("RootNamespace", outputFileWithoutExt);

                                         var outputDir = assemblyPath.GetParent(Context.PathTable);
                                         SetOutputDirectory(project, outputDir, OutputDirectoryType.Build);
                                     };
                            break;
                        case "/ruleset:":
                            action = (obj) =>
                            {
                                project.SetProperty("CodeAnalysisRuleSet", (AbsolutePath)obj);
                                project.SetProperty("RunCodeAnalysis", bool.TrueString);
                            };
                            break;
                        case "/additionalfile:":
                            action = (obj) => project.AddItem("AdditionalFiles", (AbsolutePath)obj);
                            break;
                        case "/features:":
                            action = obj =>
                            {
                                var features = ((PipData)obj).ToString(Context.PathTable);
                                project.SetProperty("Features", features);
                            };
                            break;
                        default:
                            const string Target = "/target:";
                            const string Define = "/define:";
                            const string Reference = "/r:";

                            if (strValue.StartsWith(Target, StringComparison.OrdinalIgnoreCase))
                            {
                                project.SetProperty("OutputType", strValue.Substring(Target.Length));
                                break;
                            }

                            if (strValue.StartsWith(Define, StringComparison.OrdinalIgnoreCase))
                            {
                                project.SetProperty("DefineConstants", strValue.Substring(Define.Length).Trim('"'));
                                break;
                            }

                            if (strValue.StartsWith(Reference, StringComparison.OrdinalIgnoreCase) &&
                                strValue.EndsWith("="))
                            {
                                string alias = strValue.Substring(Reference.Length).Split('=')[0];
                                action = (obj) => project.AddRawReference((AbsolutePath)obj, alias);
                                break;
                            }

                            break;
                    }
                }
                else if (type == PipFragmentType.NestedFragment)
                {
                    IterateCscArguments(project, arg.GetNestedFragmentValue(), true);
                }
            }
        }
    }
}
