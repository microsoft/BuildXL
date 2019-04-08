// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.FrontEnd.Script.Constants;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Class for writing DScript config file and getting writers for build specification.
    /// </summary>
    public sealed class DsConfigFileWriter
    {
        /// <summary>
        /// List of build specification writers.
        /// </summary>
        private readonly List<DsBuildSpecWriter> m_buildSpecWriters;

        /// <summary>
        /// List of source resolvers.
        /// </summary>
        private readonly List<ResolverTestObject> m_resolverTestObjects;

        /// <summary>
        /// Disable default source resolver.
        /// </summary>
        private bool m_disableDefaultSourceResolver;

        /// <summary>
        /// Named qualifiers.
        /// </summary>
        private Dictionary<string, Dictionary<string, string>> m_namedQualifiers;

        /// <summary>
        /// Content of configuration.
        /// </summary>
        private string m_configContent;

        /// <summary>
        /// The primary configuration file name used by the writer
        /// </summary>
        public string PrimaryConfigurationFileName { get; private set; }

        /// <nodoc />
        public DsConfigFileWriter()
        {
            m_buildSpecWriters = new List<DsBuildSpecWriter>();
            m_resolverTestObjects = new List<ResolverTestObject>();
            PrimaryConfigurationFileName = Names.ConfigDsc;
        }

        /// <summary>
        /// Writes the configuration into <paramref name="directory" />.
        /// </summary>
        /// <param name="directory">Directory for writing the configuration.</param>
        public void Write(string directory, DsTestWriter testWriter)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(directory));

            for (int i = 0; i < m_buildSpecWriters.Count; ++i)
            {
                m_buildSpecWriters[i].Write(directory, testWriter);
            }

            var fullPath = Path.Combine(
                directory,
                PrimaryConfigurationFileName);

            testWriter.WriteFile(fullPath, m_configContent ?? ToString());
        }

        /// <summary>
        /// Sets config content.
        /// </summary>
        /// <param name="configContent">New content for config.</param>
        public void SetConfigContent(string configContent)
        {
            SetConfigContent(PrimaryConfigurationFileName, configContent);
        }

        /// <summary>
        /// Sets config content.
        /// </summary>
        /// <param name="configContent">New content for config.</param>
        /// <param name="configFileName">Name of the config file</param>
        public void SetConfigContent(string configFileName, string configContent)
        {
            m_configContent = configContent;
        }

        /// <summary>
        /// Instructs the config writer to use the legacy file name for the primary configuration file
        /// </summary>
        public void UseLegacyConfigExtension()
        {
            PrimaryConfigurationFileName = Names.ConfigDsc;
        }

        /// <summary>
        /// Instructs the config writer to use the modern file name for the primary configuration file
        /// </summary>
        public void UseModernConfigExtension()
        {
            PrimaryConfigurationFileName = Names.ConfigBc;
        }

        /// <summary>
        /// Adds build specification.
        /// </summary>
        public DsBuildSpecWriter AddBuildSpec(string relativePath, string spec)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(relativePath));
            Contract.Requires(spec != null);

            var specWriter = new DsBuildSpecWriter(relativePath, spec);
            m_buildSpecWriters.Add(specWriter);

            return specWriter;
        }

        /// <summary>
        /// Adds package.
        /// </summary>
        public DsBuildSpecWriter AddPackage(string name, string relativeDir, string spec, bool implicitReferenceSemantics = false)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(relativeDir));
            Contract.Requires(spec != null);

            var specWriter = new DsBuildSpecWriter(name, Path.Combine(relativeDir, Names.PackageDsc), spec, implicitReferenceSemantics);
            m_buildSpecWriters.Add(specWriter);

            return specWriter;
        }

        /// <summary>
        /// Adds a source resolver.
        /// </summary>
        public SourceResolverTestObject AddSourceResolver(string root = null)
        {
            var resolver = new SourceResolverTestObject(root);
            m_resolverTestObjects.Add(resolver);

            return resolver;
        }

        /// <summary>
        /// Adds a named qualifier.
        /// </summary>
        public void AddNamedQualifier(string name, Dictionary<string, string> qualifier)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(qualifier != null);
            Contract.RequiresForAll(qualifier, pair => pair.Key != null && pair.Value != null);

            if (m_namedQualifiers == null)
            {
                m_namedQualifiers = new Dictionary<string, Dictionary<string, string>>();
            }

            m_namedQualifiers[name] = qualifier;
        }

        /// <summary>
        /// Returns null since default resolver shouldn't be used explicitly anyway.
        /// </summary>
        /// <remarks>
        /// TODO: remove this method
        /// </remarks>
        public DefaultSourceResolverTestObject AddDefaultSourceResolver()
        {
            return null;
        }

        /// <summary>
        /// Sets the value of always using default resolver.
        /// </summary>
        public void DisableDefaultSourceResolver(bool value)
        {
            m_disableDefaultSourceResolver = value;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return GetConfigurationObjectWithQualifiers(m_namedQualifiers, m_resolverTestObjects, m_disableDefaultSourceResolver);
        }

        /// <nodoc />
        public static string GetConfigurationObjectWithQualifiers(
            Dictionary<string, Dictionary<string, string>> namedQualifiers,
            List<ResolverTestObject> resolverTestObjects,
            bool disableDefaultSourceResolver)
        {
            var builder = new StringBuilder();
            builder.AppendLine("config({");

            if (namedQualifiers != null)
            {
                builder.AppendLine("    qualifiers: {");

                if (namedQualifiers != null)
                {
                    builder.AppendLine("        namedQualifiers: {");
                    foreach (var map in namedQualifiers)
                    {
                        builder.Append("            " + "\"" + map.Key + "\": {");

                        foreach (var kvp in map.Value)
                        {
                            builder.Append("\"" + kvp.Key + "\": " + "\"" + kvp.Value + "\",");
                        }

                        builder.AppendLine("},");
                    }

                    builder.AppendLine("        },");
                }

                builder.AppendLine("    },");
            }

            if (resolverTestObjects?.Count > 0)
            {
                builder.Append("    resolvers: [");

                for (int i = 0; i < resolverTestObjects.Count; ++i)
                {
                    builder.Append(resolverTestObjects[i]);
                    if (i != resolverTestObjects.Count - 1)
                    {
                        builder.Append(", ");
                    }
                }

                builder.AppendLine("],");
            }

            if (disableDefaultSourceResolver)
            {
                builder.Append("    disableDefaultSourceResolver: true");
            }

            builder.Append("});");

            return builder.ToString();
        }

        /// <summary>
        /// Gets all files known by this config file writer.
        /// </summary>
        /// <returns>All files known by this config file writer.</returns>
        public IEnumerable<Tuple<string, string>> GetAllFiles()
        {
            var list = new List<Tuple<string, string>>
                       {
                           Tuple.Create(PrimaryConfigurationFileName,
                               m_configContent ?? ToString())
                       };

            list.AddRange(m_buildSpecWriters.Select(writer => Tuple.Create(writer.RelativePath, writer.Spec)));

            return list;
        }
    }
}
