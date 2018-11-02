
namespace BuildXL.Utilities
{
    /// <summary>
    /// Contains meta-information about BuildXL.
    /// </summary>
    public static class Branding
    {
        /// <summary>
        /// Returns the current BuildXL version set by a build agent.
        /// </summary>
        public static string Version => "0.1.0.0";

        /// <summary>
        /// Returns commit Id of the current BuildXL version.
        /// </summary>
        public static string SourceVersion => "Dev build";

        /// <summary>
        /// Returns the long product name for use in printing the full brand.
        /// </summary>
        public static string LongProductName => "Microsoft (R) Build Accelerator";

        /// <summary>
        /// Returns the short product name for use in informal contexts, e.g. help text,
        /// where the full, long brand name is not required.
        /// </summary>
        public static string ShortProductName => "BuildXL";
    }
}