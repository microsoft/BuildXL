// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace BuildXL.ViewModel
{
    /// <summary>
    /// Stores a recursive structure of perf measurements
    /// </summary>
    /// <remarks>
    /// This class has an Add method and implements IEnumerable so that we can use object literal construction of PerfTree's.
    /// </remarks>
    public class PerfTree : IEnumerable<PerfTree>
    {
        /// <nodoc />
        public PerfTree(string title, TimeSpan duration)
        {
            Title = title;
            Duration = duration;
        }

        /// <nodoc />
        public PerfTree(string title, long durationMs)
            : this(title, TimeSpan.FromMilliseconds(durationMs))
        {
        }

        /// <summary>
        /// The duration of the current step
        /// </summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// The title of the current step
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// The nested child pref trees
        /// </summary>
        public List<PerfTree> NestedItems { get; } = new List<PerfTree>();

        /// <summary>
        /// Add method for overload literal support
        /// </summary>
        public void Add(PerfTree item)
        {
            NestedItems.Add(item);
        }

        /// <inheritdoc />
        public IEnumerator<PerfTree> GetEnumerator()
        {
            return NestedItems.GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Prints indented tree view
        /// </summary>
        public void Write(StringBuilder builder, int indent, TimeSpan outer)
        {
            AddLine(builder, indent, Title, Duration, outer);

            indent++;

            var remaining = Duration;
            if (NestedItems.Count > 0)
            {
                foreach (var item in NestedItems)
                {
                    item.Write(builder, indent, Duration);
                    remaining -= item.Duration;
                }

                if (remaining.TotalMilliseconds > 0)
                {
                    AddLine(builder, indent, "Other", remaining, outer);
                }
            }
        }

        /// <summary>
        /// Helper to print a single line with fixed spacing, the time it took to run and the relative percentage compared to the parent
        /// </summary>
        private void AddLine(StringBuilder builder, int indent, string title, TimeSpan duration, TimeSpan parentDuration)
        {
            int percent = 0;
            if (parentDuration.TotalMilliseconds > 0)
            {
                percent = (int)(100.0 * duration.TotalMilliseconds / parentDuration.TotalMilliseconds);
            }

            builder.AppendLine($"{new string(' ', indent * 4)}{title,-40} {duration.MakeFriendly(),-20} {percent,3}%");
        }
    }
}
