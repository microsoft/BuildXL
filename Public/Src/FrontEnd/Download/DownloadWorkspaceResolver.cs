// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.FrontEnd.Download.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using JetBrains.Annotations;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;
using ValueTaskFactory = BuildXL.Utilities.Tasks.ValueTaskFactory;

namespace BuildXL.FrontEnd.Download
{
    /// <summary>
    /// A workspace module resolver that can download and extract archives.
    /// </summary>
    public sealed class DownloadWorkspaceResolver : IWorkspaceModuleResolver
    {
        /// <inheritdoc />
        public string Kind => "Download";

        /// <inheritdoc />
        public string Name { get; private set; }

        // These are set during Initialize
        private FrontEndContext m_context;
        private FrontEndHost m_host;

        /// <nodoc />
        public IReadOnlyDictionary<string, DownloadData> Downloads { get; private set; }

        private readonly HashSet<ModuleDescriptor> m_descriptors = new();
        private readonly MultiValueDictionary<string, ModuleDescriptor> m_descriptorsByName = new(StringComparer.Ordinal);
        private readonly Dictionary<AbsolutePath, ModuleDescriptor> m_descriptorsBySpecPath = new();
        private readonly Dictionary<ModuleDescriptor, ModuleDefinition> m_definitions = new();

        /// <inheritdoc />
        public bool TryInitialize([NotNull] FrontEndHost host, [NotNull] FrontEndContext context, [NotNull] IConfiguration configuration, [NotNull] IResolverSettings resolverSettings)
        {
            Contract.Requires(context != null);
            Contract.Requires(host != null);
            Contract.Requires(configuration != null);
            Contract.Requires(resolverSettings != null);

            var settings = resolverSettings as IDownloadResolverSettings;
            Contract.Assert(settings != null);

            m_context = context;
            m_host = host;
            Name = resolverSettings.Name;

            var resolverFolder = host.GetFolderForFrontEnd(resolverSettings.Name ?? Kind);

            var downloads = new Dictionary<string, DownloadData>(StringComparer.Ordinal);
            int i = 0;
            foreach (var downloadSettings in settings.Downloads)
            {
                if (!ValidateAndExtractDownloadData(context, downloadSettings, downloads, resolverFolder, out var downloadData))
                {
                    return false;
                }

                downloads.Add(downloadSettings.ModuleName, downloadData);
                UpdateDataForDownloadData(downloadData, i);
                i++;
            }

            Downloads = downloads;

            return true;
        }

        private bool ValidateAndExtractDownloadData(
            FrontEndContext context,
            IDownloadFileSettings downloadSettings,
            Dictionary<string, DownloadData> downloads,
            AbsolutePath resolverFolder,
            out DownloadData downloadData)
        {
            downloadData = null;
            if (string.IsNullOrEmpty(downloadSettings.ModuleName))
            {
                Logger.Log.DownloadFrontendMissingModuleId(m_context.LoggingContext, downloadSettings.Url);
                return false;
            }

            if (downloads.ContainsKey(downloadSettings.ModuleName))
            {
                Logger.Log.DownloadFrontendDuplicateModuleId(m_context.LoggingContext, downloadSettings.ModuleName, Kind, Name);
                return false;
            }

            if (string.IsNullOrEmpty(downloadSettings.Url))
            {
                Logger.Log.DownloadFrontendMissingUrl(m_context.LoggingContext, downloadSettings.ModuleName);
                return false;
            }

            if (!Uri.TryCreate(downloadSettings.Url, UriKind.Absolute, out var downloadLocation))
            {
                Logger.Log.DownloadFrontendInvalidUrl(m_context.LoggingContext, downloadSettings.ModuleName, downloadSettings.Url);
                return false;
            }

            ContentHash? contentHash;
            if (string.IsNullOrEmpty(downloadSettings.Hash))
            {
                contentHash = null;
            }
            else
            {
                if (!ContentHash.TryParse(downloadSettings.Hash, out var hash))
                {
                    Logger.Log.DownloadFrontendHashValueNotValidContentHash(m_context.LoggingContext, downloadSettings.ModuleName, downloadSettings.Url, downloadSettings.Hash);
                    return false;
                }

                contentHash = hash;
            }

            // For these last two validations, the TS parser would also complain, but we just give a more targeted error before that happens.
            if (!string.IsNullOrEmpty(downloadSettings.DownloadedValueName) && !SymbolAtom.TryCreate(context.StringTable, downloadSettings.DownloadedValueName, out _))
            {
                Logger.Log.NameContainsInvalidCharacters(m_context.LoggingContext, "downloadedValueName", downloadSettings.DownloadedValueName);
                return false;
            }

            if (!string.IsNullOrEmpty(downloadSettings.ExtractedValueName) && !SymbolAtom.TryCreate(context.StringTable, downloadSettings.ExtractedValueName, out _))
            {
                Logger.Log.NameContainsInvalidCharacters(m_context.LoggingContext, "extractedValueName", downloadSettings.ExtractedValueName);
                return false;
            }

            downloadData = new DownloadData(context, downloadSettings, downloadLocation, resolverFolder, contentHash, downloadSettings.DownloadedValueName, downloadSettings.ExtractedValueName);
            return true;
        }

        /// <summary>
        /// Returns the module descriptor for the download data.
        /// </summary>
        internal ModuleDescriptor GetModuleDescriptor(DownloadData downloadData) => m_descriptorsBySpecPath[downloadData.ModuleSpecFile];

        /// <inheritdoc />
        public string DescribeExtent()
        {
            Contract.Assume(m_descriptors != null, "Init must have been called");

            return string.Join(", ", m_descriptors.Select(descriptor => descriptor.Name));
        }

        /// <inheritdoc />
        public ValueTask<Possible<HashSet<ModuleDescriptor>>> GetAllKnownModuleDescriptorsAsync()
        {
            Contract.Assume(m_descriptors != null, "Init must have been called");

            return new ValueTask<Possible<HashSet<ModuleDescriptor>>>(m_descriptors);
        }

        /// <inheritdoc />
        public ISourceFile[] GetAllModuleConfigurationFiles() =>
            // No need to do anything, this is for when input files are changed which should not happen for the Download resolver since the only data comes from config.
            CollectionUtilities.EmptyArray<ISourceFile>();

        /// <inheritdoc />
        public Task ReinitializeResolver() =>
            // No need to do anything, this is for when input files are changed which should not happen for the Download resolver since the only data comes from config.
            Task.FromResult<object>(null);

        /// <inheritdoc />
        public ValueTask<Possible<ModuleDefinition>> TryGetModuleDefinitionAsync(ModuleDescriptor moduleDescriptor)
        {
            Contract.Assume(m_definitions != null, "Init must have been called");

            return m_definitions.TryGetValue(moduleDescriptor, out var result)
                ? ValueTaskFactory.FromResult(Possible.Create(result))
                : ValueTaskFactory.FromResult((Possible<ModuleDefinition>)new ModuleNotOwnedByThisResolver(moduleDescriptor));
        }

        /// <inheritdoc />
        public ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>> TryGetModuleDescriptorsAsync(ModuleReferenceWithProvenance moduleReference)
        {
            Contract.Assume(m_descriptorsByName != null, "Init must have been called");

            IReadOnlyCollection<ModuleDescriptor> result = m_descriptorsByName.TryGetValue(moduleReference.Name, out var descriptors)
                ? descriptors
                : CollectionUtilities.EmptyArray<ModuleDescriptor>();

            return ValueTaskFactory.FromResult(Possible.Create(result));
        }

        /// <inheritdoc />
        public ValueTask<Possible<ModuleDescriptor>> TryGetOwningModuleDescriptorAsync(AbsolutePath specPath)
        {
            Contract.Assume(m_descriptorsBySpecPath != null, "Init must have been called");

            if (m_descriptorsBySpecPath.TryGetValue(specPath, out var result))
            {
                return ValueTaskFactory.FromResult(Possible.Create(result));
            }

            var notOwnedFailure = new SpecNotOwnedByResolverFailure(specPath.ToString(m_context.PathTable));
            return ValueTaskFactory.FromResult((Possible<ModuleDescriptor>)notOwnedFailure);
        }

        /// <inheritdoc />
        public Task<Possible<ISourceFile>> TryParseAsync(AbsolutePath pathToParse, AbsolutePath moduleOrConfigPathPromptingParse, ParsingOptions parsingOption = null)
        {
            Contract.Assume(m_descriptorsBySpecPath != null, "Init must have been called");

            var pathToParseAsString = pathToParse.ToString(m_context.PathTable);

            if (!m_descriptorsBySpecPath.TryGetValue(pathToParse, out var descriptor))
            {
                return Task.FromResult<Possible<ISourceFile>>(new SpecNotOwnedByResolverFailure(pathToParseAsString));
            }

            if (!Downloads.TryGetValue(descriptor.Name, out var downloadData))
            {
                Contract.Assert(false, "Inconsistent internal state of DownloadResolver");
                return Task.FromResult<Possible<ISourceFile>>(new SpecNotOwnedByResolverFailure(pathToParseAsString));
            }

            string exeExtension = OperatingSystemHelper.IsWindowsOS ? ".exe" : string.Empty;

            // CODESYNC: keep in sync with Public\Src\Tools\FileDownloader\Tool.FileDownloader.dsc deployment
            AbsolutePath toolRootPath = m_host.Configuration.Layout.NormalizedBuildEngineDirectory.IsValid ? m_host.Configuration.Layout.NormalizedBuildEngineDirectory : m_host.Configuration.Layout.BuildEngineDirectory;
            var pathToDownloader = toolRootPath.Combine(m_context.PathTable, RelativePath.Create(m_context.StringTable, "Downloader" + exeExtension));
            var pathToExtractor = toolRootPath.Combine(m_context.PathTable, RelativePath.Create(m_context.StringTable, "Extractor" + exeExtension));

            // Create a spec file that schedules two pips: a download one followed by an extract one. The latter only if extraction is specified
            // CODESYNC: tools arguments and behavior defined in Public\Src\Tools\FileDownloader\Downloader.cs and \Public\Src\Tools\FileDownloader\Extractor.cs
            var spec = $@"
                export declare const qualifier: {{}};
            
                const downloadTool  = {CreateToolDefinition(pathToDownloader, dependsOnAppDataDirectory: true)}
                const downloadResult = {CreateDownloadPip(downloadData)}
                @@public export const {downloadData.DownloadedValueName} : File = downloadResult.getOutputFile(p`{downloadData.DownloadedFilePath.ToString(m_context.PathTable)}`);";

            // The extract pip (and its corresponding public value) are only available if extraction needs to happen
            if (downloadData.ShouldExtractBits)
            {
                spec += $@"
                    const extractTool  = {CreateToolDefinition(pathToExtractor)}
                    const extractResult = {CreateExtractPip(downloadData)}
                    @@public export const {downloadData.ExtractedValueName} : OpaqueDirectory = extractResult.getOutputDirectory(d`{downloadData.ContentsFolder.Path.ToString(m_context.PathTable)}`);
                    ";
            }

            // No need to check for errors here since they are embedded in the source file and downstream consumers check for those
            FrontEndUtilities.TryParseSourceFile(m_context, pathToParse, spec, out var sourceFile);

            return Task.FromResult<Possible<ISourceFile>>(sourceFile);
        }

        private string CreateToolDefinition(AbsolutePath pathToTool, bool dependsOnAppDataDirectory = false)
        {
            return $"{{exe: f`{pathToTool.ToString(m_context.PathTable)}`, dependsOnCurrentHostOSDirectories: true, dependsOnAppDataDirectory: {(dependsOnAppDataDirectory ? "true" : "false")}, prepareTempDirectory: true, " +
                // These tools are bundled with the build engine so we trust that their behavior does not change except for when their command line changes.
                $"untrackedDirectoryScopes: [" +
                $"  d`{pathToTool.GetParent(m_context.PathTable).ToString(m_context.PathTable)}`," +
                $"  ...addIfLazy(Context.getCurrentHost().os === \"win\", () => [d`${{Context.getMount(\"LocalLow\").path}}/Microsoft/CryptnetFlushCache`])," +
                $"  ...addIfLazy(Context.getCurrentHost().os === \"win\", () => [d`${{Context.getMount(\"ProgramFiles\").path}}/WindowsApps`])," +
                $"  ...addIfLazy(Context.getCurrentHost().os !== \"win\", () => [d`/usr/local/lib`])," +
                $"  ...addIfLazy(Context.getCurrentHost().os !== \"win\" && Context.hasMount(\"UserProfile\"), () => [d`${{Context.getMount(\"UserProfile\").path}}/.dotnet`])" +
                $"]}};";
        }

        private string CreateDownloadPip(DownloadData data)
        {
            string downloadDirectory = data.DownloadedFilePath.GetParent(m_context.PathTable).ToString(m_context.PathTable);

            // The download pip is flagged with isLight, since it is mostly network intensive
            // We disable reparse point resolving for this pip (and the extract one below) since the frontend directory
            // sometimes is placed under a junction (typically when CB junction outputs) and with full reparse point resolution enabled this 
            // would generate a DFA. We know these pips do not interact with reparse points, so this is safe.
            return $@"<TransformerExecuteResult> _PreludeAmbientHack_Transformer.execute({{
                tool: downloadTool,
                tags: ['download'],
                description: 'Downloading \""{data.DownloadUri}\""',
                workingDirectory: d`{downloadDirectory}`,
                arguments: [
                    {{name: '/url:', value: '{data.DownloadUri}'}},
                    {{name: '/downloadDirectory:', value: p`{downloadDirectory}`}},
                    {(data.ContentHash.HasValue ? $"{{name: '/hash:', value: '{data.ContentHash.Value}'}}," : string.Empty)}
                    {(!string.IsNullOrEmpty(data.Settings.FileName) ? $"{{name: '/fileName:', value: '{data.Settings.FileName}'}}," : string.Empty)}
                ],
                outputs: [f`{data.DownloadedFilePath.ToString(m_context.PathTable)}`],
                isLight: true,
                unsafe: {{disableFullReparsePointResolving: true}},
            }});";
        }

        private string CreateExtractPip(DownloadData data)
        {
            string extractDirectory = data.ContentsFolder.Path.ToString(m_context.PathTable);

            // The result of the extraction goes into an exclusive opaque
            return $@"<TransformerExecuteResult> _PreludeAmbientHack_Transformer.execute({{
                tool: extractTool,
                tags: ['extract'],
                description: 'Extracting \""{data.DownloadUri}\""',
                workingDirectory: d`{extractDirectory}`,
                arguments: [
                    {{name: '/file:', value: p`{data.DownloadedFilePath.ToString(m_context.PathTable)}`}},
                    {{name: '/extractTo:', value: p`{extractDirectory}`}},
                    {{name: '/archiveType:', value: '{data.Settings.ArchiveType}'}},
                ],
                outputs: [d`{extractDirectory}`],
                dependencies: [{data.DownloadedValueName}],
                unsafe: {{disableFullReparsePointResolving: true}},
            }});";
        }

        internal void UpdateDataForDownloadData(DownloadData downloadData, int resolverSettingsIndex, FrontEndContext context = null)
        {
            context ??= m_context;
            Contract.Assert(context != null);

            var name = downloadData.Settings.ModuleName;

            var moduleId = ModuleId.Create(context.StringTable, name);
            var descriptor = new ModuleDescriptor(moduleId, name, name, string.Empty, Kind, Name);

            var definition = ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                descriptor,
                downloadData.ModuleRoot,
                downloadData.ModuleConfigFile,
                new[] { downloadData.ModuleSpecFile },
                allowedModuleDependencies: null,
                cyclicalFriendModules: null,
                mounts: new Mount[] {
                    new Mount {
                        Name = PathAtom.Create(m_context.StringTable, $"Download#{resolverSettingsIndex}"),
                        Path = downloadData.ModuleRoot,
                        TrackSourceFileChanges = true,
                        IsWritable = true,
                        IsReadable = true,
                        IsScrubbable = true }
                }); // A Download package does not have any module dependency restrictions nor allowlist cycles

            m_descriptors.Add(descriptor);
            m_descriptorsByName.Add(name, descriptor);
            m_descriptorsBySpecPath.Add(downloadData.ModuleSpecFile, descriptor);
            m_definitions.Add(descriptor, definition);
        }
    }
}
