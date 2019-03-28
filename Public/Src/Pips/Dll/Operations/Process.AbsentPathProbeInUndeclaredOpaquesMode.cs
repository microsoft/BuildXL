namespace BuildXL.Pips.Operations
{
    /// <nodoc />
    public partial class Process
    {
        /// <summary>
        /// Mode setting controlling how pips should deal with absent path probes under opaque directories they do not take a dependency on. This is a per pip setting.
        /// </summary>
        /// <remarks>
        /// When a pip is skipped incrementally, FileMonitoringViolationAnalyzer might fail to detect WriteOnAbsentPathProbe violation.
        /// It is because we add file accesses to DFA analyzer only at two places --- after a pip has finished OR when we replay a pip from cache.
        /// Safe modes (Strict and Relaxed) prevent this from happening.
        ///
        /// If a pip produces shared opaques, it will be automatically marked dirty. For such pips, there is no difference
        /// between Unsafe and Relaxed modes.
        ///
        /// Only Strict mode affects cacheability of a pip (by nature of triggering a DFA error). Absent path probes alone are not that dangerous,
        /// and such probes do not affect cacheability when the probes happen outside of opaque directories, so treating them differently here
        /// dose not make much sense.
        ///
        /// This setting does not directly affect WriteOnAbsentPathProbe DFA analysis. Safe modes just ensure that absent file
        /// probes are correctly reported to the analyzer.
        ///  
        ///                 | Unsafe | Relaxed | Strict
        /// Cacheability    | Yes    | Yes     | n/a
        /// Inc. scheduling | Yes    | No      | n/a
        /// Verbose message | Yes    | Yes     | Yes
        /// DFA (DX500)     | n/a    | n/a     | Yes
        /// </remarks>
        public enum AbsentPathProbeInUndeclaredOpaquesMode : byte
        {
            /// <summary>
            /// The least invasive option; does not alter how we treat absent path probes under opaque directories.
            /// Potentially unsafe (incrementally skipped pips might lead to inconsistent DFAs). Default value for now.
            /// </summary>
            Unsafe = 0,

            /// <summary>
            /// If a pip probes an absent path under an opaque directory that this pip does not take dependency on,
            /// we emit a DFA and fail the pip.
            /// </summary>
            Strict = 1,

            /// <summary>
            /// If a pip probes an absent path under an opaque directory that this pip does not take dependency on,
            /// the pip marked as dirty.
            /// </summary>
            Relaxed = 2,
        }
    }
}