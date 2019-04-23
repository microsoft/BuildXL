// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition that provides basic json functionality.
    /// </summary>
    public sealed partial class AmbientXml : AmbientDefinitionBase
    {
        /// <nodoc />
        public AmbientXml(PrimitiveTypes knownTypes)
            : base("Xml", knownTypes)
        {
        }

        /// <nodoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                AmbientHack.GetName("Xml"),
                new[]
                {
                    Function("read", Read, ReadSignature),
                    Function("write", Write, WriteSignature),
                });
        }

        private class XmlContext
        {
            public StringTable StringTable { get; }

            public SymbolAtom KindField { get; }
            public SymbolAtom NameField { get; }
            public SymbolAtom PrefixField { get; }
            public SymbolAtom LocalField { get; }
            public SymbolAtom NamespaceField { get; }
            public SymbolAtom NodesField { get; }
            public SymbolAtom AttributesField { get; }
            public SymbolAtom ValueField { get; }
            public SymbolAtom TextField { get; }


            public const string DocumentKind = "document";
            public const string ElementKind = "element";
            public const string AttributeKind = "attribute";
            public const string TextKind = "text";
            public const string CDataKind = "cdata";
            public const string CommentKind = "comment";
            public const string ProcessingInstructionKind = "processing-instruction";

            public static readonly string[] AllKinds = new[]
            {
                DocumentKind,
                ElementKind,
                AttributeKind,
                TextKind,
                CDataKind,
                CommentKind,
                ProcessingInstructionKind,
            };

            public XmlContext(StringTable stringTable)
            {
                StringTable = stringTable;
                KindField = SymbolAtom.Create(stringTable, "kind");
                NameField = SymbolAtom.Create(stringTable, "name");
                PrefixField = SymbolAtom.Create(stringTable, "prefix");
                LocalField = SymbolAtom.Create(stringTable, "local");
                NamespaceField = SymbolAtom.Create(stringTable, "namespace");
                NodesField = SymbolAtom.Create(stringTable, "nodes");
                AttributesField = SymbolAtom.Create(stringTable, "attributes");
                ValueField = SymbolAtom.Create(stringTable, "value");
                TextField = SymbolAtom.Create(stringTable, "text");
            }

        }
    }
}
