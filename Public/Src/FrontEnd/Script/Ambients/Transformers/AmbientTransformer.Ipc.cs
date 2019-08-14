// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Ambient definition for namespace Transformer.
    /// </summary>
    public partial class AmbientTransformerBase : AmbientDefinitionBase
    {
        internal const string GetNewIpcMonikerFunctionName = "getNewIpcMoniker";
        internal const string GetIpcServerMonikerFunctionName = "getIpcServerMoniker";
        internal const string GetDominoIpcServerMonikerFunctionName = "getDominoIpcServerMoniker";
        internal const string IpcSendFunctionName = "ipcSend";

        private SymbolAtom m_ipcSendMoniker;
        private SymbolAtom m_ipcSendMessageBody;
        private SymbolAtom m_ipcSendTargetServicePip;
        private SymbolAtom m_ipcSendOutputFile;
        private SymbolAtom m_ipcSendDependencies;
        private SymbolAtom m_ipcSendMaxConnectRetries;
        private SymbolAtom m_ipcSendConnectRetryDelayMillis;
        private SymbolAtom m_ipcSendLazilyMaterializedDependencies;
        private SymbolAtom m_ipcSendMustRunOnMaster;
        private SymbolAtom m_ipcSendResultOutputFile;
        private PathAtom m_ipcObjectFolderName;
        private PathAtom m_ipcOutputFileName;

        private CallSignature GetNewIpcMonikerSignature => CreateSignature(returnType: AmbientTypes.IpcMonikerType);

        private CallSignature GetIpcMonikerSignature => CreateSignature(returnType: AmbientTypes.IpcMonikerType);

        private CallSignature IpcSendSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.IpcMonikerType, AmbientTypes.IpcSendArgumentsType),
            returnType: AmbientTypes.IpcSendResultType);


        private static EvaluationResult GetNewIpcMoniker(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var semiStableHash = context.GetPipConstructionHelper().GetNextSemiStableHash();
            return EvaluationResult.Create(IpcFactory.GetProvider().LoadOrCreateMoniker(string.Format(CultureInfo.InvariantCulture, "{0:X16}", semiStableHash)));
        }

        private static EvaluationResult GetIpcServerMoniker(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(context.GetPipConstructionHelper().PipGraph.GetApiServerMoniker());
        }

        private void IntializeIpcNames()
        {
            m_ipcSendMoniker = Symbol("moniker");
            m_ipcSendMessageBody = Symbol("messageBody");
            m_ipcSendTargetServicePip = Symbol("targetService");
            m_ipcSendOutputFile = Symbol("outputFile");
            m_ipcSendDependencies = Symbol("fileDependencies");
            m_ipcSendMaxConnectRetries = Symbol("maxConnectRetries");
            m_ipcSendConnectRetryDelayMillis = Symbol("connectRetryDelayMillis");
            m_ipcSendLazilyMaterializedDependencies = Symbol("lazilyMaterializedDependencies");
            m_ipcSendMustRunOnMaster = Symbol("mustRunOnMaster");
            
            // IpcSendResult
            m_ipcSendResultOutputFile = Symbol("outputFile");
            m_ipcObjectFolderName = PathAtom.Create(StringTable, "ipc");
            m_ipcOutputFileName = PathAtom.Create(StringTable, "results.txt");
        }

        private EvaluationResult IpcSend(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {

            var obj = Args.AsObjectLiteral(args, 0);

            if (!TryScheduleIpcPip(context, obj, allowUndefinedTargetService: false, isServiceFinalization: false, out var outputFile, out _))
            {
                // Error has been logged
                return EvaluationResult.Error;
            }


            // create and return result
            var result = ObjectLiteral.Create(
                new List<Binding>
                    {
                        new Binding(m_ipcSendResultOutputFile, outputFile, location: default(LineInfo)),
                    },
                context.TopStack.InvocationLocation,
                context.TopStack.Path);
            return EvaluationResult.Create(result);
        }

        private bool TryScheduleIpcPip(Context context, ObjectLiteral obj, bool allowUndefinedTargetService, bool isServiceFinalization, out FileArtifact outputFile, out PipId pipId)
        {
            // IpcClientInfo
            IIpcMoniker moniker = Converter.ExtractRef<IIpcMoniker>(obj, m_ipcSendMoniker, allowUndefined: false);
            int? numRetries = Converter.ExtractNumber(obj, m_ipcSendMaxConnectRetries, allowUndefined: true);
            int? retryDelayMillis = Converter.ExtractNumber(obj, m_ipcSendConnectRetryDelayMillis, allowUndefined: true);
            var clientConfig = new ClientConfig(numRetries, retryDelayMillis);
            var ipcClientInfo = new IpcClientInfo(moniker.ToStringId(context.StringTable), clientConfig);

            // target service pip
            PipId? servicePipId = Converter.ExtractValue<PipId>(obj, m_ipcSendTargetServicePip, allowUndefined: allowUndefinedTargetService);

            // arguments
            PipData arguments;
            ReadOnlyArray<FileArtifact> fileDependencies;
            ReadOnlyArray<DirectoryArtifact> directoryDependencies;
            using (var ipcProcessBuilder = ProcessBuilder.Create(context.PathTable, context.FrontEndContext.GetPipDataBuilder()))
            {
                // process arguments
                ArrayLiteral argumentsArrayLiteral = Converter.ExtractArrayLiteral(obj, m_ipcSendMessageBody);
                TransformerExecuteArgumentsProcessor.ProcessArguments(context, ipcProcessBuilder, argumentsArrayLiteral);

                // input file dependencies
                var dependenciesArray = Converter.ExtractArrayLiteral(obj, m_ipcSendDependencies, allowUndefined: true);
                if (dependenciesArray != null)
                {
                    for (int i = 0; i < dependenciesArray.Length; i++)
                    {
                        ProcessImplicitDependency(ipcProcessBuilder, dependenciesArray[i], convContext: new ConversionContext(pos: i, objectCtx: dependenciesArray));
                    }
                }

                arguments = ipcProcessBuilder.ArgumentsBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);
                fileDependencies = ipcProcessBuilder.GetInputFilesSoFar();
                directoryDependencies = ipcProcessBuilder.GetInputDirectoriesSoFar();
            }

            // output
            AbsolutePath output = Converter.ExtractPath(obj, m_ipcSendOutputFile, allowUndefined: true);
            if (!output.IsValid)
            {
                output = context.GetPipConstructionHelper().GetUniqueObjectDirectory(m_ipcObjectFolderName).Path.Combine(context.PathTable, m_ipcOutputFileName);
            }

            // tags
            string[] tags = null;
            var tagsArray = Converter.ExtractArrayLiteral(obj, m_executeTags, allowUndefined: true);
            if (tagsArray != null && tagsArray.Count > 0)
            {
                tags = new string[tagsArray.Count];
                for (int i = 0; i < tagsArray.Count; i++)
                {
                    tags[i] = Converter.ExpectString(tagsArray[i], context: new ConversionContext(pos: i, objectCtx: tagsArray));
                }
            }

            // skip materialization for files
            FileOrDirectoryArtifact[] skipMaterializationArtifacts = CollectionUtilities.EmptyArray<FileOrDirectoryArtifact>();
            ArrayLiteral skipMaterializationLiteral = Converter.ExtractArrayLiteral(obj, m_ipcSendLazilyMaterializedDependencies, allowUndefined: true);
            if (skipMaterializationLiteral != null)
            {
                skipMaterializationArtifacts = new FileOrDirectoryArtifact[skipMaterializationLiteral.Length];
                for (int i = 0; i < skipMaterializationLiteral.Length; i++)
                {
                    Converter.ExpectFileOrStaticDirectory(
                        skipMaterializationLiteral[i],
                        out var fileArtifact,
                        out var staticDirectory,
                        context: new ConversionContext(pos: i, objectCtx: skipMaterializationLiteral));

                    Contract.Assert(fileArtifact.IsValid ^ staticDirectory != null);

                    skipMaterializationArtifacts[i] = fileArtifact.IsValid
                        ? FileOrDirectoryArtifact.Create(fileArtifact)
                        : FileOrDirectoryArtifact.Create(staticDirectory.Root);
                }
            }

            // must run on master
            var mustRunOnMaster = Converter.ExtractOptionalBoolean(obj, m_ipcSendMustRunOnMaster) == true;

            outputFile = FileArtifact.CreateOutputFile(output);

            // create IPC pip and add it to the graph
            return context.GetPipConstructionHelper().TryAddIpc(
                ipcClientInfo,
                arguments,
                outputFile,
                servicePipDependencies: servicePipId != null ? ReadOnlyArray<PipId>.From(new[] { servicePipId.Value }) : ReadOnlyArray<PipId>.Empty,
                fileDependencies: fileDependencies,
                directoryDependencies : directoryDependencies,
                skipMaterializationFor: ReadOnlyArray<FileOrDirectoryArtifact>.FromWithoutCopy(skipMaterializationArtifacts),
                isServiceFinalization: isServiceFinalization,
                mustRunOnMaster: mustRunOnMaster,
                tags: tags,
                out pipId);
        }
    }
}
