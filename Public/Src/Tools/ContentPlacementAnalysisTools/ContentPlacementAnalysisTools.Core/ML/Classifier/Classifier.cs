using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace ContentPlacementAnalysisTools.Core.ML.Classifier
{
    /// <summary>
    ///  Base class for classifiers
    /// </summary>
    public abstract class MLClassifier
    {

        /// <summary>
        ///  Classify a single instance
        /// </summary>
        public abstract void Classify(MLInstance instance);
        /// <summary>
        ///  Classify a single instance using many threads
        /// </summary>
        public abstract void Classify(MLInstance instance, int maxParalellism);

    }

    /// <summary>
    ///  Base class for all instances
    /// </summary>
    public abstract class MLInstance {}

    /// <summary>
    ///  An instance to be classified in a binary fashion
    /// </summary>
    public abstract class BinaryMLInstance : MLInstance
    {
        /// <summary>
        ///  The class set after the prediction is done
        /// </summary>
        public string PredictedClass { get; set; } = null;
        
    }

    /// <summary>
    ///  An instance to be classified in a multiclass fashion
    /// </summary>
    public abstract class MultiClassMLInstance : MLInstance
    {
        /// <summary>
        ///  The class set after the prediction is done
        /// </summary>
        public List<string> PredictedClasses { get; set; } = null;
    }
}
