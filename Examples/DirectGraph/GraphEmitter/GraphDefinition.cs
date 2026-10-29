// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GraphEmitter
{
    internal sealed class GraphDefinition
    {
        [JsonProperty("modules", Required = Required.Always)]
        public IReadOnlyList<ModuleDefinition> Modules { get; set; }
    }

    internal sealed class ModuleDefinition
    {
        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }

        [JsonProperty("operations", Required = Required.Always)]
        public IReadOnlyList<JObject> Operations { get; set; }
    }

    internal abstract class OperationDefinition
    {
        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }

        [JsonProperty("kind", Required = Required.Always)]
        public string Kind { get; set; }
    }

    internal sealed class WriteFileOperationDefinition : OperationDefinition
    {
        [JsonProperty("destination", Required = Required.Always)]
        public string Destination { get; set; }

        [JsonProperty("content", Required = Required.Always)]
        public string Content { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    internal sealed class ProcessOperationDefinition : OperationDefinition
    {
        [JsonProperty("executable", Required = Required.Always)]
        public string Executable { get; set; }

        [JsonProperty("workingDirectory", Required = Required.Always)]
        public string WorkingDirectory { get; set; }

        [JsonProperty("arguments")]
        public IReadOnlyList<string> Arguments { get; set; }

        [JsonProperty("inputFiles")]
        public IReadOnlyList<FileReferenceDefinition> InputFiles { get; set; }

        [JsonProperty("inputDirectories")]
        public IReadOnlyList<DirectoryReferenceDefinition> InputDirectories { get; set; }

        [JsonProperty("outputFiles")]
        public IReadOnlyList<string> OutputFiles { get; set; }

        /// <summary>
        /// Files another operation produced that this process rewrites in place. Each is also an input of the process.
        /// </summary>
        [JsonProperty("rewrittenFiles")]
        public IReadOnlyList<FileReferenceDefinition> RewrittenFiles { get; set; }

        /// <summary>
        /// Shared opaque output directories produced by this process.
        /// </summary>
        [JsonProperty("producesDirectories")]
        public IReadOnlyList<string> ProducesDirectories { get; set; }

        [JsonProperty("environment")]
        public IReadOnlyDictionary<string, string> Environment { get; set; }

        /// <summary>
        /// Process options by name, for example 'dependsOnCurrentOs'.
        /// </summary>
        [JsonProperty("options")]
        public IReadOnlyList<string> Options { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    internal sealed class CopyFileOperationDefinition : OperationDefinition
    {
        [JsonProperty("source", Required = Required.Always)]
        public FileReferenceDefinition Source { get; set; }

        [JsonProperty("destination", Required = Required.Always)]
        public string Destination { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    internal sealed class SealDirectoryOperationDefinition : OperationDefinition
    {
        [JsonProperty("directoryRoot", Required = Required.Always)]
        public string DirectoryRoot { get; set; }

        /// <summary>
        /// One of partial, full, sourceAllDirectories, sourceTopDirectoryOnly. Defaults to partial.
        /// </summary>
        [JsonProperty("sealKind")]
        public string SealKind { get; set; }

        [JsonProperty("contents")]
        public IReadOnlyList<FileReferenceDefinition> Contents { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    /// <summary>
    /// Composes several shared opaque directories into one.
    /// </summary>
    internal sealed class ComposeSharedOpaqueOperationDefinition : OperationDefinition
    {
        [JsonProperty("directoryRoot", Required = Required.Always)]
        public string DirectoryRoot { get; set; }

        [JsonProperty("contents", Required = Required.Always)]
        public IReadOnlyList<DirectoryReferenceDefinition> Contents { get; set; }

        /// <summary>
        /// Optional regular expression. Files whose absolute path matches it are excluded.
        /// </summary>
        [JsonProperty("excludeRegex")]
        public string ExcludeRegex { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    internal sealed class ExportOperationDefinition : OperationDefinition
    {
        [JsonProperty("files")]
        public IReadOnlyList<FileReferenceDefinition> Files { get; set; }

        [JsonProperty("directories")]
        public IReadOnlyList<DirectoryReferenceDefinition> Directories { get; set; }
    }

    /// <summary>
    /// A reference to a directory produced by an earlier operation.
    /// </summary>
    internal sealed class DirectoryReferenceDefinition
    {
        [JsonProperty("producedBy", Required = Required.Always)]
        public string ProducedBy { get; set; }

        /// <summary>
        /// Root of the wanted directory. Optional when the referenced operation produced exactly one.
        /// </summary>
        [JsonProperty("path")]
        public string Path { get; set; }
    }

    internal sealed class FileReferenceDefinition
    {
        [JsonProperty("sourcePath")]
        public string SourcePath { get; set; }

        [JsonProperty("producedBy")]
        public string ProducedBy { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }
    }
}
