// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Context required for ast conversion.
    /// </summary>
    internal sealed class AstConversionContext
    {
        internal readonly SymbolAtom WithQualifierKeyword;
        internal readonly SymbolAtom UndefinedLiteral;
        internal readonly SymbolAtom QualifierDeclarationKeyword;
        internal readonly SymbolAtom LegacyPackageKeyword;
        internal readonly SymbolAtom ModuleKeyword;
        internal readonly SymbolAtom TemplateReference;
        internal readonly SymbolAtom RuntimeRootNamespaceSymbol;

        internal readonly FullSymbol UnsafeNamespace;
        internal readonly SymbolAtom UnsafeOutputFile;
        internal readonly SymbolAtom UnsafeExOutputDirectory;

        public AstConversionContext(
            RuntimeModelContext runtimeModelContext,
            AbsolutePath currentSpecPath,
            ISourceFile currentSourceFile,
            FileModuleLiteral currentFileModule)
        {
            RuntimeModelContext = runtimeModelContext;
            CurrentSpecPath = currentSpecPath;
            CurrentSourceFile = currentSourceFile;
            CurrentFileModule = currentFileModule;

            QualifierDeclarationKeyword = CreateSymbol(Constants.Names.CurrentQualifier);
            WithQualifierKeyword = CreateSymbol(Constants.Names.WithQualifierFunction);
            UndefinedLiteral = CreateSymbol(Constants.Names.Undefined);
            LegacyPackageKeyword = CreateSymbol(Constants.Names.LegacyModuleConfigurationFunctionCall);
            ModuleKeyword = CreateSymbol(Constants.Names.ModuleConfigurationFunctionCall);
            TemplateReference = CreateSymbol(Constants.Names.TemplateReference);
            RuntimeRootNamespaceSymbol = CreateSymbol(Constants.Names.RuntimeRootNamespaceAlias);

            UnsafeNamespace = CreateFullSymbol(Constants.Names.UnsafeNamespace);
            UnsafeOutputFile = CreateSymbol(Constants.Names.UnsafeOutputFile);
            UnsafeExOutputDirectory = CreateSymbol(Constants.Names.UnsafeExOutputDirectory);
        }

        private SymbolAtom CreateSymbol(string name) => SymbolAtom.Create(RuntimeModelContext.StringTable, name);

        private FullSymbol CreateFullSymbol(string name) => FullSymbol.Create(RuntimeModelContext.SymbolTable, name);

        public RuntimeModelContext RuntimeModelContext { get; }

        public AbsolutePath CurrentSpecPath { get; }

        public AbsolutePath CurrentSpecFolder => CurrentSpecPath.GetParent(RuntimeModelContext.PathTable);

        public ISourceFile CurrentSourceFile { get; }

        public FileModuleLiteral CurrentFileModule { get; }

        public Logger Logger => RuntimeModelContext.Logger;

        public LoggingContext LoggingContext => RuntimeModelContext.LoggingContext;

        public PathTable PathTable => RuntimeModelContext.PathTable;

        public StringTable StringTable => RuntimeModelContext.StringTable;
    }
}
