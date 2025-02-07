// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

#nullable enable

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    public class ChangeFeedContinuationToken
    {
        public required int CursorVersion { get; init; }

        public required string UrlHost { get; init; }

        public required DateTime? EndTime { get; init; }

        public required CurrentSegmentCursor CurrentSegmentCursor { get; init; }
    }

    public class CurrentSegmentCursor
    {
        public required List<ShardCursor> ShardCursors { get; init; }

        public required string CurrentShardPath { get; init; }

        public required string SegmentPath { get; init; }

        public DateTime? SegmentDate => ParseSegmentDate(SegmentPath);

        private DateTime? ParseSegmentDate(string segmentPath)
        {
            if (string.IsNullOrEmpty(segmentPath))
            {
                return null;
            }

            var match = Regex.Match(segmentPath, @"(\d{4})/(\d{2})/(\d{2})/(\d{4})");
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int year) &&
                    int.TryParse(match.Groups[2].Value, out int month) &&
                    int.TryParse(match.Groups[3].Value, out int day))
                {
                    return new DateTime(year, month, day);
                }
            }

            return null;
        }
    }

    public class ShardCursor
    {
        public required string CurrentChunkPath { get; init; }

        public required long BlockOffset { get; init; }

        public required int EventIndex { get; init; }
    }
}
