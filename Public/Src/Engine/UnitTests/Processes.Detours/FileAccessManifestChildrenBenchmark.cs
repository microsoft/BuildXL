// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using BuildXL.Native.Processes;
using BuildXL.Utilities.Core;

namespace Test.BuildXL.Processes.Detours
{
    /// <summary>
    /// Synthetic benchmark for the high-cardinality, many-small-child-dictionary FAM shape observed in worker dumps.
    /// Set BXL_RUN_FAM_CHILDREN_BENCHMARK=1 to run it; the normal test run leaves it inert.
    /// </summary>
    internal static class FileAccessManifestChildrenBenchmark
    {
        private const int ProjectCount = 50_000;
        private const int FilesPerProject = 8;
        private const int LookupIterations = 1_000_000;

        public static void Run()
        {
            var pathTable = new PathTable();
            StringId[][] paths = CreatePaths(pathTable);
            StringId[][] caseVariants = CreateCaseVariants(pathTable, paths);
            StringId[][] misses = CreateMisses(pathTable, paths);

            var proposedFirst = Environment.GetEnvironmentVariable("BXL_FAM_BENCHMARK_ORDER") == "proposed-first";
            Measurement<BaselineNode> baseline;
            Measurement<ProposedNode> proposed;
            if (proposedFirst)
            {
                proposed = MeasureConstruction(() => ProposedNode.Create(pathTable.StringTable, paths));
                baseline = MeasureConstruction(() => BaselineNode.Create(pathTable.StringTable, paths));
            }
            else
            {
                baseline = MeasureConstruction(() => BaselineNode.Create(pathTable.StringTable, paths));
                proposed = MeasureConstruction(() => ProposedNode.Create(pathTable.StringTable, paths));
            }

            Console.WriteLine($"FAM child benchmark ({(proposedFirst ? "proposed-first" : "baseline-first")}): {paths.Length:N0} leaves, {ProjectCount:N0} project dictionaries, {FilesPerProject} files/project.");
            Print("baseline construction", baseline);
            Print("cached-normalized StringId construction", proposed);

            MeasureLookups("baseline exact hot", () => baseline.Value.Find(paths[0]), paths.Length, index => baseline.Value.Find(paths[index]));
            MeasureLookups("cached-normalized StringId exact hot", () => proposed.Value.Find(paths[0]), paths.Length, index => proposed.Value.Find(paths[index]));
            MeasureLookups("baseline case variants", () => baseline.Value.Find(caseVariants[0]), paths.Length, index => baseline.Value.Find(caseVariants[index]));
            MeasureLookups("cached-normalized StringId case variants", () => proposed.Value.Find(caseVariants[0]), paths.Length, index => proposed.Value.Find(caseVariants[index]));
            MeasureLookups("baseline misses", () => baseline.Value.Find(misses[0]), paths.Length, index => baseline.Value.Find(misses[index]));
            MeasureLookups("cached-normalized StringId misses", () => proposed.Value.Find(misses[0]), paths.Length, index => proposed.Value.Find(misses[index]));

            GC.KeepAlive(baseline.Value);
            GC.KeepAlive(proposed.Value);
        }

        private static StringId[][] CreatePaths(PathTable pathTable)
        {
            var paths = new StringId[ProjectCount * FilesPerProject][];
            var index = 0;
            for (int project = 0; project < ProjectCount; project++)
            {
                for (int file = 0; file < FilesPerProject; file++)
                {
                    var path = AbsolutePath.Create(
                        pathTable,
                        $@"C:\src\repo\shard-{project % 64:D2}\project-{project:D5}\obj\release\file-{project:D5}-{file:D2}.dll");
                    paths[index++] = GetFragments(pathTable, path);
                }
            }

            return paths;
        }

        private static StringId[][] CreateCaseVariants(PathTable pathTable, StringId[][] paths)
        {
            var variants = new StringId[paths.Length][];
            for (int i = 0; i < paths.Length; i++)
            {
                variants[i] = new StringId[paths[i].Length];
                for (int part = 0; part < paths[i].Length; part++)
                {
                    variants[i][part] = pathTable.StringTable.AddString(pathTable.StringTable.GetString(paths[i][part]).ToUpperInvariant());
                }
            }

            return variants;
        }

        private static StringId[][] CreateMisses(PathTable pathTable, StringId[][] paths)
        {
            var misses = new StringId[paths.Length][];
            for (int i = 0; i < paths.Length; i++)
            {
                misses[i] = (StringId[])paths[i].Clone();
                misses[i][misses[i].Length - 1] = pathTable.StringTable.AddString($"missing-{i:D6}.dll");
            }

            return misses;
        }

        private static StringId[] GetFragments(PathTable pathTable, AbsolutePath path)
        {
            var fragments = new Stack<StringId>();
            while (path.IsValid)
            {
                fragments.Push(path.GetName(pathTable).StringId);
                path = path.GetParent(pathTable);
            }

            return fragments.ToArray();
        }

        private static Measurement<T> MeasureConstruction<T>(Func<T> create)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
            var allocatedBefore = GetAllocatedBytes();
            var stopwatch = Stopwatch.StartNew();
            T value = create();
            stopwatch.Stop();
            var allocated = GetAllocatedBytes() - allocatedBefore;
            var retained = GC.GetTotalMemory(forceFullCollection: true) - retainedBefore;

            return new Measurement<T>(value, stopwatch.Elapsed, allocated, retained);
        }

        private static void MeasureLookups(string name, Func<bool> hotLookup, int pathCount, Func<int, bool> lookup)
        {
            var allocatedBefore = GetAllocatedBytes();
            var stopwatch = Stopwatch.StartNew();
            var found = 0;
            for (int i = 0; i < LookupIterations; i++)
            {
                if (hotLookup())
                {
                    found++;
                }
            }

            for (int i = 0; i < LookupIterations; i++)
            {
                if (lookup((int)(((long)i * 7919) % pathCount)))
                {
                    found++;
                }
            }

            stopwatch.Stop();
            var allocated = GetAllocatedBytes() - allocatedBefore;
            Console.WriteLine($"{name}: {2 * LookupIterations:N0} lookups in {stopwatch.Elapsed.TotalMilliseconds:F0} ms ({allocated:N0} B allocated, found {found:N0}).");
        }

        private static long GetAllocatedBytes()
        {
#if NET8_0_OR_GREATER
            return GC.GetAllocatedBytesForCurrentThread();
#else
            return 0;
#endif
        }

        private static void Print<T>(string name, Measurement<T> measurement)
        {
            Console.WriteLine($"{name}: {measurement.Elapsed.TotalMilliseconds:F0} ms, {measurement.AllocatedBytes:N0} B allocated, {measurement.RetainedBytes:N0} B retained.");
        }

        private readonly struct Measurement<T>
        {
            public readonly T Value;
            public readonly TimeSpan Elapsed;
            public readonly long AllocatedBytes;
            public readonly long RetainedBytes;

            public Measurement(T value, TimeSpan elapsed, long allocatedBytes, long retainedBytes)
            {
                Value = value;
                Elapsed = elapsed;
                AllocatedBytes = allocatedBytes;
                RetainedBytes = retainedBytes;
            }
        }

        private sealed class BaselineNode
        {
            private readonly StringTable m_stringTable;
            private readonly Dictionary<StringId, NormalizedFragment> m_normalizedFragments;
            private Dictionary<NormalizedFragment, BaselineNode> m_children;

            private BaselineNode(StringTable stringTable, Dictionary<StringId, NormalizedFragment> normalizedFragments)
            {
                m_stringTable = stringTable;
                m_normalizedFragments = normalizedFragments;
            }

            public static BaselineNode Create(StringTable stringTable, StringId[][] paths)
            {
                var root = new BaselineNode(stringTable, new Dictionary<StringId, NormalizedFragment>());
                foreach (StringId[] path in paths)
                {
                    var node = root;
                    foreach (StringId fragment in path)
                    {
                        node = node.GetOrAdd(fragment);
                    }
                }

                return root;
            }

            public bool Find(StringId[] path)
            {
                var node = this;
                foreach (StringId fragment in path)
                {
                    if (!node.TryGet(fragment, out node))
                    {
                        return false;
                    }
                }

                return true;
            }

            private BaselineNode GetOrAdd(StringId fragment)
            {
                if (TryGet(fragment, out var child))
                {
                    return child;
                }

                m_children ??= new Dictionary<NormalizedFragment, BaselineNode>();
                var normalized = GetNormalized(fragment);
                child = new BaselineNode(m_stringTable, m_normalizedFragments);
                m_children.Add(normalized, child);
                return child;
            }

            private bool TryGet(StringId fragment, out BaselineNode child)
            {
                if (m_children is null)
                {
                    child = null;
                    return false;
                }

                return m_children.TryGetValue(GetNormalized(fragment), out child);
            }

            private NormalizedFragment GetNormalized(StringId fragment)
            {
                if (!m_normalizedFragments.TryGetValue(fragment, out var normalized))
                {
                    normalized = new NormalizedFragment(m_stringTable, fragment);
                    m_normalizedFragments.Add(fragment, normalized);
                }

                return normalized;
            }
        }

        private sealed class ProposedNode
        {
            private readonly StringTable m_stringTable;
            private readonly Dictionary<StringId, NormalizedFragment> m_normalizedFragments;
            private readonly NativeFragmentComparer m_comparer;
            private Dictionary<StringId, ProposedNode> m_children;

            private ProposedNode(
                StringTable stringTable,
                Dictionary<StringId, NormalizedFragment> normalizedFragments,
                NativeFragmentComparer comparer)
            {
                m_stringTable = stringTable;
                m_normalizedFragments = normalizedFragments;
                m_comparer = comparer;
            }

            public static ProposedNode Create(StringTable stringTable, StringId[][] paths)
            {
                var normalizedFragments = new Dictionary<StringId, NormalizedFragment>();
                var comparer = new NativeFragmentComparer(normalizedFragments);
                var root = new ProposedNode(stringTable, normalizedFragments, comparer);
                foreach (StringId[] path in paths)
                {
                    var node = root;
                    foreach (StringId fragment in path)
                    {
                        node = node.GetOrAdd(fragment);
                    }
                }

                return root;
            }

            public bool Find(StringId[] path)
            {
                var node = this;
                foreach (StringId fragment in path)
                {
                    if (!node.TryGet(fragment, out node))
                    {
                        return false;
                    }
                }

                return true;
            }

            private ProposedNode GetOrAdd(StringId fragment)
            {
                if (TryGet(fragment, out var child))
                {
                    return child;
                }

                m_children ??= new Dictionary<StringId, ProposedNode>(m_comparer);
                child = new ProposedNode(m_stringTable, m_normalizedFragments, m_comparer);
                m_children.Add(fragment, child);
                return child;
            }

            private bool TryGet(StringId fragment, out ProposedNode child)
            {
                GetNormalized(fragment);
                if (m_children is null)
                {
                    child = null;
                    return false;
                }

                return m_children.TryGetValue(fragment, out child);
            }

            private NormalizedFragment GetNormalized(StringId fragment)
            {
                if (!m_normalizedFragments.TryGetValue(fragment, out var normalized))
                {
                    normalized = new NormalizedFragment(m_stringTable, fragment);
                    m_normalizedFragments.Add(fragment, normalized);
                }

                return normalized;
            }

            private sealed class NativeFragmentComparer : IEqualityComparer<StringId>
            {
                private readonly Dictionary<StringId, NormalizedFragment> m_normalizedFragments;

                public NativeFragmentComparer(Dictionary<StringId, NormalizedFragment> normalizedFragments)
                {
                    m_normalizedFragments = normalizedFragments;
                }

                public bool Equals(StringId x, StringId y) =>
                    x == y || m_normalizedFragments[x].Equals(m_normalizedFragments[y]);

                public int GetHashCode(StringId obj) => m_normalizedFragments[obj].GetHashCode();
            }
        }

        private readonly struct NormalizedFragment : IEquatable<NormalizedFragment>
        {
            private readonly byte[] m_bytes;
            private readonly int m_hashCode;

            public NormalizedFragment(StringTable stringTable, StringId fragment)
            {
                m_hashCode = ProcessUtilities.NormalizeAndHashPath(stringTable.GetString(fragment), out m_bytes);
            }

            public bool Equals(NormalizedFragment other)
            {
                return m_hashCode == other.m_hashCode && ProcessUtilities.AreBuffersEqual(m_bytes, other.m_bytes);
            }

            public override bool Equals(object obj) => obj is NormalizedFragment other && Equals(other);

            public override int GetHashCode() => m_hashCode;
        }
    }
}
