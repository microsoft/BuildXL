// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Storage
{
    public sealed class NativeUnitTests
    {
        [Fact]
        public void UsnRecordEquality()
        {
            var baseValue = new UsnRecord(new FileId(123, 456), new FileId(123, 457), new Usn(789), UsnChangeReasons.DataExtend);
            StructTester.TestEquality(
                baseValue: baseValue,
                equalValue: baseValue,
                notEqualValues: new[]
                                {
                                    new UsnRecord(new FileId(124, 456), new FileId(123, 457), new Usn(789), UsnChangeReasons.DataExtend),
                                    new UsnRecord(new FileId(123, 457), new FileId(123, 457), new Usn(789), UsnChangeReasons.DataExtend),
                                    new UsnRecord(new FileId(123, 456), new FileId(123, 457), new Usn(790), UsnChangeReasons.DataExtend),
                                    new UsnRecord(new FileId(123, 456), new FileId(123, 457), new Usn(790), UsnChangeReasons.DataOverwrite),
                                    new UsnRecord(new FileId(123, 456), new FileId(123, 458), new Usn(789), UsnChangeReasons.DataExtend)
                                },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b,
                skipHashCodeForNotEqualValues: false);
        }

        [Fact]
        public void MiniUsnRecordEquality()
        {
            var baseValue = new MiniUsnRecord(new FileId(123, 456), new Usn(789));
            StructTester.TestEquality(
                baseValue: baseValue,
                equalValue: baseValue,
                notEqualValues: new[]
                                {
                                    new MiniUsnRecord(new FileId(123, 456), new Usn(790)),
                                    new MiniUsnRecord(new FileId(123, 457), new Usn(789)),
                                },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b,
                skipHashCodeForNotEqualValues: false);
        }

        [Fact]
        public void FileIdEquality()
        {
            var baseValue = new FileId(123, 456);
            StructTester.TestEquality(
                baseValue: baseValue,
                equalValue: baseValue,
                notEqualValues: new[]
                                {
                                    new FileId(123, 457),
                                    new FileId(124, 456),
                                },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b,
                skipHashCodeForNotEqualValues: false);
        }

        [Fact]
        public void FileIdAndVolumeIdEquality()
        {
            var baseValue = new FileIdAndVolumeId(789, new FileId(123, 456));
            StructTester.TestEquality(
                baseValue: baseValue,
                equalValue: baseValue,
                notEqualValues: new[]
                                {
                                    new FileIdAndVolumeId(790, new FileId(123, 456)),
                                    new FileIdAndVolumeId(789, new FileId(124, 456)),
                                    new FileIdAndVolumeId(789, new FileId(123, 457)),
                                },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b,
                skipHashCodeForNotEqualValues: false);
        }
    }
}
