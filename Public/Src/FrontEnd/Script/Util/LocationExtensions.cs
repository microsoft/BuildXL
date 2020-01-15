// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Instrumentation.Common;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Util
{
    internal static class LocationExtensions
    {
        public static UniversalLocation AsUniversalLocation(this LineInfo lineInfo, ModuleLiteral moduleLiteral, ImmutableContextBase context)
        {
            var path = moduleLiteral.GetPath(context);
            return new UniversalLocation(null, lineInfo, path, context.PathTable);
        }

        public static Location AsLoggingLocation(this LineInfo lineInfo, ModuleLiteral moduleLiteral, ImmutableContextBase context)
        {
            return lineInfo.AsUniversalLocation(moduleLiteral, context).AsLoggingLocation();
        }
    }
}
