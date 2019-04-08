// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BuildXL.Explorer.Server.Models
{
    /// <summary>
    /// Serializable PipData object. The structure uses short member names for fast serialization/deserialization
    /// </summary>
    public class PipData
    {
        public PipData(PipExecutionContext context, Pips.Operations.PipData pipData)
        {
            S = pipData.FragmentSeparator.ToString(context.StringTable);

            switch (pipData.FragmentEscaping)
            {
                case PipDataFragmentEscaping.NoEscaping:
                    E = FragmentEscaping.N;
                    break;
                case PipDataFragmentEscaping.CRuntimeArgumentRules:
                    E = FragmentEscaping.C;
                    break;
                default:
                    throw new ExplorerException($"Unsupported fragment escaping: {pipData.FragmentEscaping.ToString()}");
            }

            foreach (var fragment in pipData)
            {
                switch (fragment.FragmentType)
                {
                    case PipFragmentType.StringLiteral:
                        I.Add(new StringPipDataEntry(fragment.GetStringIdValue().ToString(context.StringTable)));
                        break;
                    case PipFragmentType.AbsolutePath:
                        I.Add(new PathPipDataEntry(context, fragment.GetPathValue()));
                        break;
                    case PipFragmentType.NestedFragment:
                        I.Add(new NestedPipDataEntry(new PipData(context, fragment.GetNestedFragmentValue())));
                        break;
                    case PipFragmentType.VsoHash:
                        I.Add(new StringPipDataEntry("<VSOHASH>"));
                        break;
                    case PipFragmentType.IpcMoniker:
                        I.Add(new StringPipDataEntry("<IPCMONIKER>"));
                        break;
                    default:
                        throw new ExplorerException($"Unsupported fragment type: {fragment.FragmentType.ToString()}");
                }
            }
        }

        /// <nodoc/>
        public static PipData EmptyPipData = new PipData();

        private PipData()
        {
            S = string.Empty;
            E = FragmentEscaping.C;
        }

        /// <summary>
        /// Entries
        /// </summary>
        public List<PipDataEntry> I { get; } = new List<PipDataEntry>();

        /// <summary>
        /// Separator: 
        /// </summary>
        public string S { get; }

        /// <summary>
        /// Escaping. Short name since this is dense data!
        /// </summary>
        public FragmentEscaping E { get; set; }
    }

    public abstract class PipDataEntry
    {
    }

    public class StringPipDataEntry : PipDataEntry
    {
        public StringPipDataEntry(string s)
        {
            S = s;
        }

        public string S { get; }
    }

    public class PathPipDataEntry : PipDataEntry
    {
        public PathPipDataEntry(PipExecutionContext context, AbsolutePath p)
        {
            P = new PathRef(context, p);
        }

        public PathRef P { get; set; }
    }

    public class NestedPipDataEntry : PipDataEntry
    {
        public NestedPipDataEntry(PipData n)
        {
            N = n;
        }

        public PipData N { get; set; }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum FragmentEscaping
    {
        N, // No encoding
        C, // CRuntime encoding (Short value to not have too lage json)
    }
}
