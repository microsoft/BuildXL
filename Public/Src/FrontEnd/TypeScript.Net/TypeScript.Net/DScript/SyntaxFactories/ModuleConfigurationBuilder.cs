// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using TypeScript.Net.Types;

namespace TypeScript.Net.DScript
{
    /// <summary>
    /// A builder object for constructing <see cref="ISourceFile"/> instance with module configuration.
    /// </summary>
    public sealed class ModuleConfigurationBuilder
    {
        private string m_packageName;
        private string m_version;
        private bool m_implicitNameResolution = true;

        /// <nodoc />
        public ModuleConfigurationBuilder Name(string packageName)
        {
            Contract.Requires(!string.IsNullOrEmpty(packageName));

            m_packageName = packageName;
            return this;
        }

        /// <nodoc />
        public ModuleConfigurationBuilder Version(string version)
        {
            Contract.Requires(!string.IsNullOrEmpty(version));

            m_version = version;
            return this;
        }

        /// <nodoc />
        public ModuleConfigurationBuilder NameResolution(bool implicitNameResolution)
        {
            m_implicitNameResolution = implicitNameResolution;
            return this;
        }

        /// <nodoc />
        public ISourceFile Build()
        {
            if (string.IsNullOrEmpty(m_packageName))
            {
                throw new InvalidOperationException("Module name should be provided. Did you forget to call Name() method?");
            }

            if (string.IsNullOrEmpty(m_version))
            {
                throw new InvalidOperationException("Module name should be provided. Did you forget to call Name() method?");
            }

            string nameResolution = m_implicitNameResolution
                ? "NameResolutionSemantics.implicitProjectReferences"
                : "NameResolutionSemantics.explicitProjectReferences";

            return new SourceFile(
                new ExpressionStatement(
                    new CallExpression(
                        new Identifier("module"),
                        new ObjectLiteralExpression(
                            new PropertyAssignment("name", new LiteralExpression(m_packageName)),
                            new PropertyAssignment("version", new LiteralExpression(m_version)),
                            new PropertyAssignment("nameResolutionSemantics", new LiteralExpression(nameResolution, LiteralExpressionKind.None))))));
        }
    }
}
