// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    internal static class PipBuilderExtensions
    {
        public static void Add(this PipDataBuilder @this, RelativePath value) => @this.Add(null, value);

        public static void Add(this PipDataBuilder @this, string prefix, RelativePath value)
        {
            var escaping = PipDataFragmentEscaping.CRuntimeArgumentRules;
            var separator = System.IO.Path.DirectorySeparatorChar.ToString();
            using (@this.StartFragment(escaping, separator))
            {
                if (!string.IsNullOrEmpty(prefix))
                {
                    @this.Add(prefix);
                }

                foreach (var atom in value.GetAtoms())
                {
                    @this.Add(atom);
                }
            }
        }
    }
}
