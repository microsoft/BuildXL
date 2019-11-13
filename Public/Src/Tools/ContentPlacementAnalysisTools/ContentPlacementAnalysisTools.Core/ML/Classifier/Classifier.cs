using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace ContentPlacementAnalysisTools.Core.ML.Classifier
{
    /// <summary>
    ///  Base class for classifiers
    /// </summary>
    public interface IMLClassifier<TInstance, TResult>
    {
        /// <summary>
        ///  Classify a single instance
        /// </summary>
        TResult Classify(TInstance instance);

        /// <summary>
        ///  Classify a single instance using many threads
        /// </summary>
        TResult Classify(TInstance instance, int maxParalellism);
    }

    /// <nodoc />
    public interface IMultiClassMLClassifier<TInstance> : IMLClassifier<TInstance, Result<List<string>>>
    {
    }
    
    /// <nodoc />
    public interface IBinaryMLClassifier<TInstance> : IMLClassifier<TInstance, Result<string>>
    {
    }
}
