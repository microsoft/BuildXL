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
        public void BasicTest()
        {
            string jsonA = "{\"Dependencies\":[{\"a\":\"valueA\"},{\"b\":\"valueB\"}]}";

            var treeA = JsonTree.Deserialize(jsonA);
            string deserialized = JsonTree.Serialize(treeA);

            XAssert.AreEqual(JsonTree.PrettyPrintJson(jsonA), deserialized);

            string jsonB = "{\"Object\":[{\"WeakFingerprint\":\"097ED1ED5703816B8F286410076E658260D029BC\"},{\"StrongFingerprint\":\"C926945B5824E1CC7C512D66FB3B8FE869B71936\"}]}";

            var treeB = JsonTree.Deserialize(jsonB);
            string deserializedB = JsonTree.Serialize(treeB);

            XAssert.AreEqual(JsonTree.PrettyPrintJson(jsonB), deserializedB);
        }

        [Fact]
        public void RepeatNameTest()
        {
            string jsonA = "{\"Object\":[{\"PathSet\":\"VSO0:890000000000000000000000000000000000000000000000000000000000000000\"}," +
                "{\"ObservedInputs\":[{\"P\":\"\"},{\"P\":\"\"},{\"P\":\"\"},{\"P\":\"\"},{\"P\":\"\"},{\"A\":\"\"},{\"P\":\"\"},{\"E\":\"VSO0:4D939FB1E1CE7586909F84F4FEFB0F385B31DD586FF97FC14874BCDB4B2A801400\"}]}]}";

            var treeA = JsonTree.Deserialize(jsonA);
            string deserialized = JsonTree.Serialize(treeA);

            XAssert.AreEqual(JsonTree.PrettyPrintJson(jsonA), deserialized);
        }

        [Fact]
        public void NestedNamelessArraysTest()
        {
            var jsonA = "{\"key\":[\"value\", [\"nestedValue\"]]}";

            var treeA = JsonTree.Deserialize(jsonA);
            var keyNode = JsonTree.FindNodeByName(treeA, "key");
            XAssert.IsTrue(keyNode.Values.Contains("value") && keyNode.Values.Contains("nestedValue"));

            string deserialized = JsonTree.Serialize(treeA);
            XAssert.IsTrue(deserialized.Contains("value") && deserialized.Contains("nestedValue"));

            XAssert.AreNotEqual(JsonTree.PrettyPrintJson(jsonA), deserialized);
        }

        [Fact]
        public void LargeNestedObjectTest()
        {

            string jsonA = "{\"Object\":[\"10?B:\\\\out\\\\objects\\\\d\\\\z\\\\bqkrr8zzp30wta21jx01qhl915zwxn\\\\xunit-out\\\\FingerprintS1F4BDE58\\\\2\\\\obj\\\\readonly\\\\obj_0???None?\"," +
                "\"5?B:\\\\out\\\\objects\\\\d\\\\z\\\\bqkrr8zzp30wta21jx01qhl915zwxn\\\\xunit-out\\\\FingerprintS1F4BDE58\\\\2\\\\obj\\\\obj_1???None?\",{\"ExecutionAndFingerprintOptionsHash\":\"224F5F8E17D4590F2CD7AFAB82AA672D2E0C86E8\"}," +
                "{\"ContentHashAlgorithmName\":\"Vso0\"},{\"PipType\":\"80\"}," +
                "{\"Executable\":{\"B:/out/objects/d/z/bqkrr8zzp30wta21jx01qhl915zwxn/Test.BuildXL.FingerprintStore-test-deployment/TestProcess/Test.BuildXL.Executables.TestProcess.exe\":\"VSO0:17426AF3467E448CD8E1146EEE9CFC27107690D39B8091E2882AB164B57ED90400\"}}," +
                "{\"WorkingDirectory\":\"B:/out/objects/d/z/bqkrr8zzp30wta21jx01qhl915zwxn/xunit-out/FingerprintS1F4BDE58/2/obj/z/7/69c8nz08y0ehxg5vn8ex8g7g59at90/Test.BuildXL.Executables.TestProcess\"}," +
                "{\"StandardError\":\"{Invalid}\"},{\"StandardOutput\":\"{Invalid}\"}," +
                "{\"Dependencies\":{\"B:/out/objects/d/z/bqkrr8zzp30wta21jx01qhl915zwxn/Test.BuildXL.FingerprintStore-test-deployment/TestProcess/Test.BuildXL.Executables.TestProcess.exe\":\"VSO0:17426AF3467E448CD8E1146EEE9CFC27107690D39B8091E2882AB164B57ED90400\"}}," +
                "{\"DirectoryDependencies\":[]},{\"Outputs\":[{\"Path\":\"B:/out/objects/d/z/bqkrr8zzp30wta21jx01qhl915zwxn/xunit-out/ FingerprintS1F4BDE58 / 2 / obj / obj_1\"},{\"Attributes\":\"0\"}]},{\"DirectoryOutputs\":[]}," +
                "{\"UntrackedPaths\":[]},{\"UntrackedScopes\":[\"C:/WINDOWS\",\"C:/Users/userName/AppData/Local/Microsoft/Windows/INetCache\",\"C:/Users/userName/AppData/Local/Microsoft/Windows/History\"]}," +
                "{\"HasUntrackedChildProcesses\":\"0\"},{\"PipData\":\"Arguments\"},{\"Escaping\":\"2\"},{\"Separator\":\" \"},{\"Fragments\":\"1\"},{\"PipData\":\"NestedFragment\"},{\"Escaping\":\"2\"},{\"Separator\":\" \"},{\"Fragments\":\"2\"}," +
                "{\"Environment\":[]},{\"WarningTimeout\":\"-1\"},{\"WarningRegex.Pattern\":\"^\\\\s*((((((\\\\d+>)?[a-zA-Z]?:[^:]*)|([^:]*))):)|())(()|([^:]*? ))warning( \\\\s*([^: ]*))?\\\\s*:.*$\"},{\"WarningRegex.Options\":\"1\"}," +
                "{\"ErrorRegex.Pattern\":\".*\"},{\"ErrorRegex.Options\":\"1\"},{\"SuccessExitCodes\":[]}]}";

            var treeA = JsonTree.Deserialize(jsonA);
            string deserialized = JsonTree.Serialize(treeA);

            XAssert.AreEqual(JsonTree.PrettyPrintJson(jsonA), deserialized);
        }
    }
}
