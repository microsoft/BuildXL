// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildXL.LogGen.Generators
{
    /// <summary>
    /// Generator that doesn't emit anything
    /// </summary>
    internal sealed class Noop : GeneratorBase
    {
        public override void GenerateLogMethodBody(LoggingSite site, Func<string> getMessageExpression)
        {
        }

        public override IEnumerable<Tuple<string, string>> ConsumedNamespaces => Enumerable.Empty<Tuple<string, string>>();

        /// <inheritdoc/>
        public override void GenerateClass()
        {
        }
    }
}
