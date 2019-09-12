// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.Collections.CollectionUtilities;

namespace BuildXL.Ide.Generator
{
    internal sealed class MsbuildWriter
    {
        private const string CSharpProjectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
        private const string NativeProjectTypeGuid = "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}";
        private const string SolutionItemsGuid = "{11111111-1111-1111-1111-111111111111}";

        private static readonly XName ProjectXName = XName.Get("Project");
        private static readonly XName PropertyGroupXName = XName.Get("PropertyGroup");
        private static readonly XName ItemGroupXName = XName.Get("ItemGroup");
        private static readonly XName ImportXName = XName.Get("Import");

        public HashSet<string> ExcludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly IReadOnlyList<MsbuildFile> m_msbuildFiles;
        private readonly Context m_context;
        private readonly RootExpander m_rootExpander;

        private readonly Dictionary<string, string> m_stringReplacements;

        internal MsbuildWriter(IReadOnlyList<MsbuildFile> msbuildFiles, Context context)
        {
            m_msbuildFiles = msbuildFiles.Where(m => m.ProjectsByQualifier.Any()).ToList();
            m_context = context;

            m_rootExpander = new RootExpander(m_context.PathTable);
            m_rootExpander.Add(m_context.EnlistmentRoot, "$(EnlistmentRoot)");
            m_rootExpander.Add(m_context.SolutionRoot, "$(SolutionRoot)");

            m_stringReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            m_stringReplacements.Add(m_context.EnlistmentRootStr, "$(EnlistmentRoot)");
            m_stringReplacements.Add(m_context.SolutionRootStr, "$(SolutionRoot)");
        }

        internal void Write()
        {
            foreach (MsbuildFile msbuildFile in m_msbuildFiles)
            {
                string fileToWrite = msbuildFile.Path.ToString(m_context.PathTable);

                // Capture pre-existing items that were not considered before.
                var solutionRootExpression = GetFullPathExpression(m_context.SolutionRootStr, fileToWrite);

                var allProjects = msbuildFile.ProjectsByQualifier.Values;

                var commonPropertiesKvp = allProjects
                    .First()
                    .Properties
                    .Where(kvp => allProjects.All(p => EqualMsBuildValues(msbuildFile, p.TryGetProperty(kvp.Key), kvp.Value)))
                    .ToList();

                var condProperties =
                    allProjects.Select(
                        project => GetPropertyGroup(
                            msbuildFile.GenerateConditionalForProject(project),
                            GetProperties(msbuildFile, project.Properties.Except(commonPropertiesKvp))));

                var items =
                    msbuildFile.ProjectsByQualifier.Values.SelectMany(
                        project => GetItems(msbuildFile, project));

                XElement[] afterPropsImport = EmptyArray<XElement>();
                XElement[] beforePropsImport = EmptyArray<XElement>();
                XElement targetImport;
                if (msbuildFile is CsprojFile)
                {
                    beforePropsImport = new XElement[]
                    {
                        new XElement(ImportXName, new XAttribute("Project", Path.Combine("$(SolutionRoot)", "CSharp.props"))),
                    };
                    targetImport = new XElement(ImportXName, new XAttribute("Project", Path.Combine("$(SolutionRoot)", "CSharp.targets")));
                }
                else
                {
                    var vcxprojFile = (VcxprojFile)msbuildFile;
                    beforePropsImport = new XElement[]
                    {
                        new XElement(ImportXName, new XAttribute("Project", Path.Combine("$(SolutionRoot)", "Common.props"))),
                    };

                    var itemDefinition = new XElement(
                        XName.Get("ItemDefinitionGroup"),
                        new XElement(
                            XName.Get("ClCompile"),
                            GetConditionedValues(vcxprojFile, "PreprocessorDefinitions", vcxprojFile.ConstantsByProject),
                            GetConditionedValues(vcxprojFile, "AdditionalIncludeDirectories", vcxprojFile.IncludeDirsByProject)));

                    afterPropsImport = new XElement[]
                    {
                        new XElement(ImportXName, new XAttribute("Project", Path.Combine("$(VCTargetsPath)", "Microsoft.Cpp.Default.props"))),
                        new XElement(ImportXName, new XAttribute("Project", Path.Combine("$(VCTargetsPath)", "Microsoft.Cpp.props"))),
                        itemDefinition,
                    };
                    targetImport = new XElement(ImportXName, new XAttribute("Project", Path.Combine("$(VCTargetsPath)", "Microsoft.Cpp.targets")));
                }

                string targetFrameworks = null;
                if (msbuildFile is CsprojFile csprojFile)
                {
                    targetFrameworks = string.Join(";", csprojFile.ProjectsByQualifier.Values
                        .Select(project => csprojFile.TryGetQualifierProperty(project, MsbuildFile.QualifierTargetFrameworkPropertyName, out var tfm) ? tfm : null)
                        .Where(tfm => tfm != null)
                        .Distinct()
                        .OrderBy(tfm => tfm));
                }

                var projectFile = new XDocument(
                    new XElement(
                        ProjectXName,
                        targetFrameworks != null ? new[] { new XAttribute("Sdk", "Microsoft.NET.Sdk") } : new XAttribute[0],
                        new XElement(
                            PropertyGroupXName,
                            new XElement(XName.Get("SolutionRoot"), solutionRootExpression),
                            targetFrameworks != null ? new[] { new XElement(XName.Get("TargetFrameworks"), targetFrameworks) } : new XElement[0]),
                        beforePropsImport,
                        GetPropertyGroup(condition: null, GetProperties(msbuildFile, commonPropertiesKvp)),
                        condProperties,
                        afterPropsImport,
                        items,
                        targetImport));

                Directory.CreateDirectory(Path.GetDirectoryName(fileToWrite));

                // TODO: Handle errors more gracefully in case someone has a 'lock' on the files.
                using (var fs = UpdateFile(fileToWrite))
                {
                    projectFile.Save(fs);
                }
            }

            if (!Directory.Exists(m_context.SolutionRootStr))
            {
                Directory.CreateDirectory(m_context.SolutionRootStr);
            }

            WriteCommonImports(m_context.EnlistmentRootStr, m_context.SolutionRootStr);
            WriteSolution(m_context.PathTable, m_context.SolutionFilePathStr);

            if (!string.IsNullOrEmpty(m_context.DotSettingsPathStr))
            {
                File.Copy(m_context.DotSettingsPathStr, m_context.SolutionFilePathStr + ".DotSettings", overwrite: true);
            }
        }

        private bool EqualMsBuildValues(MsbuildFile msbuildFile, object lhs, object rhs)
        {
            return
                ValueToMsBuild(lhs, msbuildFile, isProperty: true) ==
                ValueToMsBuild(rhs, msbuildFile, isProperty: true);
        }

        private IEnumerable<XElement> GetConditionedValues(VcxprojFile vcxprojFile, string name, MultiValueDictionary<Project, object> valuesByProject)
        {
            var xName = XName.Get(name);
            var builder = new StringBuilder();

            HashSet<object> unconditionedValues = null;
            foreach (var qualifier in valuesByProject.Keys)
            {
                var values = valuesByProject[qualifier];
                if (unconditionedValues == null)
                {
                    unconditionedValues = new HashSet<object>(values);
                }
                else
                {
                    unconditionedValues.IntersectWith(values);
                }
            }

            if (unconditionedValues != null && unconditionedValues.Count > 0)
            {
                foreach (var dir in unconditionedValues)
                {
                    builder.Append(ValueToMsBuild(dir, vcxprojFile));
                    builder.Append(";");
                }

                yield return new XElement(xName, builder.ToString());
            }

            var qualifiers = new HashSet<string>();

            foreach (var project in valuesByProject.Keys)
            {
                var conditionedValues = valuesByProject[project].Except(unconditionedValues);
                if (conditionedValues.Any())
                {
                    builder.Clear();
                    foreach (var conditionedValue in conditionedValues)
                    {
                        builder.Append(ValueToMsBuild(conditionedValue, vcxprojFile));
                        builder.Append(";");
                    }

                    builder.Append($"%({name})");
                    yield return new XElement(
                        xName,
                        new XAttribute("Condition", vcxprojFile.GenerateConditionalForProject(project)),
                        builder.ToString());
                }
            }
        }

        [SuppressMessage("Microsoft.Globalization", "CA1305")]
        private IEnumerable<XElement> GetItems(MsbuildFile msbuildFile, Project project)
        {
            if (project.Items.Count == 0)
            {
                return EmptyArray<XElement>();
            }

            var items = project.Items.OrderBy(grouping => grouping.Key).Select(
                grouping => new XElement(
                    ItemGroupXName,
                    new XAttribute("Condition", msbuildFile.GenerateConditionalForProject(project)),
                    grouping
                        .Where(item => !IsExcludedItemInclude(ValueToMsBuild(item.Include, msbuildFile)))
                        .OrderBy(item => ValueToMsBuild(item.Include, msbuildFile), StringComparer.OrdinalIgnoreCase)
                        .Select(
                            item => new XElement(
                                XName.Get(grouping.Key),
                                new XAttribute("Include", ValueToMsBuild(item.Include, msbuildFile)),
                                item.Metadata
                                    .OrderBy(kv => kv.Key)
                                    .Select(kv => new XElement(XName.Get(kv.Key), ValueToMsBuild(kv.Value, msbuildFile)))))));

            return items;
        }

        private IEnumerable<XElement> GetProperties(MsbuildFile msbuildFile, IEnumerable<KeyValuePair<string, object>> properties, string condition = null)
        {
            return properties
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new XElement(XName.Get(kv.Key), ValueToMsBuild(kv.Value, msbuildFile, true)));
        }

        private XElement GetPropertyGroup(string condition, IEnumerable<XElement> properties)
        {
            return new XElement(
                PropertyGroupXName,
                condition != null ? new[] { new XAttribute("Condition", condition) } : new XAttribute[0],
                properties);
        }

        [SuppressMessage("Microsoft.Performance", "CA1800")]
        private string ValueToMsBuild(object value, MsbuildFile msbuildFile, bool isProperty = false)
        {
            if (value is string)
            {
                return ExpandString((string)value);
            }

            if (value is PathAtom)
            {
                return ((PathAtom)value).ToString(m_context.PathTable.StringTable);
            }

            if (value is AbsolutePath)
            {
                AbsolutePath absolutePath = (AbsolutePath)value;
                if (msbuildFile != null && msbuildFile.SpecDirectory != absolutePath)
                {
                    RelativePath relativePath;

                    // If it is a property, the relative path must be only to the enlistment root.
                    if (!isProperty && msbuildFile.SpecDirectory.TryGetRelative(m_context.PathTable, absolutePath, out relativePath))
                    {
                        var relativePathStr = relativePath.ToString(m_context.PathTable.StringTable);

                        // If we write the project files somewhere else (e.g., out\vs\projects\), combine it with SpecRoot.
                        if (!m_context.CanWriteToSrc)
                        {
                            return Path.Combine("$(SpecRoot)", relativePathStr);
                        }

                        return relativePathStr;
                    }
                }

                return ExpandAbsolutePath(absolutePath);
            }

            if (value is RelativePath)
            {
                return ((RelativePath)value).ToString(m_context.PathTable.StringTable);
            }

            throw new InvalidOperationException("Unexpected content");
        }

        private string ExpandString(string value)
        {
            var result = value;
            foreach (var kv in m_stringReplacements)
            {
                result = result.Replace(kv.Key, kv.Value);
            }

            return result;
        }

        private string ExpandAbsolutePath(AbsolutePath path)
        {
            return m_context.PathTable.ExpandName(path.Value, m_rootExpander);
        }

        private void WriteCommonImports(string enlistmentDir, string ideDir)
        {
            var rootSettingsPath = Path.Combine(ideDir, "RootSettings.props");
            var rootSettingsFile = new XDocument(
                new XElement(
                    ProjectXName,
                    new XElement(
                        PropertyGroupXName,
                        new XElement(
                            XName.Get("EnlistmentRoot"),
                            GetFullPathExpression(PathUtilities.EnsureTrailingSlash(m_context.EnlistmentRootStr), rootSettingsPath)))));

            // new XElement(XName.Get("OutputRoot", MsBuildNamespace), m_outputDir),
            // new XElement(XName.Get("BuildXLConfigFile", MsBuildNamespace), m_configFile))));
            rootSettingsFile.Save(rootSettingsPath);

            WriteFile("BuildXL.Ide.Generator.CommonBuildFiles.CSharp.targets", Path.Combine(ideDir, "CSharp.targets"));
            WriteFile("BuildXL.Ide.Generator.CommonBuildFiles.CSharp.props", Path.Combine(ideDir, "CSharp.props"));
            WriteFile("BuildXL.Ide.Generator.CommonBuildFiles.Common.props", Path.Combine(ideDir, "Common.props"));
            WriteFile("BuildXL.Ide.Generator.CommonBuildFiles.Common.targets", Path.Combine(ideDir, "Common.targets"));
            WriteFile("BuildXL.Ide.Generator.CommonBuildFiles.NuGet.config", Path.Combine(ideDir, "NuGet.config"));
            WriteFile("BuildXL.Ide.Generator.CommonBuildFiles.Directory.Build.props", Path.Combine(enlistmentDir, "Directory.Build.Props"));
        }

        [SuppressMessage("Microsoft.Usage", "CA2202")]
        private void WriteSolution(PathTable pathTable, string solutionFilePath)
        {
            string solutionDir = Path.GetDirectoryName(solutionFilePath);

            // We are not intentionally deleting the vsSettingsDir!
            if (!Directory.Exists(solutionDir))
            {
                Directory.CreateDirectory(solutionDir);
            }

            Tuple<string, string>[] defaultQualifierValues = new[] { Tuple.Create("Debug|Any CPU", "Debug|Any CPU"), Tuple.Create("Release|Any CPU", "Release|Any CPU") };
            Tuple<string, string>[] nativeQualifierValues = new[] { Tuple.Create("Debug|Any CPU", "Debug|Win32"), Tuple.Create("Release|Any CPU", "Release|Win32") };

            // TODO: Compare if the file is actually different and only write when different
            // TODO: Handle errors more gracefully in case someone has a 'lock' on the files.
            using (var stream = UpdateFile(solutionFilePath))
            {
                using (var writer = new StreamWriter(stream))
                {
                    writer.WriteLine("Microsoft Visual Studio Solution File, Format Version 12.00");

                    writer.WriteLine("# Visual Studio 14");
                    writer.WriteLine("# Generated by IdeGeneratorAnalyzer");
                    writer.WriteLine("VisualStudioVersion = 14.0.24720.0");

                    writer.WriteLine("MinimumVisualStudioVersion = 10.0.40219.1");

                    var childrenByParentGuid = new MultiValueDictionary<string, GuidWithName>();
                    var folderProjects = new Dictionary<RelativePath, string>();

                    // Emit projects
                    foreach (var msbuildFile in m_msbuildFiles)
                    {
                        string projectGuid = msbuildFile.Guid;
                        string projectName = Path.GetFileNameWithoutExtension(msbuildFile.Path.ToString(m_context.PathTable));
                        writer.Write("Project(\"");
                        if (msbuildFile is CsprojFile)
                        {
                            writer.Write(CSharpProjectTypeGuid);
                        }
                        else
                        {
                            writer.Write(NativeProjectTypeGuid);
                        }

                        writer.Write("\") = \"");
                        writer.Write(projectName);
                        writer.Write("\", \"");
                        writer.Write(PathUtilities.Relativize(msbuildFile.Path.ToString(pathTable), solutionFilePath));
                        writer.Write("\", \"");
                        writer.Write(projectGuid);
                        writer.WriteLine("\"");

                        writer.WriteLine("EndProject");

                        if (projectName.EndsWith(".g", StringComparison.OrdinalIgnoreCase))
                        {
                            projectName = Path.GetFileNameWithoutExtension(projectName);
                        }

                        RelativePath folder = msbuildFile.RelativePath.GetParent();
                        if (!folder.IsEmpty && string.Equals(projectName, folder.GetName().ToString(pathTable.StringTable), StringComparison.Ordinal))
                        {
                            // If the folder name matches the project name, skip the parent folder.
                            folder = folder.GetParent();
                        }

                        string folderGuid = ProcessFolderEntry(pathTable, folder, folderProjects, childrenByParentGuid, writer);
                        if (folderGuid != null)
                        {
                            AddNestedGuids(new GuidWithName(projectGuid, projectName), folderGuid, folder, folderProjects, childrenByParentGuid);
                        }
                    }

                    // Do not show the config file in the Solution explorer any more. We will reverse this change in future.
                    // Write the root config.dsc and .editorconfig under the Solution items
                    var relativeConfigPath = PathUtilities.Relativize(m_context.ConfigFilePathStr, m_context.SolutionFilePathStr);
                    var editorConfigPath = Path.Combine(Path.GetDirectoryName(m_context.ConfigFilePathStr), ".editorconfig");
                    writer.WriteLine("Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Solution Items\", \"Solution Items\", \"" + SolutionItemsGuid + "\"");
                    writer.WriteLine("\tProjectSection(SolutionItems) = preProject");
                    writer.WriteLine("\t\t" + relativeConfigPath + " = " + relativeConfigPath);

                    if (File.Exists(editorConfigPath))
                    {
                        var relativeEditorConfigPath = PathUtilities.Relativize(editorConfigPath, m_context.SolutionFilePathStr);
                        writer.WriteLine("\t\t" + relativeEditorConfigPath + " = " + relativeEditorConfigPath);
                    }

                    writer.WriteLine("\tEndProjectSection");
                    writer.WriteLine("EndProject");

                    // Write parent and child relations
                    writer.WriteLine("Global");
                    writer.WriteLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
                    foreach (var qualifierValue in defaultQualifierValues)
                    {
                        writer.WriteLine("\t\t{0} = {1}", qualifierValue.Item1, qualifierValue.Item2);
                    }

                    writer.WriteLine("\tEndGlobalSection");

                    writer.WriteLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
                    foreach (var msbuildFile in m_msbuildFiles)
                    {
                        Tuple<string, string>[] qualifierValues = defaultQualifierValues;

                        if (msbuildFile is VcxprojFile)
                        {
                            qualifierValues = nativeQualifierValues;
                        }

                        foreach (var qualifierValue in qualifierValues)
                        {
                            writer.WriteLine("\t\t{0}.{1}.ActiveCfg = {2}", msbuildFile.Guid, qualifierValue.Item1, qualifierValue.Item2);
                            writer.WriteLine("\t\t{0}.{1}.Build.0 = {2}", msbuildFile.Guid, qualifierValue.Item1, qualifierValue.Item2);
                        }
                    }

                    writer.WriteLine("\tEndGlobalSection");

                    writer.WriteLine("\tGlobalSection(SolutionProperties) = preSolution");
                    writer.WriteLine("\t\tHideSolutionNode = FALSE");
                    writer.WriteLine("\tEndGlobalSection");

                    writer.WriteLine("\tGlobalSection(NestedProjects) = preSolution");
                    foreach (var parentGuid in childrenByParentGuid.Keys)
                    {
                        foreach (var children in childrenByParentGuid[parentGuid])
                        {
                            writer.WriteLine("\t\t{0} = {1}", children.Guid, parentGuid);
                        }
                    }

                    writer.WriteLine("\tEndGlobalSection");

                    writer.WriteLine("EndGlobal");
                }
            }
        }

        private static void AddNestedGuids(GuidWithName child, string parentGuid, RelativePath parentFolder, Dictionary<RelativePath, string> folderProjects, MultiValueDictionary<string, GuidWithName> childrenByParentGuid)
        {
            // Check whether there is any siblings with the same name.
            // If so, put this child to the parent of the current parent folder.
            while (childrenByParentGuid.ContainsKey(parentGuid) && childrenByParentGuid[parentGuid].Any(a => a.Name == child.Name))
            {
                parentFolder = parentFolder.GetParent();
                parentGuid = folderProjects[parentFolder];
            }

            childrenByParentGuid.Add(parentGuid, child);
        }

        public bool IsExcludedItemInclude(string include)
        {
            foreach (var excludedFile in ExcludedFiles)
            {
                if (include.EndsWith(excludedFile, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        #region static methods

        [SuppressMessage("Microsoft.Reliability", "CA2000")]
        private static UpdateStream UpdateFile(string fileName)
        {
            return new UpdateStream(File.Open(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite), leaveOpen: false);
        }

        private static string GetFullPathExpression(string pathToRelativize, string basePath)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                @"$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)\{0}'))",
                PathUtilities.Relativize(pathToRelativize, basePath));
        }

        internal static void WriteFile(string resourceName, string filePath)
        {
            // TODO: Compare if the file is actually different and only write when different
            // TODO: Handle errors more gracefully in case someone has a 'lock' on the files.
            File.WriteAllText(filePath, GetEmbeddedResourceFile(resourceName));
        }

        private string ProcessFolderEntry(
            PathTable pathTable,
            RelativePath folder,
            Dictionary<RelativePath, string> folderProjects,
            MultiValueDictionary<string, GuidWithName> childrenByParentGuid,
            StreamWriter writer)
        {
            if (folder.IsEmpty)
            {
                return null;
            }

            string folderGuid;
            if (folderProjects.TryGetValue(folder, out folderGuid))
            {
                return folderGuid;
            }

            string name = folder.GetName().ToString(pathTable.StringTable);
            string currentGuid = GetGuidFromString(folder.ToString(pathTable.StringTable));

            RelativePath parentFolder = folder.GetParent();
            string parentGuid = ProcessFolderEntry(pathTable, parentFolder, folderProjects, childrenByParentGuid, writer);

            if (parentGuid != null)
            {
                AddNestedGuids(new GuidWithName(currentGuid, name), parentGuid, folder, folderProjects, childrenByParentGuid);
            }

            // Write folder.
            writer.Write("Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"");
            writer.Write(name);
            writer.Write("\", \"");
            writer.Write(name);
            writer.Write("\", \"");
            writer.Write(currentGuid);
            writer.WriteLine("\"");

            // Adding package and package.config files in order to show them in the SolutionExplorer
            var relativePath = folder.ToString(pathTable.StringTable);
            var fullPath = Path.Combine(m_context.EnlistmentRootStr, relativePath);
            var packagePath = Path.Combine(fullPath, "package" + Names.DotDscExtension);
            var packageConfigPath = Path.Combine(fullPath, Names.PackageConfigDsc);

            var builder = new StringBuilder();
            if (File.Exists(packagePath))
            {
                var temp = PathUtilities.Relativize(packagePath, m_context.SolutionFilePathStr);
                builder.AppendLine("\t\t" + temp + " = " + temp);
            }

            if (File.Exists(packageConfigPath))
            {
                var temp = PathUtilities.Relativize(packageConfigPath, m_context.SolutionFilePathStr);
                builder.AppendLine("\t\t" + temp + " = " + temp);
            }

            if (builder.Length > 0)
            {
                writer.WriteLine("\tProjectSection(SolutionItems) = preProject");
                writer.Write(builder.ToString());
                writer.WriteLine("\tEndProjectSection");
            }

            writer.WriteLine("EndProject");
            folderProjects.Add(folder, currentGuid);
            return currentGuid;
        }

        private static string GetGuidFromString(string value)
        {
#pragma warning disable CA5351 // Do not use insecure cryptographic algorithm MD5.
            using (var hash = MD5.Create())
#pragma warning restore CA5351 // Do not use insecure cryptographic algorithm MD5.
            {
                byte[] bytesToHash = Encoding.UTF8.GetBytes(value);
                hash.TransformFinalBlock(bytesToHash, 0, bytesToHash.Length);

                return new Guid(hash.Hash).ToString("B").ToUpperInvariant();
            }
        }

        private readonly struct GuidWithName
        {
            public readonly string Guid;
            public readonly string Name;

            public GuidWithName(string guid, string name)
            {
                Guid = guid;
                Name = name;
            }
        }


        /// <summary>
        /// Helper to get the string content of a resource file from the current assembly.
        /// </summary>
        /// <remarks>This unfortunately cannot be in a shared location like 'AssemblyHelpers' because on .Net Core it ignores the assembly and always tries to extract the resources from the running assembly. Even though GetManifestResourceNames() does respect it.</remarks>
        private static string GetEmbeddedResourceFile(string resourceKey)
        {
            var callingAssembly = typeof(MsbuildWriter).GetTypeInfo().Assembly;
            var stream = callingAssembly.GetManifestResourceStream(resourceKey);
            if (stream == null)
            {
                Contract.Assert(false, $"Expected embedded resource key '{resourceKey}' not found in assembly {callingAssembly.FullName}. Valid resource names are: {string.Join(",", callingAssembly.GetManifestResourceNames())}");
                return null;
            }

            using (var sr = new StreamReader(stream))
            {
                return sr.ReadToEnd();
            }
        }
        #endregion
    }
}
