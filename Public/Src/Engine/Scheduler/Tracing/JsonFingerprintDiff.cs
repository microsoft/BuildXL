// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Engine.Cache.Serialization;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;
using Newtonsoft.Json.Linq;
using static BuildXL.Scheduler.Tracing.FingerprintDiff;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Class for diff-ing weak and strong fingerprints, and present the result as Json object.
    /// </summary>
    internal static class JsonFingerprintDiff
    {
        public const string FieldNew = "New";
        public const string FieldOld = "Old";
        public const string FieldChanged = "Changed";
        public const string FieldAdded = "Added";
        public const string FieldRemoved = "Removed";

        #region Diff-ing

        /// <summary>
        /// Diffs weak fingerprints.
        /// </summary>
        /// <param name="weakFingerprint">Weak fingerprint.</param>
        /// <param name="weakFingerprintTree">Weak fingerprint tree.</param>
        /// <param name="otherWeakFingerprint">Other weak fingerprint.</param>
        /// <param name="otherWeakFingerprintTree">Other weak fingerprint tree.</param>
        /// <returns></returns>
        public static JObject DiffWeakFingerprints(
            string weakFingerprint,
            JsonNode weakFingerprintTree,
            string otherWeakFingerprint,
            JsonNode otherWeakFingerprintTree)
        {
            JObject result = new JObject();

            if (weakFingerprint == otherWeakFingerprint)
            {
                return result;
            }

            // {
            //   WeakFingerprint: { Old: old_weak_fingerprint, New: new_weak_fingerprint }
            // }
            AddPropertyIfNotNull(result, RenderSingleValueDiff("WeakFingerprint", weakFingerprint, otherWeakFingerprint));

            using (var weakFingerprintDataPool = JsonNodeMapPool.GetInstance())
            using (var otherWeakFingerprintDataPool = JsonNodeMapPool.GetInstance())
            {
                var weakFingerprintData = weakFingerprintDataPool.Instance;
                var otherWeakFingerprintData = otherWeakFingerprintDataPool.Instance;

                JsonTree.VisitTree(weakFingerprintTree, wfNode => weakFingerprintData[wfNode.Name] = wfNode, recurse: false);
                JsonTree.VisitTree(otherWeakFingerprintTree, wfNode => otherWeakFingerprintData[wfNode.Name] = wfNode, recurse: false);

                var fields = new HashSet<string>(weakFingerprintData.Keys.Concat(otherWeakFingerprintData.Keys));

                foreach (var field in fields)
                {
                    bool getFieldNode = weakFingerprintData.TryGetValue(field, out JsonNode fieldNode);
                    bool getOtherFieldNode = otherWeakFingerprintData.TryGetValue(field, out JsonNode otherFieldNode);

                    if (getFieldNode != getOtherFieldNode)
                    {
                        string fieldValue = getFieldNode
                            ? (fieldNode.Values != null && fieldNode.Values.Count == 1
                                ? fieldNode.Values[0]
                                : CacheMissAnalysisUtilities.RepeatedStrings.ExistentValue)
                            : CacheMissAnalysisUtilities.RepeatedStrings.UnspecifiedValue;
                        string otherFieldValue = getOtherFieldNode
                            ? (otherFieldNode.Values != null && otherFieldNode.Values.Count == 1
                                ? otherFieldNode.Values[0]
                                : CacheMissAnalysisUtilities.RepeatedStrings.ExistentValue)
                            : CacheMissAnalysisUtilities.RepeatedStrings.UnspecifiedValue;

                        AddPropertyIfNotNull(result, RenderSingleValueDiff(field, fieldValue, otherFieldValue));
                    }
                    else if (getFieldNode && getOtherFieldNode)
                    {
                        Contract.Assert(fieldNode != null);
                        Contract.Assert(otherFieldNode != null);

                        AddPropertyIfNotNull(result, DiffWeakFingerprintField(fieldNode, otherFieldNode));
                    }
                }
            }

            Contract.Assert(result.Count > 0);

            return result;

        }

        /// <summary>
        /// Diffs strong fingerprints.
        /// </summary>
        /// <param name="strongFingerprint">Strong fingerprint.</param>
        /// <param name="pathSetTree">Pathset tree.</param>
        /// <param name="strongFingerprintInputTree">Strong fingerprint input tree.</param>
        /// <param name="otherStrongFingerprint">Other strong fingerprint.</param>
        /// <param name="otherPathSetTree">Other pathset tree.</param>
        /// <param name="otherStrongFingerprintInputTree">Other strong fingerprint input tree.</param>
        /// <param name="getDirectoryMembership">Delegate for getting directory membership.</param>
        /// <param name="getOtherDirectoryMembership">Delegate for getting other directory membership.</param>
        /// <returns></returns>
        public static JObject DiffStrongFingerprints(
            string strongFingerprint,
            JsonNode pathSetTree,
            JsonNode strongFingerprintInputTree,
            string otherStrongFingerprint,
            JsonNode otherPathSetTree,
            JsonNode otherStrongFingerprintInputTree,
            Func<string, IReadOnlyList<string>> getDirectoryMembership,
            Func<string, IReadOnlyList<string>> getOtherDirectoryMembership)
        {
            JObject result = new JObject();

            if (strongFingerprint == otherStrongFingerprint)
            {
                return result;
            }

            // {
            //   StrongFingerprint: { Old: old_strong_fingerprint, New: new_strong_fingerprint }
            // }
            AddPropertyIfNotNull(result, RenderSingleValueDiff("StrongFingerprint", strongFingerprint, otherStrongFingerprint));

            AddPropertyIfNotNull(
                result,
                DiffObservedPaths(
                    pathSetTree,
                    strongFingerprintInputTree,
                    otherPathSetTree,
                    otherStrongFingerprintInputTree,
                    getDirectoryMembership,
                    getOtherDirectoryMembership));

            Contract.Assert(result.Count > 0);

            return result;
        }

        /// <summary>
        /// Diffs strong fingerprints.
        /// </summary>
        /// <param name="pathSetHash">Pathset hash.</param>
        /// <param name="pathSetTree">Pathset tree.</param>
        /// <param name="strongFingerprintInputTree">Strong fingerprint input tree.</param>
        /// <param name="otherPathSetHash">Other pathset hash.</param>
        /// <param name="otherPathSetTree">Other pathset tree.</param>
        /// <param name="otherStrongFingerprintInputTree">Other strong fingerprint input tree.</param>
        /// <param name="getDirectoryMembership">Delegate for getting directory membership.</param>
        /// <param name="getOtherDirectoryMembership">Delegate for getting other directory membership.</param>
        /// <returns></returns>
        public static JObject DiffPathSets(
            string pathSetHash,
            JsonNode pathSetTree,
            JsonNode strongFingerprintInputTree,
            string otherPathSetHash,
            JsonNode otherPathSetTree,
            JsonNode otherStrongFingerprintInputTree,
            Func<string, IReadOnlyList<string>> getDirectoryMembership,
            Func<string, IReadOnlyList<string>> getOtherDirectoryMembership)
        {
            JObject result = new JObject();

            if (pathSetHash == otherPathSetHash)
            {
                return result;
            }

            // {
            //   PathSetHash: { Old: old_path_set_hash, New: new_path_set_hash }
            // }
            AddPropertyIfNotNull(result, RenderSingleValueDiff("PathSetHash", pathSetHash, otherPathSetHash));

            JsonNode unsafeOptionsNode = JsonTree.FindNodeByName(pathSetTree, ObservedPathSet.Labels.UnsafeOptions);
            JsonNode otherUnsafeOptionsNode = JsonTree.FindNodeByName(otherPathSetTree, ObservedPathSet.Labels.UnsafeOptions);

            // {
            //   UnsafeOptions: 
            //   { 
            //        <property_Name>:
            //        {
            //           Old: old_value, 
            //           New: new_value
            //        }
            //        PreserveOutputInfo:
            //        {
            //            <property_Name>:
            //            {
            //               Old: old_value, 
            //               New: new_value
            //            }
            //        }
            //   }
            AddPropertyIfNotNull(result, DiffUnsafeOptions(unsafeOptionsNode, otherUnsafeOptionsNode));

            AddPropertyIfNotNull(
                result,
                DiffObservedPaths(
                    pathSetTree,
                    strongFingerprintInputTree,
                    otherPathSetTree,
                    otherStrongFingerprintInputTree,
                    getDirectoryMembership,
                    getOtherDirectoryMembership));

            JsonNode obsFileNameNode = JsonTree.FindNodeByName(pathSetTree, ObservedPathSet.Labels.ObservedAccessedFileNames);
            JsonNode otherObsFileNameNode = JsonTree.FindNodeByName(otherPathSetTree, ObservedPathSet.Labels.ObservedAccessedFileNames);

            bool hasDiff = ExtractUnorderedListDiff(obsFileNameNode.Values, otherObsFileNameNode.Values, out var addedFileNames, out var removedFileName);

            if (hasDiff)
            {
                result.Add(new JProperty(
                    ObservedPathSet.Labels.ObservedAccessedFileNames,
                    RenderUnorderedListDiff(addedFileNames, removedFileName, RenderPath)));
            }

            Contract.Assert(result.Count > 0);

            return result;
        }

        private static JProperty DiffWeakFingerprintField(JsonNode fieldNode, JsonNode otherFieldNode)
        {
            Contract.Requires(fieldNode != null);
            Contract.Requires(otherFieldNode != null);
            Contract.Requires(fieldNode.Name == otherFieldNode.Name);

            switch (fieldNode.Name)
            {
                case nameof(Process.Dependencies):
                {
                    using (var inputFileDataPool = InputFileDataMapPool.GetInstance())
                    using (var otherInputFileDataPool = InputFileDataMapPool.GetInstance())
                    {
                        var inputFileData = inputFileDataPool.Instance;
                        var otherInputFileData = otherInputFileDataPool.Instance;
                        populateInputFileData(fieldNode, inputFileData);
                        populateInputFileData(otherFieldNode, otherInputFileData);
                        return ExtractUnorderedMapDiff(
                            inputFileData,
                            otherInputFileData,
                            (dOld, dNew) => dOld.Equals(dNew),
                            out var added,
                            out var removed,
                            out var changed)
                            ? new JProperty(fieldNode.Name, RenderUnorderedMapDiff(
                                inputFileData,
                                otherInputFileData,
                                added,
                                removed,
                                changed,
                                RenderPath,
                                (dataA, dataB) => dataA.HashOrContent))
                            : null;
                    }
                }

                case nameof(Process.FileOutputs):
                {
                    using (var outputFileDataPool = OutputFileDataMapPool.GetInstance())
                    using (var otherOutputFileDataPool = OutputFileDataMapPool.GetInstance())
                    {
                        var outputFileData = outputFileDataPool.Instance;
                        var otherOutputFileData = otherOutputFileDataPool.Instance;
                        populateOutputFileData(fieldNode, outputFileData);
                        populateOutputFileData(otherFieldNode, otherOutputFileData);
                        return ExtractUnorderedMapDiff(
                            outputFileData,
                            otherOutputFileData,
                            (dOld, dNew) => dOld.Equals(dNew),
                            out var added,
                            out var removed,
                            out var changed)
                            ? new JProperty(fieldNode.Name, RenderUnorderedMapDiff(
                                outputFileData,
                                otherOutputFileData,
                                added,
                                removed,
                                changed,
                                RenderPath,
                                (dataA, dataB) => dataA.Attributes))
                            : null;
                    }
                }

                case nameof(PipFingerprintField.ExecutionAndFingerprintOptions):
                case nameof(Process.EnvironmentVariables):
                {
                    var result = DiffNameValuePairs(fieldNode, otherFieldNode);
                    return result != null ? new JProperty(fieldNode.Name, result) : null;
                }

                case nameof(Process.DirectoryDependencies):
                case nameof(Process.DirectoryOutputs):
                case nameof(Process.UntrackedPaths):
                case nameof(Process.UntrackedScopes):
                case nameof(Process.PreserveOutputWhitelist):
                case nameof(Process.SuccessExitCodes):
                case PipFingerprintField.Process.SourceChangeAffectedInputList:
                case nameof(Process.ChildProcessesToBreakawayFromSandbox):
                {
                    var data = fieldNode.Values;
                    var otherData = otherFieldNode.Values;
                    return ExtractUnorderedListDiff(data, otherData, out var added, out var removed)
                        ? new JProperty(fieldNode.Name, RenderUnorderedListDiff(added, removed, RenderPath))
                        : null;
                }
                default:
                    return RenderSingleValueDiff(fieldNode.Name, getSingleValueNode(fieldNode), getSingleValueNode(otherFieldNode));

            }

            string getSingleValueNode(JsonNode node) =>
                    node.Values.Count > 0
                    ? node.Values[0]
                    : CacheMissAnalysisUtilities.RepeatedStrings.MissingValue;

            void populateInputFileData(JsonNode dependencyNode, Dictionary<string, InputFileData> inputFileData)
            {
                JsonTree.VisitTree(
                    dependencyNode,
                    node =>
                    {
                        string value = CacheMissAnalysisUtilities.RepeatedStrings.MissingValue;
                        if (node.Values.Count > 0)
                        {
                            value = node.Values[0];
                        }
                        else if (node.Children.First != null
                            && node.Children.First.Value.Name == PipFingerprintField.FileDependency.PathNormalizedWriteFileContent
                            && node.Children.First.Value.Values.Count > 0)
                        {
                            value = node.Children.First.Value.Values[0];
                        }

                        inputFileData[node.Name] = new InputFileData(node.Name, value);
                    },
                    recurse: false);
            }

            void populateOutputFileData(JsonNode outputNode, Dictionary<string, OutputFileData> outputFileData)
            {
                JsonTree.VisitTree(
                    outputNode,
                    node =>
                    {
                        string value = CacheMissAnalysisUtilities.RepeatedStrings.MissingValue;
                        if (node.Children.First != null
                            && node.Children.First.Value.Name == PipFingerprintField.FileOutput.Attributes
                            && node.Children.First.Value.Values.Count > 0)
                        {
                            value = node.Children.First.Value.Values[0];
                        }

                        outputFileData[node.Name] = new OutputFileData(node.Name, value);
                    },
                    recurse: false);
            }
        }

        private static JObject DiffNameValuePairs(JsonNode fieldNode, JsonNode otherFieldNode)
        {
            using (var nameValuePairDataPool = NameValuePairDataMapPool.GetInstance())
            using (var othernameValuePairDataPool = NameValuePairDataMapPool.GetInstance())
            {
                var nameValuePairData = nameValuePairDataPool.Instance;
                var othernameValuePairData = othernameValuePairDataPool.Instance;
                PopulateNameValuePairData(fieldNode, nameValuePairData);
                PopulateNameValuePairData(otherFieldNode, othernameValuePairData);
                return ExtractUnorderedMapDiff(
                    nameValuePairData,
                    othernameValuePairData,
                    (dOld, dNew) => dOld.Equals(dNew),
                    out var added,
                    out var removed,
                    out var changed)
                    ? RenderUnorderedMapDiff(
                        nameValuePairData,
                        othernameValuePairData,
                        added,
                        removed,
                        changed,
                        k => k,
                        (dataA, dataB) => dataA.Value)
                    : null;
            }
        }

        private static void PopulateNameValuePairData(JsonNode nameValuePairNode, Dictionary<string, NameValuePairData> nameValuePairData)
        {
            JsonTree.VisitTree(
                nameValuePairNode,
                node =>
                {
                    nameValuePairData[node.Name] = new NameValuePairData(
                        node.Name,
                        node.Values.Count > 0 ? node.Values[0] : CacheMissAnalysisUtilities.RepeatedStrings.MissingValue);
                },
                recurse: false);
        }

        private static JProperty DiffUnsafeOptions(JsonNode unsafeOptionsNode, JsonNode otherUnsafeOptionsNode)
        {
            Contract.Requires(unsafeOptionsNode != null);
            Contract.Requires(otherUnsafeOptionsNode != null);
            Contract.Requires(unsafeOptionsNode.Name == otherUnsafeOptionsNode.Name);

            // Get the diff result of the single values in unsafeUnsafeOptions
            JObject result = DiffNameValuePairs(unsafeOptionsNode, otherUnsafeOptionsNode);

            // Deal with the preserveOutputInfo struct in unsafeUnsafeOptions
            JsonNode preserveOutputInfoNode = JsonTree.FindNodeByName(unsafeOptionsNode, nameof(PreserveOutputsInfo));
            JsonNode otherpreserveOutputInfoNode = JsonTree.FindNodeByName(otherUnsafeOptionsNode, nameof(PreserveOutputsInfo));
            if (preserveOutputInfoNode != null || otherpreserveOutputInfoNode != null)
            {
                JObject preserveOutputInfoResult = DiffNameValuePairs(
                    preserveOutputInfoNode ?? new JsonNode() { Name = nameof(PreserveOutputsInfo) }, 
                    otherpreserveOutputInfoNode ?? new JsonNode() { Name = nameof(PreserveOutputsInfo) });

                if (preserveOutputInfoResult != null)
                {
                    result = result ?? new JObject();
                    AddPropertyIfNotNull(result,new JProperty(nameof(PreserveOutputsInfo), preserveOutputInfoResult));
                }
            }

            return result != null ? new JProperty(unsafeOptionsNode.Name, result) : null;
        }

        private static JProperty DiffObservedPaths(
            JsonNode pathSetTree, 
            JsonNode strongFingerprintInputTree,
            JsonNode otherPathSetTree,
            JsonNode otherStrongFingerprintInputTree,
            Func<string, IReadOnlyList<string>> getDirectoryMembership,
            Func<string, IReadOnlyList<string>> getOtherDirectoryMembership)
        {
            JsonNode pathsNode = JsonTree.FindNodeByName(pathSetTree, ObservedPathSet.Labels.Paths);
            JsonNode otherPathsNode = JsonTree.FindNodeByName(otherPathSetTree, ObservedPathSet.Labels.Paths);

            JsonNode observedInputsTree = JsonTree.FindNodeByName(strongFingerprintInputTree, ObservedInputConstants.ObservedInputs);
            JsonNode otherObservedInputsTree = JsonTree.FindNodeByName(otherStrongFingerprintInputTree, ObservedInputConstants.ObservedInputs);

            using (var pathSetDataPool = ObservedInputDataMapPool.GetInstance())
            using (var otherPathSetDataPool = ObservedInputDataMapPool.GetInstance())
            {
                var pathSetData = pathSetDataPool.Instance;
                var otherPathSetData = otherPathSetDataPool.Instance;
                traversePathSetPaths(pathsNode, observedInputsTree, pathSetData);
                traversePathSetPaths(otherPathsNode, otherObservedInputsTree, otherPathSetData);

                bool hasDiff = ExtractUnorderedMapDiff(
                    pathSetData,
                    otherPathSetData,
                    (data, otherData) => data.Equals(otherData),
                    out var added,
                    out var removed,
                    out var changed);

                if (hasDiff)
                {
                    // {
                    //   Paths: { 
                    //      Added  : [..paths..],
                    //      Removed: [..paths..],
                    //      Changed: {
                    //        path: { Old: ..., New: ... }
                    //      }: 
                    //   }
                    // }
                    return new JProperty(
                        ObservedPathSet.Labels.Paths,
                        RenderUnorderedMapDiff(
                            pathSetData,
                            otherPathSetData,
                            added,
                            removed,
                            changed,
                            RenderPath,
                            (dataA, dataB) => dataA.DescribeDiffWithoutPath(dataB),
                            c => diffDirectoryIfApplicable(pathSetData, otherPathSetData, c)));
                }

                return null;
            }

            JProperty diffDirectoryIfApplicable(Dictionary<string, ObservedInputData> obsInputData, Dictionary<string, ObservedInputData> otherObsInputData, string possiblyChangeDirectory)
            {
                // {
                //    Members: {
                //      Added : [..file..],
                //      Removed : [..file..]
                //    }
                // }
                const string MembersLabel = "Members";

                var change = obsInputData[possiblyChangeDirectory];
                var otherChange = otherObsInputData[possiblyChangeDirectory];
                if (change.AccessType == ObservedInputConstants.DirectoryEnumeration
                    && otherChange.AccessType == ObservedInputConstants.DirectoryEnumeration
                    && change.Pattern == otherChange.Pattern)
                {
                    var members = getDirectoryMembership(change.Hash);

                    if (members == null)
                    {
                        return new JProperty(MembersLabel, $"{CacheMissAnalysisUtilities.RepeatedStrings.MissingDirectoryMembershipFingerprint} ({nameof(ObservedInputData.Hash)}: {change.Hash})");
                    }

                    var otherMembers = getOtherDirectoryMembership(otherChange.Hash);

                    if (otherMembers == null)
                    {
                        return new JProperty(MembersLabel, $"{CacheMissAnalysisUtilities.RepeatedStrings.MissingDirectoryMembershipFingerprint} ({nameof(ObservedInputData.Hash)}: {otherChange.Hash})");
                    }

                    bool hasDiff = ExtractUnorderedListDiff(members, otherMembers, out var addedMembers, out var removedMembers);

                    if (hasDiff)
                    {
                        return new JProperty(MembersLabel, RenderUnorderedListDiff(addedMembers, removedMembers, RenderPath));
                    }
                }

                return null;
            }

            void traversePathSetPaths(
                JsonNode pathSetTree,
                JsonNode strongFingerprintInputTree,
                Dictionary<string, ObservedInputData> populatedData)
            {
                TraversePathSetPaths(pathSetTree, strongFingerprintInputTree, data => populatedData[data.Path] = data);
            }
        }

        #endregion Diff-ing

        #region Rendering

        private static JObject RenderUnorderedMapDiff<T>(
                IReadOnlyDictionary<string, T> oldData,
                IReadOnlyDictionary<string, T> newData,
                IReadOnlyList<string> added,
                IReadOnlyList<string> removed,
                IReadOnlyList<string> changed,
                Func<string, string> renderKey,
                Func<T, T, string> describeValueDiff,
                Func<string, JProperty> extraDiffChange = null)
        {
            JObject result = RenderUnorderedListDiff(added, removed, renderKey);

            JProperty changedProperty = null;

            if (changed != null && changed.Count > 0)
            {
                changedProperty = new JProperty(
                    FieldChanged,
                    new JObject(changed.Select(c => RenderSingleValueDiff(
                        renderKey(c),
                        describeValueDiff(oldData[c], newData[c]),
                        describeValueDiff(newData[c], oldData[c]),
                        extraDiffChange)).ToArray()));
            }

            if (result == null && changedProperty == null)
            {
                return null;
            }

            if (result == null)
            {
                result = new JObject();
            }

            if (changedProperty != null)
            {
                result.Add(changedProperty);
            }

            return result;
        }

        private static JObject RenderUnorderedListDiff(
            IReadOnlyList<string> added,
            IReadOnlyList<string> removed,
            Func<string, string> renderItem)
        {
            JProperty addedProperty = added != null && added.Count > 0 ? new JProperty(FieldAdded, new JArray(added.Select(a => renderItem(a)).ToArray())) : null;
            JProperty removedProperty = removed != null && removed.Count > 0 ? new JProperty(FieldRemoved, new JArray(removed.Select(a => renderItem(a)).ToArray())) : null;

            if (addedProperty == null && removedProperty == null)
            {
                return null;
            }

            JObject result = new JObject();

            addToResult(addedProperty);
            addToResult(removedProperty);

            return result;

            void addToResult(JProperty p)
            {
                if (p != null)
                {
                    result.Add(p);
                }
            }
        }

        private static JProperty RenderSingleValueDiff(string key, string oldValue, string newValue, Func<string, JProperty> extraDiff = null)
        {
            if (oldValue == newValue)
            {
                return null;
            }

            var diff = new []
                {
                    new JProperty(FieldOld, oldValue),
                    new JProperty(FieldNew, newValue)
                };

            var diffObject = new JObject(diff);

            if (extraDiff != null)
            {
                JProperty extra = extraDiff(key);
                if (extra != null)
                {
                    diffObject.Add(extra);
                }
            }
            return new JProperty(key, diffObject);
        }

        private static string RenderPath(string path) => path;

        #endregion Rendering

        #region Traversal

        private static void TraversePathSetPaths(
            JsonNode pathSetPathsNode,
            JsonNode observedInputs,
            Action<ObservedInputData> action)
        {
            string path = null;
            string flags = null;
            string pattern = null;

            string hashMarker = null;
            string hash = null;

            var obIt = observedInputs?.Children.First;

            for (var it = pathSetPathsNode.Children.First; it != null; it = it.Next)
            {
                var elem = it.Value;
                switch (elem.Name)
                {
                    case ObservedPathEntryConstants.Path:
                        if (path != null)
                        {
                            action(new ObservedInputData(path, flags, pattern, hashMarker, hash));
                            path = null;
                            flags = null;
                            pattern = null;
                            hashMarker = null;
                            hash = null;
                        }

                        path = elem.Values[0];

                        if (obIt != null)
                        {
                            hashMarker = obIt.Value.Name;
                            hash = obIt.Value.Values[0];
                            obIt = obIt.Next;
                        }

                        break;
                    case ObservedPathEntryConstants.Flags:
                        Contract.Assert(path != null);
                        flags = elem.Values[0];
                        break;
                    case ObservedPathEntryConstants.EnumeratePatternRegex:
                        Contract.Assert(path != null);
                        pattern = elem.Values[0];
                        break;
                    default:
                        break;
                }
            }

            if (path != null)
            {
                action(new ObservedInputData(path, flags, pattern, hashMarker, hash));
            }
        }

        #endregion Traversal

        #region Utilities

        private static void AddPropertyIfNotNull(JObject o, JProperty p)
        {
            if (p != null)
            {
                o.Add(p);
            }
        }

        private static ObjectPool<Dictionary<string, JsonNode>> JsonNodeMapPool { get; } =
            new ObjectPool<Dictionary<string, JsonNode>>(
                () => new Dictionary<string, JsonNode>(),
                map => { map.Clear(); return map; });

        #endregion Utilities
    }
}
