using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of a Distributed Delete call
    /// </summary>
    public class DistributedDeleteResult : DeleteResult
    {
        /// <summary>
        /// Mapping from a machine location to its delete result
        /// </summary>
        public Dictionary<string, DeleteResult> DeleteMapping { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DistributedDeleteResult"/> class.
        /// </summary>
        public DistributedDeleteResult(ContentHash hash, long contentSize, Dictionary<string, DeleteResult> deleteMapping)
            : base(hash, contentSize)
        {
            DeleteMapping = deleteMapping;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DistributedDeleteResult"/> class.
        /// </summary>
        public DistributedDeleteResult(ResultBase other, string message)
            : base(other, message)
        {
            DeleteMapping = new Dictionary<string, DeleteResult>();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var contentSizeTrace = ContentSize == 0 ? $"Content size could not be determined" : $"ContentSize={ContentSize}";
            return $"{contentSizeTrace}, Results=[" + string.Join(
                       ",",
                       DeleteMapping.Select(
                           pair => $"{pair.Key}: {pair.Value}")) + "]";
        }
    }
}
