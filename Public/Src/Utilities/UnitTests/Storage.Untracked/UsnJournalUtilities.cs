// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;

namespace Test.BuildXL.Storage.Admin
{
    public static class UsnJournalUtilities
    {
        public static UsnChangeReasons GetAggregateChangeReasons(
            FileId fileFilter,
            IEnumerable<UsnRecord> records)
        {
            UsnChangeReasons reasons = default(UsnChangeReasons);
            foreach (UsnRecord record in records)
            {
                if (fileFilter == record.FileId)
                {
                    reasons |= record.Reason;
                }
            }

            return reasons;
        }
    }
}
