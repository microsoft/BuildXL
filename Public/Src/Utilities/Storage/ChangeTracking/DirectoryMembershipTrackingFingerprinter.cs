// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Directory membership fingerprinter for tracking purpose.
    /// </summary>
    public static class DirectoryMembershipTrackingFingerprinter
    {
        /// <summary>
        /// Result of computing directory membership fingerprint.
        /// </summary>
        public readonly struct DirectoryMembershipFingerprintResult
        {
            /// <summary>
            /// The resulting fingerprint.
            /// </summary>
            public readonly DirectoryMembershipTrackingFingerprint Fingerprint;

            /// <summary>
            /// Path existence.
            /// </summary>
            public readonly PathExistence PathExistence;

            /// <summary>
            /// Number of members.
            /// </summary>
            public readonly int MemberCount;

            /// <summary>
            /// Creates an instance of <see cref="DirectoryMembershipFingerprintResult"/>.
            /// </summary>
            public DirectoryMembershipFingerprintResult(
                DirectoryMembershipTrackingFingerprint fingerprint,
                PathExistence pathExistence,
                int memberCount)
            {
                Fingerprint = fingerprint;
                PathExistence = pathExistence;
                MemberCount = memberCount;
            }
        }

        /// <summary>
        /// Computes fingerprint.
        /// </summary>
        public static Possible<DirectoryMembershipFingerprintResult> ComputeFingerprint(
            string path,
            Action<string, FileAttributes> handleEntry = null)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(path));

            var calculator = DirectoryMembershipTrackingFingerprint.CreateCalculator();

            int memberCount = 0;
            EnumerateDirectoryResult enumResult = FileUtilities.EnumerateDirectoryEntries(
                path,
                (entryName, entryAttributes) =>
                {
                    calculator = calculator.Accumulate(entryName, entryAttributes);
                    memberCount++;
                    handleEntry?.Invoke(entryName, entryAttributes);
                });

            DirectoryMembershipTrackingFingerprint fingerprint = calculator.GetFingerprint();
            var existence = PathExistence.Nonexistent;
            bool enumerationSucceeded = true;

            switch (enumResult.Status)
            {
                case EnumerateDirectoryStatus.Success:
                    existence = PathExistence.ExistsAsDirectory;
                    break;
                case EnumerateDirectoryStatus.CannotEnumerateFile:
                    // We return an empty fingerprint for a path without children, even if that is because
                    // the directory path was to a 'leaf' node (file).
                    Contract.Assume(fingerprint == DirectoryMembershipTrackingFingerprint.Zero);
                    existence = PathExistence.ExistsAsFile;
                    break;
                case EnumerateDirectoryStatus.SearchDirectoryNotFound:
                    // The directory is expected to exist because we have a handle.
                    // But that handle may be for a symlink to a non-existent directory.
                    // Thus, directory enumeration results in directory not found.
                    var possibleExistence = HandleSearchDirectoryNotFoundOnDirectoryEnumerationForFingerprinting(path);
                    if (possibleExistence.Succeeded)
                    {
                        existence = possibleExistence.Result;
                    }

                    fingerprint = DirectoryMembershipTrackingFingerprint.Absent;
                    break;

                case EnumerateDirectoryStatus.AccessDenied:
                case EnumerateDirectoryStatus.UnknownError:
                    enumerationSucceeded = false;
                    break;
                default:
                    throw Contract.AssertFailure("Unhandled EnumerateDirectoryStatus");
            }

            if (!enumerationSucceeded)
            {
                return new Failure<EnumerateDirectoryStatus>(enumResult.Status).Annotate(
                    "Failed to enumerate a directory (unexpected since a read-access handle to it was just opened; was there a rename?)");
            }

            return new DirectoryMembershipFingerprintResult(fingerprint, existence, memberCount);
        }

        private static Possible<PathExistence> HandleSearchDirectoryNotFoundOnDirectoryEnumerationForFingerprinting(string expandedPath)
        {
            var openFlags = FileUtilities.GetFileFlagsAndAttributesForPossibleReparsePoint(expandedPath);

            SafeFileHandle handle;
            OpenFileResult openResult = FileUtilities.TryOpenDirectory(
                expandedPath,
                FileDesiredAccess.GenericRead,
                FileShare.ReadWrite | FileShare.Delete,
                openFlags,
                out handle);

            if (!openResult.Succeeded)
            {
                // Directory may have been renamed.
                return new Failure<PathExistence>(PathExistence.Nonexistent);
            }

            Contract.Assert(handle != null && !handle.IsInvalid);
            handle.Dispose();

            if ((openFlags & FileFlagsAndAttributes.FileFlagOpenReparsePoint) != 0)
            {
                // Path may be a symlink to a non-existent directory. In this case, we should not fail fingerprinting.
                return PathExistence.Nonexistent;
            }

            // Unknown case.
            return new Failure<PathExistence>(PathExistence.Nonexistent);
        }

        /// <summary>
        /// Compute directory membership fingerprint given its members.
        /// </summary>
        public static DirectoryMembershipTrackingFingerprint ComputeFingerprint(IReadOnlyList<(string, FileAttributes)> members)
        {
            var calculator = DirectoryMembershipTrackingFingerprint.CreateCalculator();

            foreach (var valueTuple in members.AsStructEnumerable())
            {
                calculator = calculator.Accumulate(valueTuple.Item1, valueTuple.Item2);
            }

            return calculator.GetFingerprint();
        }
    }
}
