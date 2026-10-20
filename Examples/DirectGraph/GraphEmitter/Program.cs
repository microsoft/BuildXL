// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.DirectGraph;
using Grpc.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GraphEmitter
{
    internal static class Program
    {
        private const string ChannelEnvironmentVariable = "BUILDXL_DIRECT_GRAPH_CHANNEL";

        // Messages larger than gRPC's 4 MB default are possible, for example a process with a long argument list.
        private const int MaxMessageLengthBytes = 256 * 1024 * 1024;

        private static readonly ChannelOption[] s_channelOptions =
        {
            new ChannelOption(Grpc.Core.ChannelOptions.MaxReceiveMessageLength, MaxMessageLengthBytes),
            new ChannelOption(Grpc.Core.ChannelOptions.MaxSendMessageLength, MaxMessageLengthBytes),
        };

        public static int Main(string[] args)
        {
            try
            {
                return RunAsync(args).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static async Task<int> RunAsync(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: GraphEmitter <graph.json>");
                return 1;
            }

            string endpoint = Environment.GetEnvironmentVariable(ChannelEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException($"Environment variable '{ChannelEnvironmentVariable}' is not set.");
            }

            Console.WriteLine($"Using channel '{endpoint}'");

            var definition = JsonConvert.DeserializeObject<GraphDefinition>(await File.ReadAllTextAsync(args[0]));
            if (definition?.Modules == null)
            {
                throw new InvalidDataException("The graph definition must contain a 'modules' array.");
            }

            var state = new EmitterState();
            var moduleNames = new HashSet<string>(StringComparer.Ordinal);
            ulong operationId = 1;

            var channel = new Channel(endpoint, ChannelCredentials.Insecure, s_channelOptions);
            try
            {
                var client = new DirectGraphService.DirectGraphServiceClient(channel);
                using var call = client.BuildGraph();
                var responses = new ResponseReader(call);

                foreach (var module in definition.Modules)
                {
                    if (string.IsNullOrWhiteSpace(module.Name))
                    {
                        throw new InvalidDataException("Every module must have a non-empty 'name'.");
                    }

                    if (!moduleNames.Add(module.Name))
                    {
                        throw new InvalidDataException($"A module named '{module.Name}' is already defined.");
                    }

                    if (module.Operations == null)
                    {
                        throw new InvalidDataException($"Module '{module.Name}' must contain an 'operations' array.");
                    }

                    foreach (var operationJson in module.Operations)
                    {
                        var operation = CreateOperation(operationJson, module.Name, operationId, state, out string operationName, out var producedDirectories);

                        if (state.OperationIds.ContainsKey(operationName))
                        {
                            throw new InvalidDataException($"An operation named '{operationName}' is already defined.");
                        }

                        // References name their producer by operation id, so no operation waits for a response.
                        state.OperationIds.Add(operationName, operationId);
                        state.ProducedDirectories.Add(operationName, producedDirectories);

                        // Written in the order ids were assigned, which is what keeps the registrations in line with BuildXL's.
                        state.Values.AttachNewValues(operation);
                        await responses.WriteAsync(operation);
                        operationId++;
                    }
                }

                await responses.WriteAsync(new GraphOperation
                {
                    OperationId = operationId,
                    Complete = new CompleteRequest(),
                });

                await responses.Completion;
                await call.RequestStream.CompleteAsync();
            }
            finally
            {
                await channel.ShutdownAsync();
            }

            return 0;
        }

        private static GraphOperation CreateOperation(
            JObject operationJson,
            string moduleName,
            ulong operationId,
            EmitterState state,
            out string operationName,
            out List<string> producedDirectories)
        {
            var header = operationJson.ToObject<OperationDefinitionHeader>();
            if (string.IsNullOrWhiteSpace(header?.Name))
            {
                throw new InvalidDataException("Every operation must have a non-empty 'name'.");
            }

            operationName = header.Name;
            producedDirectories = new List<string>();

            return header.Kind switch
            {
                "writeFile" => CreateWriteFileOperation(operationJson.ToObject<WriteFileOperationDefinition>(), moduleName, operationId, state),
                "process" => CreateProcessOperation(operationJson.ToObject<ProcessOperationDefinition>(), moduleName, operationId, state, producedDirectories),
                "copyFile" => CreateCopyFileOperation(operationJson.ToObject<CopyFileOperationDefinition>(), moduleName, operationId, state),
                "sealDirectory" => CreateSealDirectoryOperation(operationJson.ToObject<SealDirectoryOperationDefinition>(), moduleName, operationId, state, producedDirectories),
                "composeSharedOpaque" => CreateComposeSharedOpaqueOperation(operationJson.ToObject<ComposeSharedOpaqueOperationDefinition>(), moduleName, operationId, state, producedDirectories),
                _ => throw new InvalidDataException($"Operation '{operationName}' has unsupported kind '{header.Kind}'."),
            };
        }

        private static DirectoryArtifactReference ResolveDirectoryReference(DirectoryReferenceDefinition definition, EmitterState state)
        {
            if (!state.OperationIds.TryGetValue(definition.ProducedBy, out ulong producerOperationId) ||
                !state.ProducedDirectories.TryGetValue(definition.ProducedBy, out var directories) ||
                directories.Count == 0)
            {
                throw new InvalidDataException(
                    $"Directory reference names operation '{definition.ProducedBy}', which has not been emitted or produced no directory.");
            }

            string path;
            if (string.IsNullOrWhiteSpace(definition.Path))
            {
                if (directories.Count != 1)
                {
                    throw new InvalidDataException(
                        $"Operation '{definition.ProducedBy}' produced {directories.Count} directories, so the reference must specify 'path'.");
                }

                path = directories[0];
            }
            else
            {
                string wanted = ResolvePath(definition.Path);
                path = directories.FirstOrDefault(directory => string.Equals(directory, wanted, StringComparison.OrdinalIgnoreCase));

                if (path == null)
                {
                    throw new InvalidDataException(
                        $"Operation '{definition.ProducedBy}' did not produce directory '{wanted}'. It produced: {string.Join(", ", directories)}.");
                }
            }

            return new DirectoryArtifactReference
            {
                ProducerOperationId = producerOperationId,
                Path = state.Values.Path(path),
            };
        }

        private static GraphOperation CreateComposeSharedOpaqueOperation(
            ComposeSharedOpaqueOperationDefinition definition,
            string moduleName,
            ulong operationId,
            EmitterState state,
            List<string> producedDirectories)
        {
            string directoryRoot = ResolvePath(definition.DirectoryRoot);
            producedDirectories.Add(directoryRoot);

            var request = new AddCompositeSharedOpaqueDirectoryRequest
            {
                Module = moduleName,
                Symbol = definition.Name,
                DirectoryRoot = state.Values.Path(directoryRoot),
                Action = CompositionActionKind.WidenDirectoryCone,
            };

            if (definition.Description != null)
            {
                request.Description = state.Values.String(definition.Description);
            }

            foreach (var content in definition.Contents)
            {
                request.Contents.Add(ResolveDirectoryReference(content, state));
            }

            if (!string.IsNullOrWhiteSpace(definition.ExcludeRegex))
            {
                request.ContentFilter = new ContentFilter { Kind = ContentFilterKind.Exclude, Regex = definition.ExcludeRegex };
            }

            return new GraphOperation
            {
                OperationId = operationId,
                AddCompositeSharedOpaqueDirectory = request,
            };
        }

        private static GraphOperation CreateCopyFileOperation(
            CopyFileOperationDefinition definition,
            string moduleName,
            ulong operationId,
            EmitterState state)
        {
            var request = new AddCopyFileRequest
            {
                Module = moduleName,
                Symbol = definition.Name,
                Source = CreateFileReference(definition.Source, state),
                Destination = state.Values.Path(ResolvePath(definition.Destination)),
            };

            if (definition.Description != null)
            {
                request.Description = state.Values.String(definition.Description);
            }

            return new GraphOperation
            {
                OperationId = operationId,
                AddCopyFile = request,
            };
        }

        private static GraphOperation CreateSealDirectoryOperation(
            SealDirectoryOperationDefinition definition,
            string moduleName,
            ulong operationId,
            EmitterState state,
            List<string> producedDirectories)
        {
            string directoryRoot = ResolvePath(definition.DirectoryRoot);
            producedDirectories.Add(directoryRoot);

            var request = new AddSealDirectoryRequest
            {
                Module = moduleName,
                Symbol = definition.Name,
                DirectoryRoot = state.Values.Path(directoryRoot),
                Kind = ParseSealDirectoryKind(definition.SealKind),
            };

            if (definition.Description != null)
            {
                request.Description = state.Values.String(definition.Description);
            }

            foreach (var content in definition.Contents ?? Array.Empty<FileReferenceDefinition>())
            {
                request.Contents.Add(CreateFileReference(content, state));
            }

            return new GraphOperation
            {
                OperationId = operationId,
                AddSealDirectory = request,
            };
        }

        private static SealDirectoryKind ParseSealDirectoryKind(string kind) => (kind ?? "partial") switch
        {
            "partial" => SealDirectoryKind.Partial,
            "full" => SealDirectoryKind.Full,
            "sourceAllDirectories" => SealDirectoryKind.SourceAllDirectories,
            "sourceTopDirectoryOnly" => SealDirectoryKind.SourceTopDirectoryOnly,
            _ => throw new InvalidDataException($"Unsupported seal directory kind '{kind}'."),
        };

        private static ProcessOptions ParseProcessOptions(IReadOnlyList<string> options)
        {
            var result = new ProcessOptions();

            foreach (string option in options ?? Array.Empty<string>())
            {
                switch (option)
                {
                    case "dependsOnCurrentOs":
                        result.DependsOnCurrentHostOsDirectories = true;
                        break;
                    case "dependsOnWindowsAppData":
                        result.DependsOnWindowsAppData = true;
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported process option '{option}'.");
                }
            }

            return result;
        }

        /// <summary>
        /// Expands environment variables and resolves a possibly relative path against the current working directory.
        /// </summary>
        /// <remarks>
        /// BuildXL launches this tool with the resolver's configured working directory, so a graph definition can use
        /// paths relative to it and stay portable across enlistments.
        /// </remarks>
        private static string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        }

        private static GraphOperation CreateWriteFileOperation(
            WriteFileOperationDefinition definition,
            string moduleName,
            ulong operationId,
            EmitterState state)
        {
            var request = new AddWriteFileRequest
            {
                Module = moduleName,
                Symbol = definition.Name,
                Destination = state.Values.Path(ResolvePath(definition.Destination)),
                Content = state.Values.String(definition.Content),
            };

            if (definition.Description != null)
            {
                request.Description = state.Values.String(definition.Description);
            }

            return new GraphOperation
            {
                OperationId = operationId,
                AddWriteFile = request,
            };
        }

        private static GraphOperation CreateProcessOperation(
            ProcessOperationDefinition definition,
            string moduleName,
            ulong operationId,
            EmitterState state,
            List<string> producedDirectories)
        {
            var values = state.Values;
            var request = new AddProcessRequest
            {
                Module = moduleName,
                Symbol = definition.Name,
                Executable = new FileArtifactReference { SourcePath = values.Path(ResolvePath(definition.Executable)) },
                WorkingDirectory = values.Path(ResolvePath(definition.WorkingDirectory)),
                Options = ParseProcessOptions(definition.Options),
            };

            if (definition.Description != null)
            {
                request.Description = values.String(definition.Description);
            }

            foreach (string argument in definition.Arguments ?? Array.Empty<string>())
            {
                request.Arguments.Add(values.String(Environment.ExpandEnvironmentVariables(argument)));
            }

            foreach (string outputFile in definition.OutputFiles ?? Array.Empty<string>())
            {
                request.OutputFiles.Add(new OutputFile
                {
                    Path = values.Path(ResolvePath(outputFile)),
                    Existence = FileExistenceKind.Required,
                });
            }

            foreach (var rewritten in definition.RewrittenFiles ?? Array.Empty<FileReferenceDefinition>())
            {
                var reference = CreateFileReference(rewritten, state);
                if (reference.ReferenceCase != FileArtifactReference.ReferenceOneofCase.ProducedFile)
                {
                    throw new InvalidDataException("A rewritten file must specify 'producedBy' and 'path'.");
                }

                request.OutputFiles.Add(new OutputFile
                {
                    Rewrites = reference.ProducedFile,
                    Existence = FileExistenceKind.Required,
                });
            }

            foreach (string producedDirectory in definition.ProducesDirectories ?? Array.Empty<string>())
            {
                string path = ResolvePath(producedDirectory);
                producedDirectories.Add(path);
                request.OutputDirectories.Add(new OutputDirectory
                {
                    Path = values.Path(path),
                    Kind = OutputDirectoryKind.Shared,
                });
            }

            foreach (var inputDirectory in definition.InputDirectories ?? Array.Empty<DirectoryReferenceDefinition>())
            {
                request.InputDirectories.Add(ResolveDirectoryReference(inputDirectory, state));
            }

            foreach (var variable in definition.Environment ?? new Dictionary<string, string>())
            {
                request.EnvironmentVariables.Add(new EnvironmentVariableEntry
                {
                    Name = values.String(variable.Key),
                    Value = values.String(variable.Value),
                });
            }

            foreach (var input in definition.InputFiles ?? Array.Empty<FileReferenceDefinition>())
            {
                request.InputFiles.Add(CreateFileReference(input, state));
            }

            return new GraphOperation
            {
                OperationId = operationId,
                AddProcess = request,
            };
        }

        private static FileArtifactReference CreateFileReference(FileReferenceDefinition definition, EmitterState state)
        {
            bool hasSourcePath = !string.IsNullOrWhiteSpace(definition.SourcePath);
            bool hasProducedFile = !string.IsNullOrWhiteSpace(definition.ProducedBy) && !string.IsNullOrWhiteSpace(definition.Path);

            if (hasSourcePath == hasProducedFile)
            {
                throw new InvalidDataException(
                    "An input file must specify either 'sourcePath' or both 'producedBy' and 'path'.");
            }

            if (hasSourcePath)
            {
                return new FileArtifactReference { SourcePath = state.Values.Path(ResolvePath(definition.SourcePath)) };
            }

            if (!state.OperationIds.TryGetValue(definition.ProducedBy, out ulong producerOperationId))
            {
                throw new InvalidDataException(
                    $"Input references unknown or not-yet-emitted operation '{definition.ProducedBy}'.");
            }

            return new FileArtifactReference
            {
                ProducedFile = new ProducedFileReference
                {
                    ProducerOperationId = producerOperationId,
                    Path = state.Values.Path(ResolvePath(definition.Path)),
                },
            };
        }

        /// <summary>
        /// What the emitter has sent so far: the operation id it gave each named operation, the directories each one
        /// produces, and the values it has interned.
        /// </summary>
        private sealed class EmitterState
        {
            public DirectGraphValueRegistry Values { get; } = new();

            public Dictionary<string, ulong> OperationIds { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, List<string>> ProducedDirectories { get; } = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// Writes operations and checks each response against the operation it answers. BuildXL answers in the order it
        /// was sent.
        /// </summary>
        /// <remarks>
        /// Reads concurrently with writing: an emitter that wrote without reading would eventually stall both sides on
        /// flow control. The first failure fails every later write and <see cref="Completion"/>.
        /// </remarks>
        private sealed class ResponseReader
        {
            private readonly AsyncDuplexStreamingCall<GraphOperation, GraphOperationResponse> m_call;
            private readonly ConcurrentQueue<GraphOperation> m_unanswered = new();
            private volatile Exception m_failure;

            public ResponseReader(AsyncDuplexStreamingCall<GraphOperation, GraphOperationResponse> call)
            {
                m_call = call;
                Completion = Task.Run(ReadAsync);
            }

            /// <summary>
            /// Completes once Complete has been answered, and faults with the first failure.
            /// </summary>
            public Task Completion { get; }

            public Task WriteAsync(GraphOperation operation)
            {
                if (m_failure != null)
                {
                    throw new InvalidOperationException(m_failure.Message, m_failure);
                }

                operation.ProtocolVersion = DirectGraphProtocol.Version;
                m_unanswered.Enqueue(operation);
                return m_call.RequestStream.WriteAsync(operation);
            }

            private async Task ReadAsync()
            {
                try
                {
                    while (await m_call.ResponseStream.MoveNext(CancellationToken.None))
                    {
                        var response = m_call.ResponseStream.Current;
                        if (!m_unanswered.TryDequeue(out var answered) || answered.OperationId != response.OperationId)
                        {
                            throw new InvalidOperationException(
                                $"Received a response for operation '{response.OperationId}', which is not the next operation awaiting one.");
                        }

                        if (response.Status != GraphOperationResponseStatus.Success)
                        {
                            throw new InvalidOperationException(
                                $"Operation '{answered.OperationId}' failed with status '{response.Status}': {response.Error}");
                        }

                        if (answered.OperationCase == GraphOperation.OperationOneofCase.Complete)
                        {
                            return;
                        }
                    }

                    throw new InvalidOperationException("The server closed the stream before answering every operation.");
                }
                catch (Exception exception)
                {
                    m_failure = exception;
                    throw;
                }
            }
        }

        private sealed class OperationDefinitionHeader
        {
            [JsonProperty("name", Required = Required.Always)]
            public string Name { get; set; }

            [JsonProperty("kind", Required = Required.Always)]
            public string Kind { get; set; }
        }
    }
}
