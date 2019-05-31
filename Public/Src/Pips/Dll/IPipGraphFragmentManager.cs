using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Pips
{
    /// <summary>
    /// Manager to add binary pip fragments to a pip graph builder
    /// </summary>
    public interface IPipGraphFragmentManager
    {
        /// <summary>
        /// Add a pip graph fragment file to the graph.
        /// </summary>
        /// <param name="fragmentName">Name of the fragment</param>
        /// <param name="filePath">Path to the file to read.</param>
        /// <param name="dependencyNames">Name to the pip fragments this fragment depends on.</param>
        Task<bool> AddFragmentFileToGraph(string fragmentName, AbsolutePath filePath, string[] dependencyNames);

        /// <summary>
        /// Get a list of (fragment description, fragment load task)
        /// </summary>
        IReadOnlyCollection<(PipGraphFragmentSerializer, Task<bool>)> GetAllFragmentTasks();
    }
}