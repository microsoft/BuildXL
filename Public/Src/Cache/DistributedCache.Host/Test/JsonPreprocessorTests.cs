// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using FluentAssertions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Host.Test
{
    public class JsonPreprocessorTests
    {
        private static readonly string TestJsonForOverrides = @"
            {
                'StringField' : 'value.{RegionId:U}.{StampId:L}',
                'DoubleField'                    : 12.3,
                'DoubleField[Stamp:R1_S3|R1_S4]' : 10.9,
                'DoubleField[Stamp:R1_S4|R1_S5]' : 11.0,

                'IntField'                       : 12,
                'IntField[Stamp:R1_S1]'          : 15,
                'IntField[Stamp:R1_S2|R2_S1]'    : 18,

                'IntField[Feature:MyFeature][Stamp:R1_S1]'    : 19,
                'IntField[Feature:MyFeature]'    : 20,
                'IntField[Feature:My_Feature]'    : 21,

                'OptionalField[Region:R10]' : 'region-defined',
                'OptionalField[Stamp:R10_S1]' : 'stamp-defined',
   
                'PrecedenceField[Stamp:R10_S1]' : 'stamp-overridden',
                'PrecedenceField[Capability:Cap20]' : 'capability-overridden',
                'PrecedenceField[Region:R10]' : 'region-overridden',
                'PrecedenceField' : 'original',

                'ArrayField1' : [ 
                    'one', 
                    'two',
                    'three'    
                ],

                'ArrayField2' : [
                    { 'Field1' : 'foo' }, 
                    { 'Field1' : 'bar' }
                ],

                'MapField1' : {
                    'map1.1' : 'map1.one',
                    'map1.2' : 'map1.two',
                },

                'MapField1[Region:R1]' : {
                    'map1.overridden' : 'overridden_for {RegionId}',
                },

                'MapField2' : {
                    'map1.1' : 1,
                    'map1.2'               : 2,
                    'map1.2[Region:R5|R6]' : 3,
                    'map1.2[Region:R4|R6]' : 4,
                },

                'MapField3' : {
                    'map1.1' : { 'Field1' : 1 },
                    'map1.2' : { 'Field1' : 2 },
                },   

                'NestedStruct' : {
                    'Field1' : 25.0,
                    'Field2'                 : 'done',
                    'Field2   [Region:R2|R3]' : 'done_for {StampId:AL}',
                    'Field3' : 123,
                    'Field3[Capability:MeanngOfLife]' : 42, 
                    'Field3[Stamp:R1_S43]' : 43,
                }, 

                'NullField' : null,
            }
        ".Replace("'", "\"");

        private string[] DefaultCapability = new[] { "Default" };

        [Fact]
        public void NoOverrides()
        {
            var actual = LoadBaseline();
            Assert.Equal("value.DEFAULTREGION.defaultstamp", actual.StringField);
            Assert.Null(actual.OptionalField);
        }

        [Fact]
        public void TemplatingNestedProperties()
        {
            const string Config = @"{
            'AnotherProp{Stamp:LA}': 42,
            'PlainSecrets[Stamp:DM_S1|DM_S2]': {
                'cbcacheteststorage{StampId:LA}': {
                    'VaultKeyName': 'cbcacheteststorage{StampId:LA}',
                    'MfAcl{StampId:LA}': ['{StampId:LA}']
                },
                'cbcache-test-redis-secondary-{StampId:LA}': {
                    'VaultKeyName': 'cbcache-test-redis-secondary-{StampId:LA}',
                    'MfAcl': ['2']
                },
                'Nested[Stamp:DM_S1|DM_S2]': {
                    'cbcacheteststorage{StampId:LA}': {
                        'VaultKeyName': 'cbcacheteststorage{StampId:LA}',
                        'MfAcl{StampId:LA}': ['{StampId:LA}']
                    },
                    'cbcache-test-redis-secondary-{StampId:LA}': {
                        'VaultKeyName': 'cbcache-test-redis-secondary-{StampId:LA}',
                        'MfAcl': ['2'],
                        'Path': '{$Home}'
                    },
                }
              }
            }";

            var preProc = GetPreprocessor("DM_S1", "DM", DefaultCapability);

            var actual = PreprocessAndDeserialize<JsonObject>(Config, preProc);
            var expected = Deserialize<JsonObject>(Config);

            var actualAsString = actual.ToString();
            var expectedAsString = expected
                .ToString()
                .Replace("[Stamp:DM_S1|DM_S2]", "")
                .Replace("{Stamp:LA}", "dms1")
                .Replace("{StampId:LA}", "dms1")
                .Replace("{$Home}", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify).Replace(@"\", @"\\"));

            Assert.Equal(expectedAsString, actualAsString);
        }

        [Fact]
        public void TemplatingPropertyNameWithCollision()
        {
            const string Config = @"{
            'Prop{StampId:LA}': 42,
            'Propdms1': 31,
            'PropDM{StampId}': 1,
            'Prop{RegionId}DM_S1': 2,
            }";

            var preProc = GetPreprocessor("DM_S1", "DM", DefaultCapability);
            var actual = PreprocessAndDeserialize<JsonObject>(Config, preProc);

            // The last override wins.
            Assert.Equal(31, actual["Propdms1"]);
            Assert.Equal(2, actual["PropDMDM_S1"]);
        }

        [Fact]
        public void OverrideWithNotConstraint()
        {
            const string Config = @"{
            //'PropNotOveridden': 31,
            //'PropNotOveridden[!Stamp:DM_S1|DM_S2]': 2,
            //'PropOveridden': 31,
            //'PropOveridden[!Stamp:DM_S2]': 1,
            //'PropOveridden2': 32,
            //'PropOveridden2[Stamp:!DM_S2]': 2,
            'PropOveridden3': 33,
            'PropOveridden3[Feature:!Special]': 3,
            }";

            var preProc = GetPreprocessor("DM_S1", "DM", DefaultCapability);
            var actual = PreprocessAndDeserialize<JsonObject>(Config, preProc);

            //Assert.Equal(31, actual["PropNotOveridden"]);
            //Assert.Equal(1, actual["PropOveridden"]);
            //Assert.Equal(2, actual["PropOveridden2"]);
            Assert.Equal(3, actual["PropOveridden3"]);
        }

        [Fact]
        public void RingOverride()
        {
            const string Config = @"{
            'PropNotOveridden': 31,
            'PropNotOveridden[Ring:Ring_2]': 2,
            'PropOveridden': 31,
            'PropOveridden[Ring:Ring_1]': 2,
            }";

            var preProc = GetPreprocessor("DM_S1", "DM", DefaultCapability, ringId: "Ring_1");
            var actual = PreprocessAndDeserialize<JsonObject>(Config, preProc);

            Assert.Equal(31, actual["PropNotOveridden"]);
            Assert.Equal(2, actual["PropOveridden"]);
        }

        [Fact]
        public void OverrideWithPeriod()
        {
            string overrideJson = @"
            {
                'DoubleField'                    : 12.3,
                'DoubleField[Stamp:R1.S1]' : 10.9
            }";

            var preProc = GetPreprocessor("R1.S1", "R1", DefaultCapability);
            var actual = PreprocessConfig(overrideJson, preProc);

            Assert.Equal(actual.DoubleField, 10.9);
        }

        [Fact]
        public void OverrideSingleStampOrRegion()
        {
            var preProc = GetPreprocessor("R1_S1", "R1", DefaultCapability);
            var actual = PreprocessConfig(TestJsonForOverrides, preProc);

            var expected = LoadBaseline();
            expected.StringField = "value.R1.r1_s1";
            expected.IntField = 15;
            expected.MapField1 = new Dictionary<string, string>()
            {
                { "map1.overridden", "overridden_for R1" }
            };

            Validate(expected, actual);
        }

        [Fact]
        public void OverrideByFeature()
        {
            /*
             * 'IntField'                       : 12,
             * 'IntField[Stamp:R1_S1]'          : 15,
                'IntField[Feature:MyFeature][Stamp:R1_S1]'    : 19,
                'IntField[Feature:MyFeature]'    : 20,
                'IntField[Feature:My_Feature]'    : 21,
             */

            // Passing multiple features to make sure that the result is correct when one the elements in the list matches a value in the config.
            var stampAndFeature = GetPreprocessor("R1_S1", "R1", DefaultCapability, new string[] { "MyFeature3", "MyFeature" });
            TestConfig stampAndFeatureConfig = ParseConfig(stampAndFeature);
            Assert.Equal(20, stampAndFeatureConfig.IntField);

            // If there are two orthogonal options that match the result, then the last one
            // wins.
            stampAndFeature = GetPreprocessor("R1_S1", "R1", DefaultCapability, new string[] { "MyFeature3", "My_Feature" });
            stampAndFeatureConfig = ParseConfig(stampAndFeature);
            Assert.Equal(21, stampAndFeatureConfig.IntField);

            // Check that the feature works when a given feature has '_' in the name
            stampAndFeature = GetPreprocessor("R1_S5", "R1", DefaultCapability, new string[] { "MyFeature3", "My_Feature" });
            stampAndFeatureConfig = ParseConfig(stampAndFeature);
            Assert.Equal(21, stampAndFeatureConfig.IntField);

            var stampAndNoFeature = GetPreprocessor("R1_S1", "R1", DefaultCapability);
            TestConfig stampAndNoFeatureConfig = ParseConfig(stampAndNoFeature);
            Assert.Equal(15, stampAndNoFeatureConfig.IntField);

            var stampAndWrongFeature = GetPreprocessor("R1_S1", "R1", DefaultCapability, new string[] { "MyFeature_2" });
            TestConfig stampAndWrongFeatureConfig = ParseConfig(stampAndWrongFeature);
            Assert.Equal(15, stampAndWrongFeatureConfig.IntField);

            var wrongStampAndFeature = GetPreprocessor("R1_S1_2", "R1", DefaultCapability, new string[] { "MyFeature" });
            TestConfig wrongStampAndFeatureConfig = ParseConfig(wrongStampAndFeature);
            Assert.Equal(20, wrongStampAndFeatureConfig.IntField);
        }

        private TestConfig ParseConfig(JsonPreprocessor preprocessor)
        {
            return PreprocessConfig(TestJsonForOverrides, preprocessor);
        }

        [Fact]
        public void OverrideMultipleStampsOrRegions()
        {
            var preProc = GetPreprocessor("R2_S1", "R2", DefaultCapability);
            var actual = PreprocessConfig(TestJsonForOverrides, preProc);

            var expected = LoadBaseline();
            expected.StringField = "value.R2.r2_s1";
            expected.IntField = 18;
            expected.NestedStruct.Field2 = "done_for r2s1";

            Validate(expected, actual);
        }

        [Fact]
        public void AmbiguousStampMatch()
        {
            var preProc = GetPreprocessor("R1_S4", "R1", DefaultCapability);
            var actual = PreprocessConfig(TestJsonForOverrides, preProc);

            // Last one wins
            Assert.Equal(11.0, actual.DoubleField);
        }

        [Fact]
        public void AmbiguousRegionMatch()
        {
            var preProc = GetPreprocessor("R6_S4", "R6", DefaultCapability);
            var actual = PreprocessConfig(TestJsonForOverrides, preProc);

            // Last one wins
            Assert.Equal(4, actual.MapField2["map1.2"]);
        }

        [Fact]
        public void NotOperatorIsEvaluated()
        {
            var template = @"
{
    ""SomeProperty"": 1,
    ""SomeProperty [Constraint1:!NonExistent]"": 2
}
";
            var config = PreprocessAndDeserialize<JsonObject>(template);

            Assert.Equal(2, config["SomeProperty"]);
        }

        [Fact]
        public void ConstraintsAreTrimmed()
        {
            var template = @"
{
    ""SomeProperty"": 1,
    ""SomeProperty [ Stamp : DM_S1 ]"": 2
}
";
            var preProc = GetPreprocessor("DM_S1", "DM", DefaultCapability);
            var config = PreprocessAndDeserialize<JsonObject>(template, preProc);

            Assert.Equal(2, config["SomeProperty"]);
        }

        [Fact]
        public void ComparisonConstraints()
        {
            var currentTime = DateTime.UtcNow;

            var greaterTime = (currentTime + TimeSpan.FromSeconds(20)).ToReadableString();
            var lesserTime = (currentTime - TimeSpan.FromSeconds(20)).ToReadableString();

            var machineName = "M1";
            var fraction = DeploymentUtilities.ComputeContentHashFraction(machineName);
            fraction.Should().BeInRange(0, 1);

            var template = ("{" + $@"
            'PropA' : 'Unexpected',
            'PropA [ UtcNow > {lesserTime} ]': 'Expected',
            'PropB' : 'Unexpected',
            'PropB [ UtcNow < {greaterTime} ]': 'Expected',
            'PropC' : 'Expected',
            'PropC [ UtcNow > {greaterTime} ]': 'Unexpected',
            'PropD' : 'Expected',
            'PropD [ UtcNow < {lesserTime} ]': 'Unexpected',
            'PropE' : 'Unexpected',
            'PropE [ ServiceVersion < 0.1.0-20220624 ]': 'Expected',
            'PropF' : 'Unexpected',
            'PropF [ ServiceVersion > 0.1.0-20220621 ]': 'Expected',
            'Prop1' : 'Unexpected',
            // Less decimals to force comparand to be smaller
            'Prop1 [ MachineFraction > {fraction:f1} ]': 'Expected', 
            'Prop2' : 'Unexpected',
            // Add small value to force the comparand to be larger
            'Prop2 [ MachineFraction < {(fraction + 0.00001):f5} ]': 'Expected', 
            " + "}").Replace('\'', '"');

            var preProc = DeploymentUtilities.GetHostJsonPreprocessor(new HostParameters()
            {
                Machine = machineName,
                UtcNow = currentTime,
                ServiceVersion = "0.1.0-20220623.1025.user",
            });

            var config = PreprocessAndDeserialize<JsonObject>(template, preProc);

            foreach (var prop in config.Element.EnumerateObject())
            {
                var value = config[prop.Name];
                ((string)value).Should().Be("Expected", $"Property {value.Name} should have value 'Expected'");
            }
        }

        [Fact]
        public void HasValueConstraint()
        {
            var currentTime = DateTime.UtcNow;

            var template = ("{" + $@"
            'PropA' : 'Unexpected',
            'PropA [ Stamp.HasValue:true ]': '{{Stamp}}',
            'PropB' : 'Expected',
            'PropB [ Ring.HasValue:True ]': 'Unexpected',
            'PropC' : 'Unexpected',
            'PropC [ !Ring.HasValue:True ]': 'Expected',
            " + "}").Replace('\'', '"');

            var preProc = DeploymentUtilities.GetHostJsonPreprocessor(new HostParameters()
            {
                Stamp = "Expected",
            });

            var config = PreprocessAndDeserialize<JsonObject>(template, preProc);

            foreach (var prop in config.Element.EnumerateObject())
            {
                var value = config[prop.Name];
                ((string)value).Should().Be("Expected", $"Property {value.Name} should have value 'Expected'");
            }
        }

        [Fact]
        public void ConstraintsAreTrimmedWithOr()
        {
            var template = @"
{
    ""SomeProperty"": 1,
    ""SomeProperty [ Stamp : DM_S1 | DM_S2 ]"": 2
}
";
            var preProc = GetPreprocessor("DM_S1", "DM", DefaultCapability);
            var config = PreprocessAndDeserialize<JsonObject>(template, preProc);

            Assert.Equal(2, config["SomeProperty"]);
        }

        [Fact]
        public void ConstraintsAreTrimmedWithNegation()
        {
            var template = @"
{
    ""SomeProperty"": 1,
    ""SomeProperty [ Stamp : !DM_S2 ]"": 2
}
";
            var preProc = GetPreprocessor("DM_S1", "DM", DefaultCapability);
            var config = PreprocessAndDeserialize<JsonObject>(template, preProc);

            Assert.Equal(2, config["SomeProperty"]);
        }

        [Fact]
        public void NotOperatorIsNotEvaluatedWhenSpecified()
        {
            var template = @"
{
    ""SomeProperty"": 1,
    ""SomeProperty [Constraint1:!One]"": 2
}
";
            var config = PreprocessAndDeserialize<JsonObject>(template);

            Assert.Equal(1, config["SomeProperty"]);
        }

        [Fact]
        public void NotOperatorIsEvaluatedWhenMultipleValues()
        {
            const string Config = @"{
            'Prop': 33,
            'Prop[Feature:!TheFeature]': 3,
            }";

            var preProc = GetPreprocessor("DM_S1", "DM", DefaultCapability, features: new[] { "AnotherFeature", "TheFeature" });
            var actual = PreprocessAndDeserialize<JsonObject>(Config, preProc);

            Assert.Equal(33, actual["Prop"]);
        }

        [Fact]
        public void MacroCanBeEmpty()
        {
            var template = @"
{
    ""TheAnswerToTheUniverse"": ""4{Macro1}2"",
}
";
            var config = PreprocessAndDeserialize<JsonObject>(template);

            Assert.Equal("42", config["TheAnswerToTheUniverse"]);
        }

        private static JsonSerializerOptions s_defaultOptions = new JsonSerializerOptions()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters =
            {
                JsonUtilities.FuncJsonConverter.Create<JsonObject>(ReadJsonObject, WriteJsonObject)
            }
        };

        private static void WriteJsonObject(Utf8JsonWriter writer, JsonObject obj)
        {
            obj.Element.WriteTo(writer);
        }

        private static JsonObject ReadJsonObject(ref Utf8JsonReader reader)
        {
            var document = JsonDocument.ParseValue(ref reader);
            return new JsonObject(document.RootElement);
        }

        private readonly record struct JsonObject(JsonElement Element, string Name = null)
        {
            public JsonObject this[string name]
                => Element.TryGetProperty(name, out var property)
                ? new JsonObject(property, name)
                : default;

            public static implicit operator int(JsonObject obj)
            {
                return obj.Element.GetInt32();
            }

            public static implicit operator string(JsonObject obj)
            {
                return obj.Element.GetString();
            }

            public override string ToString()
            {
                return JsonSerializer.Serialize(this, s_defaultOptions);
            }
        }
#pragma warning disable 0649
        // unused field 
        private class TestConfig
        {
            public class SubType1
            {
                public string Field1 { get; set; }
            }

            public class SubType2
            {
                public double Field1 { get; set; }
            }

            public class SubType3
            {
                public double Field1 { get; set; }
                public string Field2 { get; set; }
                public int Field3 { get; set; }
            }

            public string StringField { get; set; }
            public double DoubleField { get; set; }
            public int IntField { get; set; }
            public string OptionalField { get; set; }
            public string PrecedenceField { get; set; }
            public string[] ArrayField1 { get; set; }
            public SubType1[] ArrayField2 { get; set; }

            public Dictionary<string, string> MapField1 { get; set; }
            public Dictionary<string, int> MapField2 { get; set; }
            public Dictionary<string, SubType2> MapField3 { get; set; }

            public SubType3 NestedStruct { get; set; }

            public string NullField { get; set; }
        }

        private TestConfig LoadBaseline()
        {
            var preProc = GetPreprocessor("DefaultStamp", "DefaultRegion", DefaultCapability);

            return PreprocessConfig(TestJsonForOverrides, preProc);
        }

        private string Preprocess(
            string json,
            JsonPreprocessor preprocessor = null)
        {
            json = json.Replace("'", "\"");
            preprocessor ??= GetDefaultTestPreprocessor();
            var preprocessedJson = preprocessor.Preprocess(json);
            return preprocessedJson;
        }

        private T PreprocessAndDeserialize<T>(
            string json,
            JsonPreprocessor preprocessor = null)
        {
            return Deserialize<T>(Preprocess(json, preprocessor));
        }

        private T Deserialize<T>(string json)
        {
            json = json.Replace("'", "\"");
            return JsonSerializer.Deserialize<T>(json, s_defaultOptions);
        }

        private TestConfig PreprocessConfig(
            string json,
            JsonPreprocessor preprocessor = null)
        {
            return PreprocessAndDeserialize<TestConfig>(json, preprocessor);
        }

        private static JsonPreprocessor GetDefaultTestPreprocessor()
        {
            return new JsonPreprocessor(
                new[]
                {
                    new ConstraintDefinition("Constraint1", new[] { "One" }),
                },
                new Dictionary<string, string>()
                {
                    { "Macro1", "" }
                });
        }

        private static void Validate(TestConfig expected, TestConfig actual)
        {
            string strExpected = JsonUtilities.JsonSerialize(expected);
            string strActual = JsonUtilities.JsonSerialize(actual);

            Assert.Equal(strExpected, strActual, ignoreCase: false);
        }

        private JsonPreprocessor GetPreprocessor(
            string stampId,
            string regionId,
            IEnumerable<string> capabilities,
            IEnumerable<string> features = null,
            string ringId = null)
        {
            return DeploymentUtilities.GetHostJsonPreprocessor(new HostParameters()
            {
                Stamp = stampId,
                Region = regionId,
                Ring = ringId,
                Flags = new Dictionary<string, string[]>()
                {
                    { "Feature", features?.ToArray() },
                    { "Capability", capabilities?.ToArray() }
                }
            });
        }
    }
}