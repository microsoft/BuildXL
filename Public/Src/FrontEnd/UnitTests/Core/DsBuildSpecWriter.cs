// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.FrontEnd.Script.Constants;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Class for writing DScript build specifications (packages, projects, sdks).
    /// </summary>
    public sealed class DsBuildSpecWriter
    {
        /// <summary>
        /// Relative path of the build specification with respect to writing directory.
        /// </summary>
        public readonly string RelativePath;

        /// <summary>
        /// The build specification.
        /// </summary>
        public readonly string Spec;

        /// <summary>
        /// Returns true if the build spec if a package.
        /// </summary>
        public bool IsPackage => !string.IsNullOrWhiteSpace(m_name);

        private readonly string m_name;
        private readonly bool m_implicitReferenceSemantics;

        /// <nodoc />
        public DsBuildSpecWriter(string relativePath, string spec)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(relativePath));
            Contract.Requires(spec != null);

            RelativePath = relativePath;
            Spec = spec;
        }

        /// <nodoc />
        public DsBuildSpecWriter(string name, string relativePath, string spec, bool implicitReferenceSemantics = false)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(relativePath));
            Contract.Requires(spec != null);

            m_name = name;
            RelativePath = relativePath;
            Spec = spec;
            m_implicitReferenceSemantics = implicitReferenceSemantics;
        }

        /// <summary>
        /// Writes the build specification into the path (see <see cref="RelativePath" />) relative to <paramref name="directory" />.
        /// </summary>
        /// <param name="directory">Directory for writing the build specification.</param>
        public void Write(string directory, DsTestWriter testWriter)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(directory));

            var fullPath = Path.Combine(directory, RelativePath);
            testWriter.WriteFile(fullPath, Spec);

            if (IsPackage)
            {
                WriteAsPackage(directory, testWriter);
            }
        }

        private void WriteAsPackage(string directory, DsTestWriter testWriter)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(directory));
            Contract.Requires(!string.IsNullOrWhiteSpace(m_name));

            var builder = new StringBuilder();
            builder.AppendLine("module({");
            builder.AppendLine("    name: \"" + m_name + "\",");

            if (m_implicitReferenceSemantics)
            {
                builder.AppendLine("    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,");
            }

            builder.AppendLine("});");

            var fullPath = Path.Combine(directory, Path.ChangeExtension(RelativePath, Names.DotConfigDotDscExtension));
            testWriter.WriteFile(fullPath, builder.ToString());
        }
    }
}
