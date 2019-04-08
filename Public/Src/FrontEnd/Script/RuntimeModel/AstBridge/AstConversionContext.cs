// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.RuntimeModel;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Context required for ast conversion.
    /// </summary>
    internal sealed class AstConversionContext
    {
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
        }

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
