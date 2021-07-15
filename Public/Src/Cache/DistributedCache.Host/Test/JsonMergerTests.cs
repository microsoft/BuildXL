// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.Serialization;
using System.Text.Json;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using FluentAssertions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Host.Test
{
    /// <summary>
    /// Set of tests to prove that json serialization/deserialization works for Json.NET and for System.Text.Json
    /// </summary>
    public class JsonMergerTests
    {
        [Fact]
        public void BasicMerge()
        {
            TestHelper(baseJson: @"
                {
                    'a': { 'b': { 'c': { 'd': {
                        'p1': false,
                        'p2': 42,
                        'p3': {
                            'p1': 'deep nested base'
                        }
                    }}}},
                    'obj1' : {
                        'prop1' : 'value1',
                        'prop2' :  {
                            'nestedProp1' : true,
                            'nestedProp2' : 123,
                            'nestedProp3' : null,
                        },
                        'prop3' : 0,
                        'prop4' : [],
                    },
                    'obj2' : {
                        'prop1' : 'value1',
                        'prop2' :  {
                            'nestedProp1' : true,
                            'nestedProp2' : 123,
                            'nestedProp3' : null,
                        },
                        'prop3' : 0,
                        'prop4' : [],
                    },
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
                    }
                }", overlayJson:
@"
                {
                    'newprop': 'this will end up at end',
                    'a': { 'b': { 'c': { 'd': {
                        'p1': true,
                        'p3': {
                            'p1': 'deep nested overlaid'
                        }
                    }}}},
                    'obj1' : {
                        'prop1' : 'value1',
                        'prop2' :  {
                            'nestedProp2' : 47,
                        },
                        'prop3' : 0,
                        'prop4' : { },
                    },
                    'obj2' : {
                        'prop2' :  {
                            'nestedProp1' : [5, 4, 2],
                        },
                    },
                    'ArrayField1' : null,

                    'ArrayField2' : [
                        { 'NewField1' : 'newfoo' }, 
                    ],

                    'MapField1' : {
                        'map1.1' : { 'hello': 'world' },
                    },
                }", expectedJson:
@"
                {
                    'a': { 'b': { 'c': { 'd': {
                        'p1': true,
                        'p2': 42,
                        'p3': {
                            'p1': 'deep nested overlaid'
                        }
                    }}}},
                    'obj1' : {
                        'prop1' : 'value1',
                        'prop2' :  {
                            'nestedProp1' : true,
                            'nestedProp2' : 47,
                            'nestedProp3' : null
                        },
                        'prop3' : 0,
                        'prop4' : { }
                    },
                    'obj2' : {
                        'prop1' : 'value1',
                        'prop2' :  {
                            'nestedProp1' : [5, 4, 2],
                            'nestedProp2' : 123,
                            'nestedProp3' : null
                        },
                        'prop3' : 0,
                        'prop4' : []
                    },
                    'ArrayField1' : null,

                    'ArrayField2' : [
                        { 'NewField1' : 'newfoo' } 
                    ],

                    'MapField1' : {
                        'map1.1' : { 'hello': 'world' },
                        'map1.2' : 'map1.two'
                    },
                    'newprop': 'this will end up at end'
                }");
        }

        public static void TestHelper(string baseJson, string overlayJson, string expectedJson)
        {
            baseJson = baseJson.Replace("'", "\"");
            overlayJson = overlayJson.Replace("'", "\"");
            expectedJson = expectedJson.Replace("'", "\"");
            var resultJson = JsonMerger.Merge(baseJson, overlayJson);
            XAssert.EqualIgnoreWhiteSpace(expectedJson, resultJson);
        }

    }
}
