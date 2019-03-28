// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Text;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Script.Util;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Test object for source resolver.
    /// </summary>
    public sealed class SourceResolverTestObject : ResolverTestObject
    {
        private readonly string m_root;
        private readonly List<string> m_packages;

        /// <nodoc />
        public SourceResolverTestObject(string root = null)
        {
            m_root = root;
            m_packages = new List<string>();
        }

        /// <summary>
        /// Adds a path to a package.
        /// </summary>
        /// <param name="packagePath">Package path.</param>
        public void AddPackage(string packagePath)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(packagePath));
            m_packages.Add(packagePath);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append("{ ");
            builder.Append(string.Format(CultureInfo.InvariantCulture, "kind: \"{0}\", ", KnownResolverKind.DScriptResolverKind));

            if (m_root != null)
            {
                builder.Append("root: '");
                builder.Append(PathUtil.NormalizePath(m_root));
                builder.Append("', ");
            }

            if (m_packages.Count > 0)
            {
                builder.Append("modules: [");

                for (int i = 0; i < m_packages.Count; ++i)
                {
                    builder.Append("f`");
                    builder.Append(PathUtil.NormalizePath(m_packages[i]));
                    builder.Append("`");

                    if (i < m_packages.Count - 1)
                    {
                        builder.Append(", ");
                    }
                }

                builder.Append("]");
            }

            builder.Append(" }");

            return builder.ToString();
        }
    }
}
