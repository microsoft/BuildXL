// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Helpers for writing textual format of incremental scheduling state.
    /// </summary>
    internal static class IncrementalSchedulingStateWriteTextHelpers
    {
        /// <summary>
        /// Writes with a banner.
        /// </summary>
        public static void WriteTextEntryWithBanner(TextWriter writer, string banner, Action<TextWriter> writeBody)
        {
            Contract.Requires(writer != null);
            Contract.Requires(banner != null);
            Contract.Requires(writeBody != null);

            writer.WriteLine(I($">> ============================ {banner} ============================ <<"));
            writeBody(writer);
            writer.WriteLine(string.Empty);
        }

        /// <summary>
        /// Writes with a header.
        /// </summary>
        public static void WriteTextEntryWithHeader(TextWriter writer, string header, Action<TextWriter> writeBody)
        {
            Contract.Requires(writer != null);
            Contract.Requires(header != null);
            Contract.Requires(writeBody != null);

            header = "*** " + header + " ***";
            writer.WriteLine(header);
            writer.WriteLine(new string('=', header.Length));
            writeBody(writer);
            writer.WriteLine(string.Empty);
        }

        /// <summary>
        /// Writes a list of <see cref="NodeId"/>.
        /// </summary>
        public static void WriteTextNodes(TextWriter writer, PipGraph pipGraph, IEnumerable<NodeId> nodes, bool printHashSourceFile = true)
        {
            Contract.Requires(writer != null);
            Contract.Requires(pipGraph != null);
            Contract.Requires(nodes != null);

            WriteTextList(
                writer,
                nodes,
                node =>
                {
                    var pip = pipGraph.GetPipFromUInt32(node.Value);

                    if (pip.PipType.IsMetaPip())
                    {
                        return null;
                    }

                    if (pip.PipType == PipType.HashSourceFile && !printHashSourceFile)
                    {
                        return null;
                    }

                    return pip.PipType == PipType.HashSourceFile
                        ? ((HashSourceFile)pip).Artifact.Path.ToString(pipGraph.Context.PathTable)
                        : pip.GetDescription(pipGraph.Context);
                });
        }

        /// <summary>
        /// Writes <see cref="ConcurrentBigMap{TKey, TValue}"/>.
        /// </summary>
        public static void WriteTextMap<K, V>(TextWriter writer, ConcurrentBigMap<K, V> map, Func<K, string> keyToString, Func<V, string> valueToString)
        {
            Contract.Requires(writer != null);
            Contract.Requires(map != null);
            Contract.Requires(keyToString != null);
            Contract.Requires(valueToString != null);

            WriteTextList(writer, map, kvp => I($"{keyToString(kvp.Key)}: {valueToString(kvp.Value)}"));
        }

        /// <summary>
        /// Writes <see cref="ConcurrentBigSet{TValue}"/>.
        /// </summary>
        public static void WriteTextSet<V>(TextWriter writer, ConcurrentBigSet<V> set, Func<V, string> valueToString)
        {
            Contract.Requires(writer != null);
            Contract.Requires(set != null);
            Contract.Requires(valueToString != null);

            WriteTextList(writer, set.UnsafeGetList(), v => valueToString(v));
        }

        /// <summary>
        /// Writes a list of <see cref="AbsolutePath"/>.
        /// </summary>
        public static void WriteTextPaths(TextWriter writer, PathTable pathTable, IEnumerable<AbsolutePath> paths)
        {
            Contract.Requires(writer != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(paths != null);

            WriteTextList(writer, paths, p => p.ToString(pathTable));
        }

        /// <summary>
        /// Writes a list of values.
        /// </summary>
        public static void WriteTextList<T>(TextWriter writer, IEnumerable<T> list, Func<T, string> valueToString)
        {
            Contract.Requires(writer != null);
            Contract.Requires(list != null);
            Contract.Requires(valueToString != null);

            foreach (var l in list)
            {
                string s = valueToString(l);

                if (s != null)
                {
                    writer.WriteLine(s);
                }
            }
        }

        /// <summary>
        /// Writes pip fingerprint, given its stable id.
        /// </summary>
        public static void WritePipFingerprint(TextWriter writer, PipOrigins pipOrigins, PipStableId pipStableId)
        {
            Contract.Requires(writer != null);
            Contract.Requires(pipOrigins != null);

            writer.Write(GetPipIdText(pipOrigins, pipStableId));
        }

        /// <summary>
        /// Gets text format of pip stable id.
        /// </summary>
        public static string GetPipIdText(PipOrigins pipOrigins, PipStableId pipStableId)
        {
            Contract.Requires(pipOrigins != null);
            return pipOrigins.TryGetFingerprint(pipStableId, out ContentFingerprint fingerprint) ? I($"FP:{fingerprint.ToString()}") : I($"PIP_ID:{pipStableId}");
        }
    }
}
