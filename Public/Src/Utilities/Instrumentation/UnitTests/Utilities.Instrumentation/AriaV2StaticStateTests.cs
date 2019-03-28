// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Instrumentation.Common;
using Xunit;

namespace Test.BuildXL.Utilities.Instrumentation
{
    public class AriaV2StaticStateTests
    {
        [Theory]
        [InlineData("Abc.Abc.Abc.Abc", "Ab_Ab_Ab_Abc", 12)]
        [InlineData("Abcde.Abc.Abc.Abc", "Abc_A_Ab_Abc", 12)]
        [InlineData("A.A.Abcef.Abcef", "A_A_Ab_Abcef", 12)]
        [InlineData("A.D:\\.Abcef.Abcef", "A_D_Ab_Abcef", 12)]
        [InlineData("AbcefAbcefAbcefAbcefAbcef", "t__bcefAbcef", 12)]
        [InlineData("a........................b", "a_b", 12)]
        [InlineData("a.b.c.d.e.f.g.h.i.j.k.l.m.n.o.p.q.r.s.t.u.v.w.x.b", "t__u_v_w_x_b", 12)]
        [InlineData("dmnoLocalService.ContentServer.DefaultDistributedContentStore.1.D:\\.FileSystemContentStore.GetStatsCallCount",
            "dmnoLocalServi_ContentServ_DefaultDistributedContentStor_1_D_FileSystemContentStor_GetStatsCallCount", 100)]
        public void LongNameIsShortened(string name, string expected, int maxLength)
        {
            string result = AriaV2StaticState.ScrubEventProperty(name, maxLength);
            Assert.Equal(expected, result);
        }
    }
}
