// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Execution.Analyzer.Analyzers;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.ToolSupport;
using Newtonsoft.Json;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        // Required flags
        private const string OutputFileOption = "OutputFile";

        // Optional flags
        private const string PipHashOption = "PipHash";
        private const string PipTypeOption = "PipType";
        private const string MetadataOnlyOption = "MetadataOnly";

        public Analyzer InitializeCosineDumpPip()
        {
            DumpPipFilters filters = new DumpPipFilters();
            string outputFilePath = null;

            foreach (Option opt in AnalyzerOptions)
            {
                if (opt.Name.Equals(OutputFileOption, StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals(PipHashOption, StringComparison.OrdinalIgnoreCase))
                {
                    filters.PipSemiStableHash = ParseSemistableHash(opt);
                }
                else if (opt.Name.Equals(PipTypeOption, StringComparison.OrdinalIgnoreCase))
                {
                    filters.PipTypeFilter = ParseEnumOption<PipType>(opt);
                }
                else if (opt.Name.Equals(MetadataOnlyOption, StringComparison.OrdinalIgnoreCase))
                {
                    filters.OnlyMetadata = true;
                }
                else
                {
                    throw Error("Unknown option for cosine dump pip analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw Error("/outputFile parameter is required");
            }

            return new CosineDumpPip(GetAnalysisInput(), outputFilePath, filters);
        }

        private static void WriteCosineDumpPipHelp(HelpWriter writer)
        {
            writer.WriteBanner("Cosine Dump Pip Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.CosineDumpPip), "Generates a dump file containing information about pip(s)");
            writer.WriteLine("Required");
            writer.WriteOption(OutputFileOption, "The location of the output file.");
            writer.WriteLine("Optional");
            writer.WriteOption(PipHashOption, "The formatted semistable hash of a pip to dump (must start with 'Pip', e.g., 'PipC623BCE303738C69')");
            writer.WriteOption(PipTypeOption, "The type of pips to dump data for");
            writer.WriteOption(MetadataOnlyOption, "Dump only the metadata for the pips, no details associated with specific types of pips.");
        }
    }

    public class DumpPipFilters
    {
        public DumpPipFilters() { }

        /// <summary>
        /// Only dump data on pips of this type
        /// </summary>
        public PipType? PipTypeFilter { get; set; }

        /// <summary>
        /// Only dump data on the pip with this SemiStableHash
        /// </summary>
        public long PipSemiStableHash { get; set; }

        /// <summary>
        /// Only dump the metadata of the Pips. No data specific to a Pips type
        /// </summary>
        public bool OnlyMetadata { get; set; }

        /// <summary>
        /// Only dump pips described by this spec file
        /// </summary>
        public string SpecFilePath { get; set; }

        /// Potential Future Filters/Flags
        ///     Dump pips that performed no work, represented by zeroed SSHs
        ///     Given a spec file path, only dump pips created from that .dsc file
    }

    /// <summary>
    /// Creates a JSON file containing information about the pips used during a BuildXL invocation
    /// </summary>
    public sealed class CosineDumpPip : Analyzer
    {
        private readonly string OutputFilePath;
        private readonly DumpPipFilters Filters;

        public CosineDumpPip(AnalysisInput input, string outputFilePath, DumpPipFilters filters)
            : base(input)
        {
            OutputFilePath = outputFilePath;
            Filters = filters;
        }

        public override int Analyze()
        {
            List<PipReference> pipList = GeneratePipList();

            PrintPipCountData(pipList);
            
            using (StreamWriter streamWriter = new StreamWriter(OutputFilePath))
            using (JsonWriter jsonWriter = new JsonTextWriter(streamWriter))
            {
                JsonSerializer serializer = new JsonSerializer { Formatting = Formatting.Indented,  };
                serializer.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                serializer.Serialize(jsonWriter, null);
                // To reduce memory usage, we write to the JSON one PipInfo at a time instead of as one big List of PipInfos
                foreach(PipReference reference in pipList)
                {
                    GenerateAndSerializePipInfo(reference, jsonWriter, serializer);
                }
            }

            return 0;
        }

        /// <summary>
        /// Generates a list of PipReferences based on the filters from the user
        /// </summary>
        /// <returns>The list of PipReferences that match the filters</returns>
        public List<PipReference> GeneratePipList()
        {
            List<PipReference> toFilter = PipGraph.AsPipReferences(PipTable.StableKeys, PipQueryContext.PipGraphRetrieveAllPips).ToList();
            
            // For performance, we filter by removing from the list back to front
            for(int i = toFilter.Count - 1; i >= 0; i--)
            {
                // We always filter out Pips with zeroed SSHs since they didn't perform any work
                if (Convert.ToInt64(toFilter[i].SemiStableHash) == 0)
                {
                    toFilter.RemoveAt(i);
                }
                // Filter out Pips that don't match the PipType filter
                else if(Filters.PipTypeFilter != null && 
                        toFilter[i].PipType != Filters.PipTypeFilter.Value)
                {
                    toFilter.RemoveAt(i);
                }
                // Filter out Pips that don't match the SSH filter
                else if (Filters.PipSemiStableHash != 0 &&
                            toFilter[i].SemiStableHash != Filters.PipSemiStableHash)
                {
                    toFilter.RemoveAt(i);
                }
            }

            return toFilter;
        }

        /// <summary>
        /// Prints out information about the PipTypes of a collection
        /// </summary>
        public void PrintPipCountData(List<PipReference> pipList)
        {
            Dictionary<PipType, int> pipDictionary = new Dictionary<PipType, int>();

            foreach (PipReference pip in pipList)
            {
                PipType type = pip.PipType;

                if(!pipDictionary.ContainsKey(type))
                {
                    pipDictionary[type] = 1;
                }
                else
                {
                    pipDictionary[type]++;
                }
            }

            Console.WriteLine($"\nNumber of Pips found matching filters: '{pipList.Count}'");
            Console.WriteLine($"Types of Pips and counts");
            foreach(var pair in pipDictionary)
            {
                Console.WriteLine($"{pair.Key}: {pair.Value}");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Generates the PipInfo for a given pip, and then serializes it
        /// </summary>
        /// <param name="pipReference">PipReference to hydrate into a complete Pip</param>
        /// <param name="writer">Writer to write data to</param>
        /// <param name="serializer">Serializer to serialize with</param>
        public void GenerateAndSerializePipInfo(PipReference pipReference, JsonWriter writer, JsonSerializer serializer)
        {
            Pip pip = pipReference.HydratePip();

            // All PipInfos will have metadata
            PipInfo generatedPipInfo = new PipInfo
            {
                PipMetadata = GeneratePipMetadata(pip)
            };

            if (Filters.OnlyMetadata)
            {
                serializer.Serialize(writer, generatedPipInfo);
                return;
            }
            // Fill the data depending on the type of Pip
            switch (pip.PipType)
            {
                case PipType.CopyFile:
                    generatedPipInfo.CopyFilePipDetails = GenerateCopyFilePipDetails((CopyFile)pip);
                    break;
                case PipType.Process:
                    generatedPipInfo.ProcessPipDetails = GenerateProcessPipDetails((Process)pip);
                    break;
                case PipType.Ipc:
                    generatedPipInfo.IpcPipDetails = GenerateIpcPipDetails((IpcPip)pip);
                    break;
                case PipType.Value:
                    generatedPipInfo.ValuePipDetails = GenerateValuePipDetails((ValuePip)pip);
                    break;
                case PipType.SpecFile:
                    generatedPipInfo.SpecFilePipDetails = GenerateSpecFilePipDetails((SpecFilePip)pip);
                    break;
                case PipType.Module:
                    generatedPipInfo.ModulePipDetails = GenerateModulePipDetails((ModulePip)pip);
                    break;
                case PipType.HashSourceFile:
                    generatedPipInfo.HashSourceFilePipDetails = GenerateHashSourceFilePipDetails((HashSourceFile)pip);
                    break;
                case PipType.SealDirectory:
                    generatedPipInfo.SealDirectoryPipDetails = GenerateSealDirectoryPipDetails((SealDirectory)pip);
                    break;
                case PipType.WriteFile:
                    generatedPipInfo.WriteFilePipDetails = GenerateWriteFilePipDetails((WriteFile)pip);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            serializer.Serialize(writer, generatedPipInfo);
        }

        /// <summary>
        /// Generates the PipMetadata for a given Pip
        /// </summary>
        public PipMetadata GeneratePipMetadata(Pip pip)
        {
            PipMetadata pipMetadata = new PipMetadata
            {
                PipId = pip.PipId.Value,
                SemiStableHash = pip.FormattedSemiStableHash,
                PipType = pip.PipType,
                Tags = pip.Tags.IsValid ? pip.Tags.Select(tag => tag.ToString(StringTable)).ToList() : null
            };
            pipMetadata.Tags = pipMetadata.Tags.Any() ? pipMetadata.Tags : null;

            PipProvenance provenance = pip.Provenance;
            pipMetadata.Qualifier = PipGraph.Context.QualifierTable.GetCanonicalDisplayString(provenance.QualifierId);
            pipMetadata.Usage = provenance.Usage.IsValid ? provenance.Usage.ToString(PathTable) : null;
            pipMetadata.SpecFilePath = provenance.Token.Path.ToString(PathTable);
            pipMetadata.OutputValueSymbol = provenance.OutputValueSymbol.ToString(SymbolTable);
            pipMetadata.ModuleId = provenance.ModuleId.Value.Value;
            pipMetadata.SpecFilePath = provenance.Token.Path.ToString(PathTable);

            pipMetadata.PipDependencies = PipGraph.RetrievePipReferenceImmediateDependencies(pip.PipId, null)
                                                                .Where(pipRef => pipRef.PipType != PipType.HashSourceFile)
                                                                .Select(pipRef => pipRef.PipId)
                                                                .Select(pipId => PipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer))
                                                                .Select(pipHash => pipHash.FormattedSemiStableHash)
                                                                .ToList();

            pipMetadata.PipDependents = PipGraph.RetrievePipReferenceImmediateDependents(pip.PipId, null)
                                                                .Select(pipRef => pipRef.PipId)
                                                                .Select(pipId => PipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer))
                                                                .Select(pipData => pipData.FormattedSemiStableHash)
                                                                .ToList();
            pipMetadata.PipDependencies = pipMetadata.PipDependencies.Any() ? pipMetadata.PipDependencies : null;
            pipMetadata.PipDependents = pipMetadata.PipDependents.Any() ? pipMetadata.PipDependents : null;

            return pipMetadata;
        }

        #region PipDetailsGenerators

        ///
        /// Notes
        /// * To keep from serializing the empty enumerables that LINQ can create, they must be manually made null if the list is empty
        ///

        /// <summary>
        /// Generates the CopyFilePipDetails for a given Pip
        /// </summary>
        public CopyFilePipDetails GenerateCopyFilePipDetails(CopyFile pip)
        {
            CopyFilePipDetails copyFilePipDetails = new CopyFilePipDetails
            {
                Source = pip.Source.IsValid ? pip.Source.Path.ToString(PathTable) : null,
                Destination = pip.Destination.IsValid ? pip.Destination.Path.ToString(PathTable) : null
            };

            return copyFilePipDetails;
        }

        /// <summary>
        /// Generates the ProcessPipDetails for a given Pip
        /// </summary>
        public ProcessPipDetails GenerateProcessPipDetails(Process pip)
        {
            ProcessPipDetails processPipDetails = new ProcessPipDetails();

            InvocationDetails invoDetails = new InvocationDetails
            {
                Executable = pip.Executable.IsValid ? pip.Executable.Path.ToString(PathTable): null,
                ToolDescription = pip.ToolDescription.IsValid ? pip.ToolDescription.ToString(StringTable) : null,
                ResponseFilePath = pip.ResponseFile.IsValid ? pip.ResponseFile.Path.ToString(PathTable) : null,
                Arguments = pip.Arguments.IsValid ? pip.Arguments.ToString(PathTable) : null,
                ResponseFileContents = pip.ResponseFileData.IsValid ? pip.ResponseFileData.ToString(PathTable) : null,
            };
            invoDetails.EnvironmentVariables = pip.EnvironmentVariables.
                                                Select(x => new KeyValuePair<string, string>
                                                (x.Name.ToString(StringTable), x.Value.IsValid ? x.Value.ToString(PathTable) : null))
                                                .ToList();
            invoDetails.EnvironmentVariables = invoDetails.EnvironmentVariables.Any() ? invoDetails.EnvironmentVariables : null;
            processPipDetails.InvocationDetails = invoDetails;

            InputOutputDetails inOutDetails = new InputOutputDetails
            {
                STDInFile = pip.StandardInput.File.IsValid ? pip.StandardInput.File.Path.ToString(PathTable): null,
                STDOut = pip.StandardOutput.IsValid ? pip.StandardOutput.Path.ToString(PathTable): null,
                STDError = pip.StandardError.IsValid ? pip.StandardError.Path.ToString() : null,
                STDDirectory = pip.StandardDirectory.IsValid ? pip.StandardDirectory.ToString(PathTable) : null,
                WarningRegex = pip.WarningRegex.IsValid ? pip.WarningRegex.Pattern.ToString(StringTable) : null,
                ErrorRegex = pip.ErrorRegex.IsValid ? pip.ErrorRegex.Pattern.ToString(StringTable) : null,
                STDInData = pip.StandardInputData.IsValid ? pip.StandardInputData.ToString(PathTable) : null
            };

            processPipDetails.InputOutputDetails = inOutDetails;

            DirectoryDetails dirDetails = new DirectoryDetails
            {
                WorkingDirectory = pip.WorkingDirectory.IsValid ? pip.WorkingDirectory.ToString(PathTable) : null,
                UniqueOutputDirectory = pip.UniqueOutputDirectory.IsValid ? pip.UniqueOutputDirectory.ToString(PathTable) : null,
                TempDirectory = pip.TempDirectory.IsValid ? pip.TempDirectory.ToString(PathTable) : null,
            };
            if(pip.AdditionalTempDirectories.IsValid)
            {
                dirDetails.ExtraTempDirectories = pip.AdditionalTempDirectories.
                                                    Select(x => x.ToString(PathTable))
                                                    .ToList();
            }
            dirDetails.ExtraTempDirectories = dirDetails.ExtraTempDirectories.Any() ? dirDetails.ExtraTempDirectories : null;
            processPipDetails.DirectoryDetails = dirDetails;

            AdvancedOptions advancedOptions = new AdvancedOptions
            {
                TimeoutWarning = pip.WarningTimeout.GetValueOrDefault(),
                TimeoutError = pip.Timeout.GetValueOrDefault(),
                SuccessCodes = pip.SuccessExitCodes.ToList(),
                Semaphores =  pip.Semaphores.Select(x => x.Name.ToString(StringTable)).ToList(),
                HasUntrackedChildProcesses = pip.HasUntrackedChildProcesses,
                ProducesPathIndependentOutputs = pip.ProducesPathIndependentOutputs,
                OutputsMustRemainWritable = pip.OutputsMustRemainWritable,
                AllowPreserveOutputs = pip.AllowPreserveOutputs
            };
            advancedOptions.SuccessCodes = advancedOptions.SuccessCodes.Any() ? advancedOptions.SuccessCodes : null;
            advancedOptions.Semaphores = advancedOptions.Semaphores.Any() ? advancedOptions.Semaphores : null;
            processPipDetails.AdvancedOptions = advancedOptions;

            ProcessInputOutputDetails procInOutDetails = new ProcessInputOutputDetails
            {
                FileDependencies = pip.Dependencies.Select(x => x.IsValid ? x.Path.ToString(PathTable) : null).ToList(),
                DirectoryDependencies = pip.DirectoryDependencies.Select(x => x.IsValid ? x.Path.ToString(PathTable) : null).ToList(),
                OrderDependencies = pip.OrderDependencies.Select(x => x.Value).ToList(),
                FileOutputs = pip.FileOutputs.Select(x => x.IsValid ? x.Path.ToString(PathTable) : null).ToList(),
                DirectoryOuputs = pip.DirectoryOutputs.Select(x => x.IsValid ? x.Path.ToString(PathTable) : null).ToList(),
                UntrackedPaths = pip.UntrackedPaths.Select(x => x.IsValid ? x.ToString(PathTable) : null).ToList(),
                UntrackedScopes = pip.UntrackedScopes.Select(x => x.IsValid ? x.ToString(PathTable) : null).ToList(),
            };
            procInOutDetails.FileDependencies = procInOutDetails.FileDependencies.Any() ? procInOutDetails.FileDependencies : null;
            procInOutDetails.DirectoryDependencies = procInOutDetails.DirectoryDependencies.Any() ? procInOutDetails.DirectoryDependencies : null;
            procInOutDetails.OrderDependencies = procInOutDetails.OrderDependencies.Any() ? procInOutDetails.OrderDependencies : null;
            procInOutDetails.FileOutputs = procInOutDetails.FileOutputs.Any() ? procInOutDetails.FileOutputs : null;
            procInOutDetails.DirectoryOuputs = procInOutDetails.DirectoryOuputs.Any() ? procInOutDetails.DirectoryOuputs : null;
            procInOutDetails.UntrackedPaths = procInOutDetails.UntrackedPaths.Any() ? procInOutDetails.UntrackedPaths : null;
            procInOutDetails.UntrackedScopes = procInOutDetails.UntrackedScopes.Any() ? procInOutDetails.UntrackedScopes : null;
            processPipDetails.ProcessInputOutputDetails = procInOutDetails;

            ServiceDetails servDetails = new ServiceDetails
            {
                IsService = pip.IsService,
                ShutdownProcessPipId = pip.ShutdownProcessPipId.Value,
                ServicePipDependencies = pip.ServicePipDependencies.Select(x => x.Value).ToList(),
                IsStartOrShutdownKind = pip.IsStartOrShutdownKind
            };
            servDetails.ServicePipDependencies = servDetails.ServicePipDependencies.Any() ? servDetails.ServicePipDependencies : null;
            processPipDetails.ServiceDetails = servDetails;

            return processPipDetails;
        }

        /// <summary>
        /// Generates the IpcPipDetails for a given Pip
        /// </summary>
        public IpcPipDetails GenerateIpcPipDetails(IpcPip pip)
        {
            IpcPipDetails ipcPipDetails = new IpcPipDetails
            {
                IpcMonikerId = pip.IpcInfo.IpcMonikerId.Value,
                MessageBody = pip.MessageBody.IsValid ? pip.MessageBody.ToString(PathTable) : null,
                OutputFile = pip.OutputFile.Path.ToString(PathTable),
                ServicePipDependencies = pip.ServicePipDependencies.Select(x => x.Value).ToList(),
                FileDependencies = pip.FileDependencies.Select(x => x.Path.ToString(PathTable)).ToList(),
                LazilyMaterializedDependencies = pip.LazilyMaterializedDependencies.Select(x => x.Path.ToString(PathTable)).ToList(),
                IsServiceFinalization = pip.IsServiceFinalization,
                MustRunOnMaster = pip.MustRunOnMaster
            };
            ipcPipDetails.ServicePipDependencies = ipcPipDetails.ServicePipDependencies.Any() ? ipcPipDetails.ServicePipDependencies : null;
            ipcPipDetails.FileDependencies = ipcPipDetails.FileDependencies.Any() ? ipcPipDetails.FileDependencies : null;
            ipcPipDetails.LazilyMaterializedDependencies = ipcPipDetails.LazilyMaterializedDependencies.Any() ? ipcPipDetails.LazilyMaterializedDependencies : null;

            return ipcPipDetails;
        }

        /// <summary>
        /// Generates the ValuePipDetails for a given Pip
        /// </summary>
        public ValuePipDetails GenerateValuePipDetails(ValuePip pip)
        {
            ValuePipDetails valuePipDetails = new ValuePipDetails
            {
                Symbol = pip.Symbol.ToString(SymbolTable),
                Qualifier = pip.Qualifier.GetHashCode(),
                SpecFilePath = pip.LocationData.Path.ToString(PathTable),
                Location = pip.LocationData.ToString(PathTable)
            };

            return valuePipDetails;
        }

        /// <summary>
        /// Generates the SpecFilePipDetails for a given Pip
        /// </summary>
        public SpecFilePipDetails GenerateSpecFilePipDetails(SpecFilePip pip)
        {
            SpecFilePipDetails specFilePipDetails = new SpecFilePipDetails
            {
                SpecFile = pip.SpecFile.Path.ToString(PathTable),
                DefinitionFilePath = pip.DefinitionLocation.Path.ToString(PathTable),
                Location = pip.DefinitionLocation.ToString(PathTable),
                ModuleId = pip.OwningModule.Value.Value
            };

            return specFilePipDetails;
        }

        /// <summary>
        /// Generates the ModulePipDetails for a given Pip
        /// </summary>
        public ModulePipDetails GenerateModulePipDetails(ModulePip pip)
        {
            ModulePipDetails modulePipDetails = new ModulePipDetails
            {
                Identity = pip.Identity.ToString(StringTable),
                DefinitionFilePath = pip.Location.Path.ToString(PathTable),
                DefinitionPath = pip.Location.ToString(PathTable)
            };

            return modulePipDetails;
        }

        /// <summary>
        /// Generates the HashSourceFilePipDetails for a given Pip
        /// </summary>
        public HashSourceFilePipDetails GenerateHashSourceFilePipDetails(HashSourceFile pip)
        {
            HashSourceFilePipDetails hashSourceFilePipDetails = new HashSourceFilePipDetails
            {
                FileHashed = pip.Artifact.Path.ToString(PathTable)
            };

            return hashSourceFilePipDetails;
        }

        /// <summary>
        /// Generates the SealDirectoryPipDetails for a given Pip
        /// </summary>
        public SealDirectoryPipDetails GenerateSealDirectoryPipDetails(SealDirectory pip)
        {
            SealDirectoryPipDetails sealDirectoryPipDetails = new SealDirectoryPipDetails
            {
                Kind = pip.Kind,
                Scrub = pip.Scrub,
                DirectoryRoot = pip.Directory.Path.ToString(PathTable),
                Contents = pip.Contents.Select(x => x.Path.ToString(PathTable)).ToList()
            };
            sealDirectoryPipDetails.Contents = sealDirectoryPipDetails.Contents.Any() ? sealDirectoryPipDetails.Contents : null;

            return sealDirectoryPipDetails;
        }

        /// <summary>
        /// Generates the WriteFilePipDetails for a given Pip
        /// </summary>
        public WriteFilePipDetails GenerateWriteFilePipDetails(WriteFile pip)
        {
            WriteFilePipDetails writeFilePipDetails = new WriteFilePipDetails
            {
                Destination = pip.Destination.IsValid ? pip.Destination.Path.ToString(PathTable) : null,
                FileEncoding = pip.Encoding,
                Tags = pip.Tags.IsValid ? pip.Tags.Select(tag => tag.ToString(StringTable)).ToList() : null
            };
            writeFilePipDetails.Tags = writeFilePipDetails.Tags.Any() ? writeFilePipDetails.Tags : null;

            return writeFilePipDetails;
        }

        #endregion
    }

}