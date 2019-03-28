// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using BuildXL.Engine.Cache.Serialization;
using Test.BuildXL.TestUtilities.Xunit;

namespace Test.BuildXL.Engine.Cache
{
    public class JsonTreeTests
    {
        public JsonTreeTests()
        {
        }

        [Fact]
        public void MoveObjectsInList()
        {
            string jsonA = "{\"Dependencies\":[{\"a\":\"valueA\"},{\"b\":\"valueB\"}]}";
            string jsonB = "{\"Dependencies\":[{\"b\":\"valueB\"},{\"a\":\"valueA\"}]}";

            var treeA = JsonTree.BuildTree(jsonA);
            var treeB = JsonTree.BuildTree(jsonB);

            var changeList = JsonTree.DiffTrees(treeA, treeB);
            // The change list must detect either "a" or "b" as having moved positions
            // the current algorithm detects happens to detect "a"
            AssertRemoved("a", changeList);
            AssertAdded("a", changeList);
            var printDiff = JsonTree.PrintTreeChangeList(changeList);

            XAssert.IsTrue(printDiff.Contains("a") && printDiff.Contains("valueA"));
            XAssert.IsFalse(printDiff.Contains("b") || printDiff.Contains("valueB"));
        }

        [Fact]
        public void BasicTest()
        {
            string jsonA = "{\"Object\":[{\"WeakFingerprint\":\"097ED1ED5703816B8F286410076E658260D029BC\"},{\"StrongFingerprint\":\"C926945B5824E1CC7C512D66FB3B8FE869B71936\"}]}";
            string jsonB = "{\"Object\":[{\"WeakFingerprint\":\"097ED1ED5703816B8F286410076E658260D029BC\"},{\"StrongFingerprint\":\"DefinitelyNotTheSameFingerprint\"}]}";

            var treeA = JsonTree.BuildTree(jsonA);
            var treeB = JsonTree.BuildTree(jsonB);

            var changeList = JsonTree.DiffTrees(treeA, treeB);
            AssertUnchanged("WeakFingerprint", changeList);
            AssertChanged("StrongFingerprint", changeList);
        }

        [Fact]
        public void RepeatNameTest()
        {
            string jsonA = "{\"Object\":[{\"PathSet\":\"VSO0:890000000000000000000000000000000000000000000000000000000000000000\"}," +
                "{\"ObservedInputs\":[{\"P\":\"\"},{\"P\":\"\"},{\"P\":\"\"},{\"P\":\"\"},{\"P\":\"\"},{\"A\":\"\"},{\"P\":\"\"},{\"E\":\"VSO0:4D939FB1E1CE7586909F84F4FEFB0F385B31DD586FF97FC14874BCDB4B2A801400\"}]}]}";
            string jsonB = "{\"Object\":[{\"PathSet\":\"VSO0:890000000000000000000000000000000000000000000000000000000000000000\"}," +
                "{\"ObservedInputs\":[{\"P\":\"\"},{\"P\":\"\"},{\"P\":\"CHANGEDVALUE\"},{\"P\":\"\"},{\"P\":\"\"},{\"P\":\"\"},{\"E\":\"VSO0:4D939FB1E1CE7586909F84F4FEFB0F385B31DD586FF97FC14874BCDB4B2A801400\"}]}]}";

            var treeA = JsonTree.BuildTree(jsonA);
            var treeB = JsonTree.BuildTree(jsonB);

            var changeList = JsonTree.DiffTrees(treeA, treeB);
            AssertChanged("P", changeList);
            AssertAdded("P", changeList);
            AssertRemoved("A", changeList);
        }

        [Fact]
        public void LargeNestedObjectTest()
        {

            string jsonA = "{\"Object\":[{\"ExecutionAndFingerprintOptionsHash\":\"224F5F8E17D4590F2CD7AFAB82AA672D2E0C86E8\"}," +
                "{\"ContentHashAlgorithmName\":\"Vso0\"},{\"PipType\":\"80\"}," +
                "{\"Executable\":[{\"B:/out/objects/d/z/bqkrr8zzp30wta21jx01qhl915zwxn/Test.BuildXL.FingerprintStore-test-deployment/TestProcess/Test.BuildXL.Executables.TestProcess.exe\":\"VSO0:17426AF3467E448CD8E1146EEE9CFC27107690D39B8091E2882AB164B57ED90400\"}]}," +
                "{\"WorkingDirectory\":\"B:/out/objects/d/z/bqkrr8zzp30wta21jx01qhl915zwxn/xunit-out/FingerprintS1F4BDE58/2/obj/z/7/69c8nz08y0ehxg5vn8ex8g7g59at90/Test.BuildXL.Executables.TestProcess\"}," +
                "{\"StandardError\":\"{Invalid}\"},{\"StandardOutput\":\"{Invalid}\"}," +
                "{\"Dependencies\":[{\"B:/out/objects/d/z/bqkrr8zzp30wta21jx01qhl915zwxn/Test.BuildXL.FingerprintStore-test-deployment/TestProcess/Test.BuildXL.Executables.TestProcess.exe\":\"VSO0:17426AF3467E448CD8E1146EEE9CFC27107690D39B8091E2882AB164B57ED90400\"}]}," +
                "{\"DirectoryDependencies\":[]},{\"Outputs\":[{\"Path\":\"B:/out/objects/d/z/bqkrr8zzp30wta21jx01qhl915zwxn/xunit-out/ FingerprintS1F4BDE58 / 2 / obj / obj_1\"},{\"Attributes\":\"0\"}]},{\"DirectoryOutputs\":[]}," +
                "{\"UntrackedPaths\":[]},{\"UntrackedScopes\":[\"C:/WINDOWS\",\"C:/Users/userName/AppData/Local/Microsoft/Windows/INetCache\",\"C:/Users/userName/AppData/Local/Microsoft/Windows/History\"]}," +
                "{\"HasUntrackedChildProcesses\":\"0\"},{\"PipData\":\"Arguments\"},[{\"Escaping\":\"2\"},{\"Separator\":\" \"},{\"Fragments\":\"1\"},[{\"PipData\":\"NestedFragment\"},[{\"Escaping\":\"2\"},{\"Separator\":\" \"},{\"Fragments\":\"2\"}," +
                "[\"10?B:\\\\out\\\\objects\\\\d\\\\z\\\\bqkrr8zzp30wta21jx01qhl915zwxn\\\\xunit-out\\\\FingerprintS1F4BDE58\\\\2\\\\obj\\\\readonly\\\\obj_0???None?\"," +
                "\"5?B:\\\\out\\\\objects\\\\d\\\\z\\\\bqkrr8zzp30wta21jx01qhl915zwxn\\\\xunit-out\\\\FingerprintS1F4BDE58\\\\2\\\\obj\\\\obj_1???None?\"]]]]," +
                "{\"Environment\":[]},{\"WarningTimeout\":\"-1\"},{\"WarningRegex.Pattern\":\"^\\\\s*((((((\\\\d+>)?[a-zA-Z]?:[^:]*)|([^:]*))):)|())(()|([^:]*? ))warning( \\\\s*([^: ]*))?\\\\s*:.*$\"},{\"WarningRegex.Options\":\"1\"}," +
                "{\"ErrorRegex.Pattern\":\".*\"},{\"ErrorRegex.Options\":\"1\"},{\"SuccessExitCodes\":[]}]}";

            string jsonB = "{\"Object\":[{\"ExecutionAndFingerprintOptionsHash\":\"224F5F8E17D4590F2CD7AFAB82AA672D2E0C86E8\"}," +
                "{\"ContentHashAlgorithmName\":\"Vso0\"},{\"PipType\":\"80\"}," +
                "{\"Executable\":[{\"B:/out/objects/d/z/bqkrr8zzp30wta21jx01qhl915zwxn/Test.BuildXL.FingerprintStore-test-deployment/TestProcess/Test.BuildXL.Executables.TestProcess.exe\":\"VSO0:17426AF3467E448CD8E1146EEE9CFC27107690D39B8091E2882AB164B57ED90400\"}]}," +
                "{\"WorkingDirectory\":\"B:/out/objects/d/z/bqkrr8zzp30wta21jx01qhl915zwxn/xunit-out/FingerprintS1F4BDE58/2/obj/z/7/69c8nz08y0ehxg5vn8ex8g7g59at90/Test.BuildXL.Executables.TestProcess\"}," +
                "{\"StandardOutput\":\"CHANGED\"}," +
                "{\"Dependencies\":[{\"B:/out/objects/d/z/bqkrr8zzp30wta21jx01qhl915zwxn/Test.BuildXL.FingerprintStore-test-deployment/TestProcess/Test.BuildXL.Executables.TestProcess.exe\":\"VSO0:17426AF3467E448CD8E1146EEE9CFC27107690D39B8091E2882AB164B57ED90400\"}]}," +
                "{\"DirectoryDependencies\":[]},{\"Outputs\":[{\"Path\":\"B:/out/objects/d/z/bqkrr8zzp30wta21jx01qhl915zwxn/xunit-out/ FingerprintS1F4BDE58 / 2 / obj / obj_1\"},{\"Attributes\":\"0\"}]},{\"DirectoryOutputs\":[]}," +
                "{\"UntrackedPaths\":[]},{\"UntrackedScopes\":[\"C:/WINDOWS\",\"C:/Users/userName/AppData/Local/Microsoft/Windows/INetCache\",\"C:/Users/userName/AppData/Local/Microsoft/Windows/CHANGED\"]}," +
                "{\"HasUntrackedChildProcesses\":\"0\"},{\"Timeout\":\"-1\"},{\"PipData\":\"Arguments\"},[{\"Escaping\":\"2\"},{\"Separator\":\" \"},{\"Fragments\":\"1\"},[{\"PipData\":\"NestedFragment\"},[{\"Escaping\":\"2\"},{\"Separator\":\" \"},{\"Fragments\":\"2\"}," +
                "[\"10?B:\\\\out\\\\objects\\\\d\\\\z\\\\bqkrr8zzp30wta21jx01qhl915zwxn\\\\xunit-out\\\\FingerprintS1F4BDE58\\\\2\\\\obj\\\\readonly\\\\obj_0???None?\"," +
                "\"5?B:\\\\out\\\\objects\\\\d\\\\z\\\\bqkrr8zzp30wta21jx01qhl915zwxn\\\\xunit-out\\\\FingerprintS1F4BDE58\\\\2\\\\obj\\\\obj_1???None?\"]]]]," +
                "{\"Environment\":[]},{\"WarningTimeout\":\"-1\"},{\"AddedTimeout\":\"-1\"},{\"WarningRegex.Pattern\":\"^\\\\s*((((((\\\\d+>)?[a-zA-Z]?:[^:]*)|([^:]*))):)|())(()|([^:]*? ))warning( \\\\s*([^: ]*))?\\\\s*:.*$\"},{\"WarningRegex.Options\":\"1\"}," +
                "{\"ErrorRegex.Pattern\":\".*\"},{\"ErrorRegex.Options\":\"1\"},{\"SuccessExitCodes\":[]}]}";

            var treeA = JsonTree.BuildTree(jsonA);
            var treeB = JsonTree.BuildTree(jsonB);

            var changeList = JsonTree.DiffTrees(treeA, treeB);
            AssertUnchanged("Outputs", changeList);
            AssertChanged("StandardOutput", changeList);
            AssertChanged("UntrackedScopes", changeList);
            AssertRemoved("StandardError", changeList);
            AssertAdded("AddedTimeout", changeList);
        }

        private void AssertRemoved(string propertyName, ChangeList<JsonNode> changeList)
        {
            bool removedFound = false;
            for (int i = 0; i < changeList.Count; ++i)
            {
                var change = changeList[i];
                if (change.Value.Name == propertyName
                    && change.ChangeType == ChangeList<JsonNode>.ChangeType.Removed)
                {
                    removedFound = true;
                    break;
                }
            }
            XAssert.IsTrue(removedFound);
        }

        private void AssertAdded(string propertyName, ChangeList<JsonNode> changeList)
        {
            bool addedFound = false;
            for (int i = 0; i < changeList.Count; ++i)
            {
                var change = changeList[i];
                if (change.Value.Name == propertyName
                    && change.ChangeType == ChangeList<JsonNode>.ChangeType.Added)
                {
                    addedFound = true;
                    break;
                }
            }
            XAssert.IsTrue(addedFound);
        }

        private void AssertUnchanged(string propertyName, ChangeList<JsonNode> changeList)
        {
            JsonNode removed = null;
            JsonNode added = null;
            for (int i = 0; i < changeList.Count; ++i)
            {
                var change = changeList[i];
                if (change.Value.Name != propertyName)
                {
                    continue;
                }
                switch (change.ChangeType)
                {
                    case ChangeList<JsonNode>.ChangeType.Removed:
                        removed = change.Value;
                        break;
                    case ChangeList<JsonNode>.ChangeType.Added:
                        added = change.Value;
                        break;
                }
            }

            XAssert.AreEqual(null, removed);
            XAssert.AreEqual(null, added);
        }

        /// <summary>
        /// To be "changed", a node must have the same name, but different values.
        /// </summary>
        private void AssertChanged(string propertyName, ChangeList<JsonNode> changeList)
        {
            JsonNode removed = null;
            JsonNode added = null;
            for (int i = 0; i < changeList.Count; ++i)
            {
                var change = changeList[i];
                if (change.Value.Name != propertyName)
                {
                    continue;
                }
                switch (change.ChangeType)
                {
                    case ChangeList<JsonNode>.ChangeType.Removed:
                        removed = change.Value;
                        break;
                    case ChangeList<JsonNode>.ChangeType.Added:
                        added = change.Value;
                        break;
                }
            }

            XAssert.AreNotEqual(null, removed);
            XAssert.AreNotEqual(null, added);
            XAssert.AreNotEqual(removed, added);
        }
    }
}
