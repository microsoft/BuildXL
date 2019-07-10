// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Values;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Class that contains the full Transformer which is to be deprecated
    /// </summary>
    public sealed class AmbientTransformerOriginal : AmbientTransformerBase
    {
        internal static readonly string Name = "Transformer";

        /// <nodoc />
        public AmbientTransformerOriginal(PrimitiveTypes knownTypes)
            : base(Name, knownTypes)
        {
        }
    }

    /// <summary>
    /// Class that contains the ambient bindings for "Sdk.Transformers" module.
    /// </summary>
    public sealed class AmbientTransformerHack : AmbientTransformerBase
    {
        internal static readonly string Name = AmbientHack.GetName(AmbientTransformerOriginal.Name);
        /// <nodoc />
        public AmbientTransformerHack(PrimitiveTypes knownTypes)
            : base(Name, knownTypes)
        {
        }
    }

    /// <summary>
    /// Ambient definition for namespace Transformer.
    /// </summary>
    public abstract partial class AmbientTransformerBase : AmbientDefinitionBase
    {
        internal string TransformerName { get; }

        /// <nodoc />
        public AmbientTransformerBase(string transformerName, PrimitiveTypes knownTypes)
            : base(transformerName, knownTypes)
        {
            TransformerName = transformerName;

            InitializeProcessNames();
            InitializeProcessOutputNames();
            IntializeIpcNames();
            InitializeSealDirectoryNames();
            InitializeWriteNames();

            InitializeSignaturesAndStatsForProcessOutputs(StringTable);
        }


        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                TransformerName,
                new[]
                {
                    Function(ExecuteFunctionName, Execute, ExecuteSignature),
                    Function(CreateServiceFunctionName, CreateService, CreateServiceSignature),
                    Function(CopyFileFunctionName, CopyFile, CopyFileSignature),
                    Function(WriteFileFunctionName, WriteFile, WriteFileSignature),
                    Function(WriteDataFunctionName, WriteData, WriteDataSignature),
                    Function(WriteAllLinesFunctionName, WriteAllLines, WriteAllLinesSignature),
                    Function(WriteAllTextFunctionName, WriteAllText, WriteAllTextSignature),
                    Function(SealDirectoryFunctionName, SealDirectory, SealDirectorySignature),
                    Function(SealSourceDirectoryFunctionName, SealSourceDirectory, SealSourceDirectorySignature),
                    Function(SealPartialDirectoryFunctionName, SealPartialDirectory, SealPartialDirectorySignature),
                    Function(ComposeSharedOpaqueDirectoriesFunctionName, ComposeSharedOpaqueDirectories, ComposeSharedOpaqueDirectoriesSignature),
                    Function(GetNewIpcMonikerFunctionName, GetNewIpcMoniker, GetNewIpcMonikerSignature),
                    Function(GetIpcServerMonikerFunctionName, GetIpcServerMoniker, GetIpcMonikerSignature),
                    Function(GetDominoIpcServerMonikerFunctionName, GetIpcServerMoniker, GetIpcMonikerSignature),
                    Function(IpcSendFunctionName, IpcSend, IpcSendSignature),
                    Function(ReadGraphFragmentFunctionName, ReadGraphFragment, ReadGraphFragmentSignature)
                });
        }




        #region Classes used for error reporting

        private sealed class MultiArgument
        {
        }

        private sealed class NamedArgument
        {
        }

        private sealed class ResponseFilePlaceHolder
        {
        }

        #endregion
    }
}
