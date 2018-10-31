// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Utilities;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class FileEnvelopeTests
    {
        [Fact]
        public void Success()
        {
            var fe = new FileEnvelope("Dummy", 0);
            using (var stream = new MemoryStream())
            {
                FileEnvelopeId id = FileEnvelopeId.Create();
                fe.WriteHeader(stream, id);
                fe.FixUpHeader(stream, id);

                stream.Position = 0;
                fe.ReadHeader(stream);
            }
        }

        [Fact]
        public void MissingFixup()
        {
            var fe = new FileEnvelope("Dummy", 0);
            using (var stream = new MemoryStream())
            {
                FileEnvelopeId id = FileEnvelopeId.Create();
                fe.WriteHeader(stream, id);

                // fe.FixUpHeader(stream, id);
                stream.Position = 0;
                Assert.Throws<BuildXLException>(
                    () => { fe.ReadHeader(stream); });
            }
        }

        [Fact]
        public void WrongEnvelopeName()
        {
            var fe0 = new FileEnvelope("Dummy0", 0);
            var fe1 = new FileEnvelope("Dummy1", 0);
            using (var stream = new MemoryStream())
            {
                FileEnvelopeId id = FileEnvelopeId.Create();
                fe0.WriteHeader(stream, id);
                fe0.FixUpHeader(stream, id);

                stream.Position = 0;
                Assert.Throws<BuildXLException>(
                    () => { fe1.ReadHeader(stream); });
            }
        }

        [Fact]
        public void WrongEnvelopeVersion()
        {
            var fe0 = new FileEnvelope("Dummy0", 0);
            var fe1 = new FileEnvelope("Dummy0", 1);
            using (var stream = new MemoryStream())
            {
                FileEnvelopeId id = FileEnvelopeId.Create();
                fe0.WriteHeader(stream, id);
                fe0.FixUpHeader(stream, id);

                stream.Position = 0;
                Assert.Throws<BuildXLException>(
                    () => { fe1.ReadHeader(stream); });
            }
        }

        [Fact]
        public void WrongCorrelationId()
        {
            FileEnvelopeId fe0 = FileEnvelopeId.Create();
            FileEnvelopeId fe1 = FileEnvelopeId.Create();
            Assert.Throws<BuildXLException>(
                () => { FileEnvelope.CheckCorrelationIds(fe0, fe1); });
        }

        [Fact]
        public void DetectHeaderCorruption()
        {
            var r = new Random(0);
            var fe = new FileEnvelope("Dummy", 0);
            for (int i = 0; i < 10000; i++)
            {
                using (var stream = new MemoryStream())
                {
                    FileEnvelopeId id = FileEnvelopeId.Create();
                    fe.WriteHeader(stream, id);
                    fe.FixUpHeader(stream, id);

                    stream.Position = r.Next((int)stream.Length - 1);
                    int b = stream.ReadByte();
                    stream.Position = stream.Position - 1;
                    stream.WriteByte((byte)(b ^ (1 << r.Next(8))));

                    stream.Position = 0;
                    Assert.Throws<BuildXLException>(
                        () => { fe.ReadHeader(stream); });
                }
            }
        }

        [Fact]
        public void DetectFileLengthCorruption()
        {
            var fe = new FileEnvelope("Dummy", 0);
            using (var stream = new MemoryStream())
            {
                FileEnvelopeId id = FileEnvelopeId.Create();
                fe.WriteHeader(stream, id);
                fe.FixUpHeader(stream, id);

                stream.WriteByte(0); // not taken into account in fixed up header magic

                stream.Position = 0;
                Assert.Throws<BuildXLException>(
                    () => { fe.ReadHeader(stream); });
            }
        }
    }
}
