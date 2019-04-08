// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Values;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Ambient definition for Transformer.SealSourceDirectoryOption.
    /// </summary>
    public sealed class AmbientSealSourceDirectoryOption : AmbientEnumDefinitionBase
    {
        /// <nodoc />
        public AmbientSealSourceDirectoryOption(PrimitiveTypes knownTypes)
            : base(knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            var members = new List<NamespaceFunctionDefinition>();
            foreach (SealSourceDirectoryOption kind in Enum.GetValues(typeof(SealSourceDirectoryOption)))
            {
                var memberName = ToCamelCase(kind.ToString());
                members.Add(Function(memberName, ModuleBinding.CreateEnum(Symbol(memberName), (int)kind)));
            }

            return new AmbientNamespaceDefinition(
                "Transformer." + typeof(SealSourceDirectoryOption).Name,
                members.ToArray());
        }
    }

    /// <summary>
    /// Options for Source Sealed directories
    /// </summary>
    // These names are inspired by System.IO.Directory.EnumerateFiles
    public enum SealSourceDirectoryOption
    {
        /// <summary>
        /// Only allows files in the top directory only
        /// </summary>
        TopDirectoryOnly = 0,

        /// <summary>
        /// Allows all directories
        /// </summary>
        AllDirectories,
    }
}
