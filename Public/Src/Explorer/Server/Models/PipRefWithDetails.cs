// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Explorer.Server.Models
{
    public class PipRefWithDetails : PipRef
    {
        public PipRefWithDetails(PipExecutionContext context, Pip pip)
        {
            SetPipRefDetails(context, pip, this);
        }

        public static void SetPipRefDetails(PipExecutionContext context, Pip pip, PipRefWithDetails pipRef)
        {
            pipRef.Id = (int)pip.PipId.Value;
            pipRef.Kind = ConvertTypeToKind(pip.PipType);

            pipRef.SemiStableHash = pip.FormattedSemiStableHash;
            pipRef.ShortDescription = pip.GetShortDescription(context);

            if (pip.Provenance != null)
            {
                pipRef.Module = new ModuleRef()
                {
                    Id = pip.Provenance.ModuleId.Value.Value,
                    Name = pip.Provenance.ModuleName.ToString(context.StringTable)
                };
                pipRef.SpecFile = new SpecFileRef(context, pip.Provenance.Token.Path);
                pipRef.Value = new ValueRef()
                {
                    Id = 0,
                    Symbol = pip.Provenance.OutputValueSymbol.ToString(context.SymbolTable)
                };
                pipRef.Qualifier = new QualifierRef()
                {
                    Id = pip.Provenance.QualifierId.Id,
                    ShortName = context.QualifierTable.GetFriendlyUserString(pip.Provenance.QualifierId),
                    LongName = context.QualifierTable.GetCanonicalDisplayString(pip.Provenance.QualifierId),
                };
            }

            if (pip.PipType == PipType.Process)
            {
                pipRef.Tool = new ToolRef()
                {
                    Id = ((Process)pip).Executable.Path.Value.Value,
                    Name = ((Process)pip).Executable.Path.GetName(context.PathTable).ToString(context.StringTable),
                };
            }
        }

        private static string ConvertTypeToKind(PipType pipType)
        {
            switch (pipType)
            {
                case PipType.WriteFile:
                    return "writeFile";
                case PipType.CopyFile:
                    return "copyFile";
                case PipType.Process:
                    return "process";
                case PipType.SealDirectory:
                    return "sealDirectory";
                case PipType.Ipc:
                    return "ipc";
                case PipType.Value:
                    return "value";
                case PipType.SpecFile:
                    return "specFile";
                case PipType.Module:
                    return "module";
                case PipType.HashSourceFile:
                    return "hashFile";
                default:
                    return "unknownPip";
            }
        }

        public ModuleRef Module { get; set; }
        public SpecFileRef SpecFile { get; set; }
        public ValueRef Value { get; set; }
        public ToolRef Tool { get; set; }
        public QualifierRef Qualifier { get; set; }
    }
}
