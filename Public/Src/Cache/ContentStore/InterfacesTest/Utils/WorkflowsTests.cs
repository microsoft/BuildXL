// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Utils
{
    public class WorkflowsTests
    {
        [Fact]
        public void TestRunWithThreeFallbacks()
        {
            List<ContentHashWithPath> listFile = new List<ContentHashWithPath>();

            string[] dirs = new string[3] {"temp", "CloudStore", "634ec8cad5be"};

            ContentHashWithPath contentHash1 = new ContentHashWithPath(new ContentHash("MD5:72F6F256239CC69B6FE9AF1C7489CFD1"), new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("A", dirs)) / "destination1.txt");
            listFile.Add(contentHash1);

            ContentHashWithPath contentHash2 = new ContentHashWithPath(new ContentHash("MD5:72F6F256239CC69B6FE9AF1C7489CFD2"), new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("A", dirs)) / "destination2.txt");
            listFile.Add(contentHash2);

            ContentHashWithPath contentHash3 = new ContentHashWithPath(new ContentHash("MD5:72F6F256239CC69B6FE9AF1C7489CFD3"), new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("A", dirs)) / "destination3.txt");
            listFile.Add(contentHash3);

            ContentHashWithPath contentHash4 = new ContentHashWithPath(new ContentHash("MD5:72F6F256239CC69B6FE9AF1C7489CFD4"), new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("A", dirs)) / "destination4.txt");
            listFile.Add(contentHash4);

            IEnumerable<Task<Indexed<PlaceFileResult>>> result = Workflows.RunWithFallback<ContentHashWithPath, PlaceFileResult>(
                listFile,
                initialFunc: args =>
                {
                    Assert.Equal(args.Count, 4);
                    Assert.Equal(args[0], contentHash1);
                    Assert.Equal(args[1], contentHash2);
                    Assert.Equal(args[2], contentHash3);
                    Assert.Equal(args[3], contentHash4);

                    return Task.FromResult(args.AsIndexed().Select(
                        p =>
                        {
                            // Only number 2 placed successfully
                            if (p.Index == 1)
                            {
                                return Task.FromResult(new Indexed<PlaceFileResult>(
                                    new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy),
                                    p.Index));
                            }
                            return Task.FromResult(new Indexed<PlaceFileResult>(
                                new PlaceFileResult(PlaceFileResult.ResultCode.Error, "ERROR"),
                                p.Index));
                        }));
                },
                fallbackFunc: args =>
                {
                    // First fallback should only receive 3 hashes
                    Assert.Equal(args.Count, 3);
                    Assert.Equal(args[0], contentHash1);
                    Assert.Equal(args[1], contentHash3);
                    Assert.Equal(args[2], contentHash4);

                    return Task.FromResult(args.AsIndexed().Select(
                        p =>
                        {
                            // Only number 3 placed successfully
                            if (p.Index == 1)
                            {
                                return Task.FromResult(new Indexed<PlaceFileResult>(
                                    new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy),
                                    p.Index));
                            }
                            return Task.FromResult(new Indexed<PlaceFileResult>(
                                new PlaceFileResult(PlaceFileResult.ResultCode.Error, "ERROR"),
                                p.Index));
                        }));
                },
                secondFallbackFunc: args =>
                {
                    // Second fallback should only receive 2 hashes
                    Assert.Equal(args.Count, 2);
                    Assert.Equal(args[0], contentHash1);
                    Assert.Equal(args[1], contentHash4);

                    return Task.FromResult(args.AsIndexed().Select(
                        p =>
                        {
                            return Task.FromResult(new Indexed<PlaceFileResult>(
                                new PlaceFileResult(PlaceFileResult.ResultCode.Error, "ERROR"),
                                p.Index));
                        }));
                },
                thirdFallbackFunc: args =>
                {
                    // Second fallback should only receive 2 hashes
                    Assert.Equal(args.Count, 2);
                    Assert.Equal(args[0], contentHash1);
                    Assert.Equal(args[1], contentHash4);

                    return Task.FromResult(args.AsIndexed().Select(
                        p =>
                        {
                            // Only number 4 placed successfully
                            if (p.Index == 1)
                            {
                                return Task.FromResult(new Indexed<PlaceFileResult>(
                                    new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy),
                                    p.Index));
                            }
                            return Task.FromResult(new Indexed<PlaceFileResult>(
                                new PlaceFileResult(PlaceFileResult.ResultCode.Error, "ERROR"),
                                p.Index));
                        }));
                },
                arg =>
                {
                    return arg.Succeeded;
                }).Result;

            Assert.Equal(result.ToList().Count, 4);

            result.ToList().ForEach(p => {
                if (p.Result.Index == 0) {
                    Assert.Equal(p.Result.Item.Code, PlaceFileResult.ResultCode.Error);
                } else {
                    Assert.Equal(p.Result.Item.Code, PlaceFileResult.ResultCode.PlacedWithCopy);
                }
            });
        }

    }
}
