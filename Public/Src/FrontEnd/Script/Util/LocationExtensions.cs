// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

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
