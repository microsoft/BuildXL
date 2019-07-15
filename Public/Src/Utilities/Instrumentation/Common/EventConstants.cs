using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    ///  Event constants
    /// </summary>
    public static class EventConstants
    {
        /// <summary>
        /// Prefix used to indicate the provenance of an error for the user's benefit.
        /// </summary>
        /// <remarks>
        /// Please realize that changing this affects a lot of methods in this class. If you have to, please validate all consumers
        /// in this class.
        /// </remarks>
        public const string ProvenancePrefix = "{0}({1},{2}): ";

        /// <summary>
        /// Prefix used to indicate the provenance of an error for the user's benefit using named arguments rather than positional.
        /// </summary>
        /// <remarks>
        /// Must add <pre>Location location</pre> argument to the logger to use this.
        /// </remarks>
        public const string LabeledProvenancePrefix = "{location.File}({location.Line},{location.Position}): ";

        /// <summary>
        /// Prefix used to indicate a pip.
        /// </summary>
        /// <remarks>
        /// Please realize that changing this affects a lot of methods in this class. If you have to, please validate all consumers
        /// in this class.
        /// </remarks>
        public const string PipPrefix = "[{1}] ";

        /// <summary>
        /// Prefix used to indicate dependency analysis results specific to a pip.
        /// </summary>
        /// <remarks>
        /// Why this extra prefix? Text filtering. There's a corresponding ETW keyword, but today people lean mostly on the text logs.
        /// </remarks>
        public const string PipDependencyAnalysisPrefix = "Detected dependency violation: [{1}] ";

        /// <summary>
        /// Prefix used to indicate a pip, the spec file that generated it, and the working directory
        /// </summary>
        /// <remarks>
        /// Please realize that changing this affects a lot of methods in this class. If you have to, please validate all consumers
        /// in this class.
        /// </remarks>
        public const string PipSpecPrefix = "[{1}, {2}, {3}]";

        /// <summary>
        /// Prefix used to indicate dependency analysis results specific to a pip, the spec file that generated it, and the working directory
        /// </summary>
        /// <remarks>
        /// Why this extra prefix? Text filtering. There's a corresponding ETW keyword, but today people lean mostly on the text logs.
        /// </remarks>
        public const string PipSpecDependencyAnalysisPrefix = "Detected dependency violation: [{1}, {2}, {3}] ";

        /// <summary>
        /// Prefix used to indicate phases.
        /// </summary>
        public const string PhasePrefix = "-- ";

        /// <summary>
        /// Prefix used to indicate that artifacts (files or directories) or pips have changed (or have become dirty).
        /// </summary>
        /// <remarks>
        /// This prefix is used mostly for incremental scheduling logs.
        /// </remarks>
        public const string ArtifactOrPipChangePrefix = ">>> ";

        /// <summary>
        /// Suffix added to the PipProcessError log when the process finished successfully but did not produced all required outputs.
        /// </summary>
        public const string PipProcessErrorMissingOutputsSuffix = "; required output is missing";
    }
}
