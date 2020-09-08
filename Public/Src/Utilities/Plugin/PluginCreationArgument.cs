using System;

namespace BuildXL.Plugin
{
    /// <summary>
    /// Class of contains the configuration for creating a plugin
    /// </summary>
    public class PluginCreationArgument
    {
        /// <summary>
        /// Plugin path
        /// </summary>
        public string PluginPath { get; set; }

        /// <summary>
        /// Whether run the plugin in a process or thread
        /// </summary>
        /// <remarks>
        /// Default is to run in process, run in thread is used for test purpose
        /// </remarks>
        public bool RunInSeparateProcess { get; set; } = true;

        /// <summary>
        /// Plugin Id
        /// </summary>
        public string PluginId { get; set; }

        /// <summary>
        /// Plugin Connection <see cref="PluginConnectionOption" />
        /// </summary>
        public PluginConnectionOption ConnectionOption { get; set; }

        /// <summary>
        /// Function to create a plugin based on <see cref="PluginConnectionOption" />
        /// </summary>
        public Func<PluginConnectionOption, IPluginClient> CreatePluginClientFunc { get; set; }

        /// <summary>
        /// Action to start plugin in a thread
        /// </summary>
        public Action RunInPluginThreadAction { get; set; }
    }
}
