// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using BuildXL.Utilities.Core;

namespace MaterializationDaemon
{
    /// <summary>
    /// Parser of xml-based manifest files.
    /// </summary>
    public class XmlManifestParser : ManifestParser
    {
        private readonly Dictionary<string, string> m_macros;

        /// <inheritdoc/>
        public XmlManifestParser(string fileName, Dictionary<string, string> macros) : base(fileName)
        {
            m_macros = macros;
        }

        /// <inheritdoc/>
        public override List<string> ExtractFiles()
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(m_fileName);

            // Get all 'file' entries
            XmlNodeList fileNodes = xmlDoc.GetElementsByTagName("file");

            var result = new List<string>(fileNodes.Count);

            foreach (XmlNode node in fileNodes)
            {
                using (var pooledStringBuilder = Pools.StringBuilderPool.GetInstance())
                {
                    var sb = pooledStringBuilder.Instance;
                    sb.Append(node.Attributes["importPath"].Value);
                    replace(sb, m_macros);

                    result.Add(Path.Combine(sb.ToString(), node.Attributes["name"].Value));
                }
            }

            return result;

            void replace(StringBuilder builder, Dictionary<string, string> values)
            {
                foreach (var kvp in values)
                {
                    builder.Replace(kvp.Key, kvp.Value);
                }
            }
        }
    }
}
