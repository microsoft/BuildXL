// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ide.Generator
{
    /// <summary>
    /// CSharp project that represents a spec file
    /// </summary>
    internal sealed class VcxprojFile : MsbuildFile
    {
        public MultiValueDictionary<Project, object> ConstantsByProject;
        public MultiValueDictionary<Project, object> IncludeDirsByProject;

        internal VcxprojFile(
            Context context,
            AbsolutePath specFilePath)
            : base(context, specFilePath, ".vcxproj")
        {
            m_inputs = new List<AbsolutePath>();
            ConstantsByProject = new MultiValueDictionary<Project, object>();
            IncludeDirsByProject = new MultiValueDictionary<Project, object>();
        }

        internal override string GenerateConditionalForProject(Project project)
        {
            string configuration = "Debug";
            string platform = "X64";

            var friendlyQualifierName = Context.QualifierTable.GetCanonicalDisplayString(project.QualifierId);
            var normalizedFriendlyQualifier = friendlyQualifierName.ToLowerInvariant();
            if (normalizedFriendlyQualifier.Contains("release"))
            {
                configuration = "Release";
            }

            if (normalizedFriendlyQualifier.Contains("x86"))
            {
                platform = "Win32";
            }

            return $"'$(Configuration)|$(Platform)' == '{configuration}|{platform}'";
        }

        internal override void VisitProcess(Process process, ProcessType pipCategory)
        {
            var qualifier = process.Provenance.QualifierId;

            // TODO: After fixing the qualifier in the DS, I will start using the qualifier id instead of friendly qualifier name
                // Context.QualifierTable.GetQualifiedOutputDirectoryPart(Context.StringTable, qualifierId).ToString(Context.StringTable);
            var arguments = Context.GetArgumentsDataFromProcess(process);

            Project project;
            if (!ProjectsByQualifier.TryGetValue(qualifier, out project))
            {
                project = CreateProject(process);
                project.SetProperty("PlatformToolset", "v142");

                FillProjectConfigurations(project);
                AddHeaderFiles(project);

                ProjectsByQualifier.Add(qualifier, project);
            }

            switch (pipCategory)
            {
                case ProcessType.Cl:
                    IterateClArguments(project, arguments);
                    break;

                case ProcessType.Link:
                    IterateLinkArguments(project, arguments);

                    // If this is DS, use WorkingDirectory as an output folder.
                    var outputDir = ExtensionUtilities.IsScriptExtension(SpecFilePath.GetExtension(Context.PathTable).ToString(Context.StringTable)) ?
                        process.WorkingDirectory :
                        process.UniqueOutputDirectory;
                    SetOutputDirectory(project, outputDir, OutputDirectoryType.Build);

                    break;
            }
        }

        private void IterateLinkArguments(Project project, PipData arguments, bool isNested = false)
        {
            foreach (var arg in arguments)
            {
                var type = arg.FragmentType;
                if (type == PipFragmentType.AbsolutePath && !isNested)
                {
                    // Sources
                    var path = GetPathValue(arg);
                    project.AddRawReference(path);
                }
            }
        }

        private void AddHeaderFiles(Project project)
        {
            var dir = SpecDirectory.ToString(Context.PathTable);
            foreach (var headerFile in System.IO.Directory.GetFiles(dir, "*.h"))
            {
                project.AddItem("ClInclude", AbsolutePath.Create(Context.PathTable, headerFile));
            }
        }

        private static void FillProjectConfigurations(Project project)
        {
            Item item;
            project.AddItem("ProjectCapability", "DominoVC;NoVCDefaultBuildUpToDateCheckProvider");

            item = project.AddItem("ProjectConfiguration", "Debug|Win32");
            item.SetMetadata("Configuration", "Debug");
            item.SetMetadata("Platform", "Win32");

            item = project.AddItem("ProjectConfiguration", "Debug|x64");
            item.SetMetadata("Configuration", "Debug");
            item.SetMetadata("Platform", "x64");

            item = project.AddItem("ProjectConfiguration", "Release|Win32");
            item.SetMetadata("Configuration", "Release");
            item.SetMetadata("Platform", "Win32");

            item = project.AddItem("ProjectConfiguration", "Release|x64");
            item.SetMetadata("Configuration", "Release");
            item.SetMetadata("Platform", "x64");
        }

        private void IterateClArguments(Project project, PipData arguments, bool isNested = false)
        {
            Action<object> action = null;
            var commandLine = arguments.ToString(Context.PathTable);

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
                    AddSourceItem(path, project, "ClCompile");
                }
                else if (type == PipFragmentType.StringLiteral)
                {
                    var strValue = arg.GetStringIdValue().ToString(Context.StringTable);
                    if (strValue == "/I")
                    {
                        action = (obj) =>
                        {
                            if ((AbsolutePath)obj != SpecDirectory)
                            {
                                IncludeDirsByProject.Add(project, (AbsolutePath)obj);
                            }
                        };
                    }
                    else if (strValue == "/D")
                    {
                        action = (obj) =>
                        {
                            if (obj is PipData)
                            {
                                obj = ((PipData)obj).ToString(Context.PathTable);
                            }
                            else if (!(obj is string))
                            {
                                Contract.Assert(false, "Expecting string or PipData as a preprocessor definition argument");
                            }

                            ConstantsByProject.Add(project, (string)obj);
                        };
                    }
                }
                else if (type == PipFragmentType.NestedFragment)
                {
                    IterateClArguments(project, arg.GetNestedFragmentValue(), true);
                }
            }
        }
    }
}
