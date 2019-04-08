// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.FrontEnd.Script.Constants;
using Tool.MimicGenerator.LanguageWriters;

namespace Tool.MimicGenerator
{
    public enum Language : byte
    {
        None = 0,
        DScript,
    }

    public sealed class LanguageProvider
    {
        private readonly Language m_language;

        public LanguageProvider(Language language)
        {
            m_language = language;
        }

        public ConfigWriter CreateConfigWriter(string absolutePath)
        {
            switch (m_language)
            {
                case Language.DScript:
                    return new DScriptConfigWriter(absolutePath);
                default:
                    throw new NotImplementedException(m_language.ToString());
            }
        }

        public ModuleWriter CreateModuleWriter(string absolutePath, string identity, IEnumerable<string> logicAssemblies)
        {
            switch (m_language)
            {
                case Language.DScript:
                    return new DScriptPackageWriter(absolutePath, identity, logicAssemblies);
                default:
                    throw new NotImplementedException(m_language.ToString());
            }
        }

        public SpecWriter CreateSpecWriter(string absolutePath)
        {
            switch (m_language)
            {
                case Language.DScript:
                    if (!ExtensionUtilities.IsLegacyFileExtension(Path.GetExtension(absolutePath)))
                    {
                        absolutePath = Path.ChangeExtension(absolutePath, Names.DotDscExtension);
                    }

                    return new DScriptSpecWriter(absolutePath);
                default:
                    throw new NotImplementedException(m_language.ToString());
            }
        }
    }
}
