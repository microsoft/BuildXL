// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Tracing;
using JetBrains.Annotations;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ide.Generator
{
    /// <summary>
    /// Ide Generator class which generates msbuild project files and a solution file
    /// </summary>
    public sealed class IdeGenerator
    {
        /// <summary>
        /// BuildXL VS plugin GUID
        /// </summary>
        public const string PluginGuid = "76439c3a-9faf-4f38-9f54-f127e9be9171";

        /// <summary>
        /// Latest version of BuildXL VS plugin
        /// </summary>
        public const string LatestPluginVersion = "2.9";

        private readonly Context m_context;

        /// <summary>
        /// Configure the IdeConfiguration in the given config file
        /// </summary>
        [SuppressMessage("Microsoft.Globalization", "CA1307")]
        public static void Configure(ConfigurationImpl config, IStartupConfiguration startupConfig, PathTable pathTable)
        {
            Contract.Requires(startupConfig.ConfigFile.IsValid);

            // Ide generation implies a schedule phase.
            config.Engine.Phase = EnginePhases.Schedule;

            var enlistmentRoot = startupConfig.ConfigFile.GetParent(pathTable);
            Contract.Assume(enlistmentRoot.IsValid);

            // Populate the missing values of the Ide configuration
            var ideConfiguration = config.Ide;

            // Deciding the solution name:
            // If the user passes a custom name for solution, use it.
            // Otherwise if the spec filtering is used, use the directory name of the spec.
            // Otherwise, use the directory name of the config file as the solution name.
            if (!ideConfiguration.SolutionName.IsValid)
            {
                // Translate enlistmentRoot because we need to get the name of the repository, which is not possible when using mapped path (e.g., b:\)
                var unmappedEnlistmentRoot = enlistmentRoot;
                PathTranslator translator;
                if (PathTranslator.CreateIfEnabled(config.Logging.SubstTarget, config.Logging.SubstSource, pathTable, out translator))
                {
                    unmappedEnlistmentRoot = translator.Translate(pathTable, enlistmentRoot);
                }

                // It's possible for the path to be at the root of the drive and not be able to be translated back when
                // the PathTranslator isn't enabled. In that case, fall back on some default solution name.
                string solutionNameCandidate = unmappedEnlistmentRoot.GetName(pathTable).ToString(pathTable.StringTable);
                if (string.IsNullOrWhiteSpace(solutionNameCandidate))
                {
                    solutionNameCandidate = "ideGenerated";
                }

                ideConfiguration.SolutionName = PathAtom.Create(pathTable.StringTable, solutionNameCandidate);
            }

            if (!ideConfiguration.SolutionRoot.IsValid)
            {
                ideConfiguration.SolutionRoot = config.Layout.OutputDirectory.Combine(pathTable, "VS");
            }
        }

        /// <summary>
        /// Constructs an Ide Generator and generates the files
        /// </summary>
        public static bool Generate(PipExecutionContext pipContext, PipGraph pipGraph, IReadonlyDirectedGraph scheduledGraph, AbsolutePath configFilePath, IIdeConfiguration ideConfig)
        {
            var generator = new IdeGenerator(pipContext, pipGraph, scheduledGraph, configFilePath, ideConfig);
            return generator.Generate();
        }

        /// <summary>
        /// Constructs an Ide Generator from the BuildXL.Execution.Analyzer
        /// </summary>
        public IdeGenerator(PipExecutionContext pipContext, PipGraph pipGraph, IReadonlyDirectedGraph scheduledGraph, AbsolutePath configFilePath, IIdeConfiguration ideConfig)
        {
            m_context = new Context(pipContext, pipGraph, scheduledGraph, configFilePath, ideConfig);
        }

        /// <summary>
        /// Perform the generation of msbuild files
        /// </summary>
        public bool Generate()
        {
            IReadOnlyList<MsbuildFile> msbuildFiles = GenerateMsbuildFiles();

            // After generating all msbuild files, decide the project and assembly references
            ProcessRawReferences(msbuildFiles);

            // Now, merge the projects in a msbuild file by finding the conditioned and unconditioned properties/items.
            // TODO: Merging doesn't work for vcxproj or any proj with multiple qualifiers, e.g., there are duplicate entries in <ItemGroup/>. 
            // TODO: Fix me!
            //foreach (var msbuildFile in msbuildFiles)
            //{
            //    CreateConditionedProjects(msbuildFile);
            //}

            // Write the msbuild files to the disk
            var writer = new MsbuildWriter(msbuildFiles, m_context);
            writer.Write();

            return true;
        }

        private IReadOnlyList<MsbuildFile> GenerateMsbuildFiles()
        {
            var msbuildFiles = new List<MsbuildFile>();

            // Retrieve Process pip nodes. Then, hydrate them and group by their spec path.
            var specFileGroupedProcessPips = m_context
                .ScheduledGraph
                .Nodes
                .Where(nodeId => m_context.PipGraph.PipTable.GetPipType(nodeId.ToPipId()) == PipType.Process)
                .Select(nodeId => m_context.PipGraph.PipTable.HydratePip(nodeId.ToPipId(), BuildXL.Pips.PipQueryContext.IdeGenerator))
                .GroupBy(p => p.Provenance.Token.Path);

            foreach (var processPips in specFileGroupedProcessPips)
            {
                var specFile = processPips.Key;
                var categorizedProcesses = processPips
                    .OfType<Process>()
                    .Select(p => ProcessWithType.Categorize(m_context, p))
                    .ToList();

                MsbuildFile msbuildFile = null;
                if (categorizedProcesses.Any(a => a.Type == ProcessType.Csc))
                {
                    msbuildFile = new CsprojFile(m_context, specFile);
                }
                else if (categorizedProcesses.Any(a => a.Type == ProcessType.Cl))
                {
                    msbuildFile = new VcxprojFile(m_context, specFile);
                }

                if (msbuildFile != null)
                {
                    msbuildFile.VisitProcesses(categorizedProcesses);

                    // SealDirectories might not be scheduled so look at the full graph to find them.
                    foreach (var directory in m_context.PipGraph.GetPipsPerSpecFile(specFile).OfType<SealDirectory>())
                    {
                        msbuildFile.VisitDirectory(directory);
                    }

                    msbuildFile.EndVisitingProject();
                    msbuildFiles.Add(msbuildFile);
                }
            }

            return msbuildFiles;
        }

        private void ProcessRawReferences(IReadOnlyList<MsbuildFile> msbuildFiles)
        {
            var msbuildFilesBySpecFiles = new Dictionary<AbsolutePath, MsbuildFile>();
            foreach (var msbuildFile in msbuildFiles)
            {
                msbuildFilesBySpecFiles.Add(msbuildFile.SpecFilePath, msbuildFile);
            }

            foreach (var msbuildFile in msbuildFiles)
            {
                foreach (var project in msbuildFile.ProjectsByQualifier.Values)
                {
                    foreach (var reference in project.RawReferences)
                    {
                        var referencePath = reference.Key;
                        var referenceName = referencePath.GetName(m_context.PathTable).RemoveExtension(m_context.StringTable);
                        var pip = m_context.PipGraph.TryFindProducer(referencePath, VersionDisposition.Latest, null);
                        var producerSpecFilePath = pip?.Provenance?.Token.Path;
                        MsbuildFile referencedMsbuildFile;
                        if (producerSpecFilePath.HasValue && msbuildFilesBySpecFiles.TryGetValue(producerSpecFilePath.Value, out referencedMsbuildFile) && referencedMsbuildFile != msbuildFile)
                        {
                            var item = project.AddItem("ProjectReference", referencedMsbuildFile.Path);
                            item.SetMetadata("Project", referencedMsbuildFile.Guid);
                            item.SetMetadata("Name", referenceName);
                            msbuildFile.ProjectReferences.Add(referencedMsbuildFile);
                        }
                        else if (msbuildFile is CsprojFile)
                        {
                            var item = project.AddItem("Reference", referenceName);
                            item.SetMetadata("HintPath", referencePath);
                            var aliases = string.Join(",", reference.Value.Distinct());
                            if (aliases != Project.GlobalAliasName)
                            {
                                item.SetMetadata("Aliases", aliases);
                            }
                            item.SetMetadata("NuGetPackageId", referenceName); // it doesn't matter what this value is
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Writes the build.cmd file that is the snapshot of the environment variables and BuildXL commandline when user calling BuildXL with /vs argument.
        /// </summary>
        [SuppressMessage("Microsoft.Globalization", "CA1305")]
        public static void WriteCmd(string commandLine, IIdeConfiguration config, AbsolutePath configFile, PathTable pathTable, [CanBeNull] PathTranslator translator)
        {
            var enlistmentRoot = configFile.GetParent(pathTable).ToString(pathTable);

            var builder = new StringBuilder();
            builder.AppendLine("@echo off");

            if (translator == null)
            {
                builder.AppendLine("cd " + enlistmentRoot);
            }
            else
            {
                enlistmentRoot = translator.Translate(enlistmentRoot);
            }

            // Remove trailing file path separators.
            enlistmentRoot = enlistmentRoot.TrimEnd('\\');

            foreach (DictionaryEntry envVar in Environment.GetEnvironmentVariables())
            {
                var key = (string)envVar.Key;
                var value = (string)envVar.Value;

                // If the value contains '<' or '>', add escape character; otherwise we will see syntax error when running build.cmd.
                value = Regex.Replace(value, @"[<>]", m => $"^{m.Value}");

                // Scrub newlines and tabs as they will break the generated build.cmd
                value = Regex.Replace(value, @"[\r,\n,\t]", m => " ");

                builder.AppendLine("Set " + key + "=" + value);
            }

            commandLine = FixCommandLine(commandLine);

            // Prepend to the command line the RunInSubst executable and its parameters
            int exeIndex = commandLine.IndexOf($"\\{Branding.ProductExecutableName}", StringComparison.OrdinalIgnoreCase);
            string binRoot = exeIndex > -1
                ? commandLine.Substring(0, exeIndex)
                : Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(System.Reflection.Assembly.GetEntryAssembly()));
            if (translator != null)
            {
                binRoot = translator.Translate(binRoot);
            }

            string substedCommandLine = I($@"""{binRoot}\RunInSubst.exe"" B={enlistmentRoot} {commandLine} /server+ @%~dp0\build.cmd.rsp @%~dp0\domino.cmd.rsp %*"); // Also add the legacy response file domino.rsp for old plugins
            builder.AppendLine(substedCommandLine);
            builder.AppendLine("set RETURN_CODE=%ERRORLEVEL%");

            // Map the drive back.
            // RunInSubst is unmapping the drive to follow WDG protocol, but to run unit tests we need the drive still mapped.
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "subst B: {0}", enlistmentRoot));

            // Return the exit code of BuildXL.
            builder.AppendLine("exit /b %RETURN_CODE%");
            builder.AppendLine();

            var solutionDirPath = GetSolutionPath(config, pathTable).GetParent(pathTable).ToString(pathTable);
            File.WriteAllText(Path.Combine(solutionDirPath, "build.cmd.rsp"), string.Empty);
            File.WriteAllText(Path.Combine(solutionDirPath, "build.cmd"), builder.ToString());
            
            // Legacy file to support old plugins
            File.WriteAllText(Path.Combine(solutionDirPath, "domino.cmd"), "@call %~dp0build.cmd %*");
            File.WriteAllText(Path.Combine(solutionDirPath, "domino.cmd.rsp"), string.Empty); // Legacy response file for old plugin
        }

        /// <summary>
        /// Performs fixes to the commandline for the Visual Studio build.cmd script
        /// </summary>
        public static string FixCommandLine(string commandLine)
        {
            var args = SplitCommandLineArgs(commandLine);
            var cl = new CommandLineUtilities(args);

            var builder = new StringBuilder();

            string tool = cl.Arguments.FirstOrDefault();
            if (!string.IsNullOrEmpty(tool))
            {
                CommandLineUtilities.Option.PrintEncodeArgument(builder, tool);
            }

            foreach (var option in cl.Options)
            {
                if (string.Equals(option.Name, "vs", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(option.Name, "vs+", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(option.Name, "vs-", StringComparison.OrdinalIgnoreCase))
                {
                    // Remove /vs from the BuildXL arguments since we want this script to build the product, not
                    continue;
                }

                if (string.Equals(option.Name, "f", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(option.Name, "filter", StringComparison.OrdinalIgnoreCase))
                {
                    // Remove /f: and /filter: from the BuildXL arguments, since we will pass a custom filter meant for the build you are doing currently.
                    continue;
                }

                builder.Append(" ");
                option.PrintCommandLineString(builder);
            }

            // Normally you have to also add the cl.Arguments to the final results, but unnamed arguments in BuildXL are implicit filters which we want to remove.
            return builder.ToString();
        }

        private static string[] SplitCommandLineArgs(string commandline)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                return new Utilities.CLI.WinParser().SplitArgs(commandline);
            }

            if (string.IsNullOrWhiteSpace(commandline))
            {
                return CollectionUtilities.EmptyArray<string>();
            }

            int argCount;
            var splitArgs = NativeMethods.CommandLineToArgvW(commandline, out argCount);

            // CommandLineToArgvW returns NULL upon failure.
            Contract.Assert(splitArgs != IntPtr.Zero, "OS was able the parse these arguments to get this far, so it should not fail now.");

            // Make sure the memory ptrToSplitArgs to is freed, even upon failure.
            try
            {
                var args = new string[argCount];

                // ptrToSplitArgs is an array of pointers to null terminated Unicode strings.
                // Copy each of these strings into our split argument array.
                for (int i = 0; i < argCount; i++)
                {
                    args[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(splitArgs, i * IntPtr.Size));
                }

                return args;
            }
            finally
            {
                // Free memory obtained by CommandLineToArgW.
                NativeMethods.LocalFree(splitArgs);
            }
        }

        /// <summary>
        /// Gets the solution path by using the IdeConfiguration
        /// </summary>
        public static AbsolutePath GetSolutionPath(IIdeConfiguration configuration, PathTable pathTable)
        {
            var solutionName = configuration.SolutionName;
            var solutionNameWithExt = solutionName.Concat(pathTable.StringTable, PathAtom.Create(pathTable.StringTable, ".sln"));

            // $SolutionRoot$\$SolutionName$\$SolutionName$.sln
            return configuration.SolutionRoot.Combine(pathTable, solutionName, solutionNameWithExt);
        }

        /// <summary>
        /// Checks the registry whether the system has the latest BuildXL VS plugin
        /// </summary>
        public static string GetVersionsNotHavingLatestPlugin()
        {
#if !DISABLE_FEATURE_VSEXTENSION_INSTALL_CHECK
            InstallationStatus vs2015Status = InstallationStatus.VsNotInstalled;
            InstallationStatus vs2017Status = InstallationStatus.VsNotInstalled;

            try
            {
                var visualStudioAppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\VisualStudio");
                foreach (var dir in Directory.GetDirectories(visualStudioAppDataDir))
                {
                    string name = Path.GetFileName(dir);
                    if (name.StartsWith("14.", StringComparison.OrdinalIgnoreCase))
                    {
                        vs2015Status = IsPluginInstalledIn2015(name);
                    }
                    else if (name.StartsWith("15.", StringComparison.OrdinalIgnoreCase))
                    {
                        vs2017Status = IsPluginInstalledIn2017(name) ? InstallationStatus.BothInstalled : InstallationStatus.PluginNotInstalled;
                    }
                }
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                // If there are problems to find the VS directories under the AppData folder, just check with the VS 2015's default version (14.0).
                vs2015Status = IsPluginInstalledIn2015("14.0");
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            if (vs2015Status == InstallationStatus.PluginNotInstalled)
            {
                return vs2017Status == InstallationStatus.PluginNotInstalled ? "VS2015 and VS2017" : "VS2015";
            }
            else if (vs2017Status == InstallationStatus.PluginNotInstalled)
            {
                return "VS2017";
            }
#endif

            return null;
        }

#if !DISABLE_FEATURE_VSEXTENSION_INSTALL_CHECK
        private static InstallationStatus IsPluginInstalledIn2015(string version)
        {
            object defaultValue = new object();
            var returnValue = Registry.GetValue(I($@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\VisualStudio\{version}\ExtensionManager\EnabledExtensions"), I($"{PluginGuid},{LatestPluginVersion}"), defaultValue);
            if (returnValue == null)
            {
                return InstallationStatus.VsNotInstalled;
            }
            else if (returnValue == defaultValue)
            {
                return InstallationStatus.PluginNotInstalled;
            }

            return InstallationStatus.BothInstalled;
        }

        private static bool IsPluginInstalledIn2017(string version)
        {
            var existingKeyPath = I($@"Software\Microsoft\VisualStudio\{version}\ExtensionManager\EnabledExtensions");

            var privateRegistryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), I($@"Microsoft\VisualStudio\{version}\privateregistry.bin"));

            var hKey = RegistryNativeMethods.RegLoadAppKey(privateRegistryPath);
            using (var safeRegistryHandle = new SafeRegistryHandle(new IntPtr(hKey), true))
            using (var appKey = RegistryKey.FromHandle(safeRegistryHandle))
            using (var currentVersionKey = appKey.OpenSubKey(existingKeyPath))
            {
                var isPluginInstalled = currentVersionKey.GetValue(I($"{PluginGuid},{LatestPluginVersion}"));
                return isPluginInstalled != null;
            }
        }
#endif

        private enum InstallationStatus
        {
            VsNotInstalled,
            PluginNotInstalled,
            BothInstalled,
        }

        internal static class RegistryNativeMethods
        {
            [Flags]
            public enum RegSAM
            {
                KEY_READ = 0x20019,
            }

            [SuppressMessage("Microsoft.Globalization", "CA2101:SpecifyMarshalingForPInvokeStringArguments")]
            [DllImport("advapi32.dll", SetLastError = true)]
            private static extern int RegLoadAppKey(string hiveFile, out int hKey, RegSAM samDesired, int options, int reserved);

            [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
            public static int RegLoadAppKey(string hiveFile)
            {
                int hKey;
                int rc = RegLoadAppKey(hiveFile, out hKey, RegSAM.KEY_READ, 0, 0);

                if (rc != 0)
                {
                    throw new Win32Exception(rc, "Failed during RegLoadAppKey of file " + hiveFile);
                }

                return hKey;
            }
        }
    }
}
