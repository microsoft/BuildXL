// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class Mount : TrackedValue, IMount
    {
        /// <nodoc />
        public Mount()
        {
        }

        /// <nodoc />
        public Mount(IMount template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            Name = pathRemapper.Remap(template.Name);
            Path = pathRemapper.Remap(template.Path);
            TrackSourceFileChanges = template.TrackSourceFileChanges;
            IsWritable = template.IsWritable;
            IsReadable = template.IsReadable;
            IsSystem = template.IsSystem;
            IsScrubbable = template.IsScrubbable;
            AllowCreateDirectory = template.AllowCreateDirectory;
            IsStatic = template.IsStatic;
        }

        /// <inheritdoc />
        public PathAtom Name { get; set; }

        /// <inheritdoc />
        public AbsolutePath Path { get; set; }

        /// <inheritdoc />
        public bool TrackSourceFileChanges { get; set; }

        /// <inheritdoc />
        public bool IsWritable { get; set; }

        /// <inheritdoc />
        public bool IsReadable { get; set; }

        /// <inheritdoc />
        public bool IsSystem { get; set; }

        /// <inheritdoc />
        public bool IsStatic { get; set; }

        /// <inheritdoc />
        public bool IsScrubbable { get; set; }

        /// <inheritdoc />
        public bool AllowCreateDirectory { get; set; }
    }
}
