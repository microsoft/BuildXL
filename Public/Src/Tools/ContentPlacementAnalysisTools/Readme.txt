##################################################################
## Content Placement Analysis
## Author: Cesar A. Stuardo
		   castuardo@uchicago.edu (amoniaconh3@gmail.com)	
##################################################################
Index
	- Intro
	- Overview
	- The Process
		- Sampling
		- Training
		- Building a ContentPlacementClassifier
	- The Code
		- ContentPlacementAnalyzer (bxl analyzer)
		- ContentPlacement.Core
		- ContentPlacement.Extraction
		- ContentPlacement.ML
		- ContentPlacement.OfflineMapper
	- Closing Notes
##################################################################
Intro
##################################################################
This document illustrates what I have done in my internship. The 
idea  is to describe the reasoning behind it and the code that I 
have implemented.

##################################################################
Overview
##################################################################
The approach that i took is a white-box approach, in which I 
included the knowledge of the Cache's costumers, more specifically,
BuildXL. The idea here is use this knowledge to get an understanding
of what is shared and what is not: How does a shared artifact (a 
piece of content, a file) looks like, from the point of view of 
BuildXL. Trough many iterations, i decided that the set of 
features that best represent an artifact from the point of
view of the workload that uses (produces/consumes) it (hence, BuildXL 
point of view) is the following (all are averages in the context of 
a workload):

["AvgInputPips"] : How many pips consumed this file as an input 
["AvgOutputPips"] : How many pips produced this file as an output
["AvgPositionForInputPips"] : The position (pip start vs workload end) of input pips
["AvgPositionForOutputPips"] : The position (pip end vs workload end) of output pips
["AvgDepsForInputPips"] : Dependency count of input pips
["AvgDepsForOutputPips"] : Dependency count of output pips
["AvgInputsForInputPips"] : Input count of input pips (file inputs)
["AvgInputsForOutputPips"] : Input count of output pips (file inputs)
["AvgOutputsForInputPips"] : Output count of input pips (file outputs)
["AvgOutputsForOutputPips"] : Output count of output pips (file outputs)
["AvgPriorityForInputPips"] : Priority of input pips (process pips)
["AvgPriorityForOutputPips"] : Priority of output pips (process pips)
["AvgWeightForInputPips"] : Weight of input pips (process pips)
["AvgWeightForOutputPips"] : Weight of output pips (process pips)
["AvgTagCountForInputPips"] : Tag count of input pips (process pips)
["AvgTagCountForOutputPips"] : Tag count of output pips (process pips)
["AvgSemaphoreCountForInputPips"] : Semaphore count of input pips (process pips)
["AvgSemaphoreCountForOutputPips"] : Semaphore count of output pips (process pips)

Its important to notice I use -1 to represent non precense. So for example, if an
artifact has no input pips, then AvgInputPips = AvgPositionForInputPips = ... = -1

With these values (one vector = one instance) I can train a random forest to
make predictions about how shared the file is. In order to decide to go with this method,
I spent a few weeks experimenting with popular ML choices, from siple to complex. The random
forest (and in general, decision-tree based methods) are the ones that produced the best results.
I suspect this is highly related to the fact that most of the build graphs are static and the actual
common content resides in a portion of them (this is, a well defined portion of a linear version 
of the graph, with probably few outliers).

A random forest is a collection of decision trees. Each decision tree is built taking a random
interval of the inout dataset and attempts to build the best suited tree (the one that cassifies the
best) for the instances in said interval. Each random tree looks like the following (just an example, 
they tend to be deeper):

AvgTagCountForInputPips < 0.3556535
|   AvgWeightForOutputPips < 0.8166665 : Shared
|	AvgWeightForOutputPips >= 0.8166665 : NonShared
AvgTagCountForInputPips >= 0.3556535
|   AvgOutputsForInputPips < 0.25 : Shared
|	AvgOutputsForInputPips >= 0.25 : NonShared

The nodes with a colon are leafs and output a classification. They are always at the last level.
The general shape is either ["attr" "operator" "value" : "class"] or ["attr" "operator" "value"]. The attr here 
maps directly to the instance attribute name. This is important to notice since if you create a tree that 
does not include some attribute, then from the instance point of view its irrelevant wich value you assign to it
(it will never be used) BUT if you include one more attribute and DONT modify the instance to have it, it will fail
(throwing some "Not found in dictionary" exception). The decision a random forest takes is obtained from the whole ensemble: 
If N/2 + 1 trees classify the input as Shared, then the artifact is shared. Classifying here means using instance attributes to 
traverse each tree and getting to a leaf node.

In this implementation, the random forest is created using weka. We use this piece of software (its a jar) in two phases:
1. We first create an arff file from the input csv file. This is important for weka's performance.
2. We then create the random forest using another command (and the arff file). As it currently stands, we do
preprocess this file using a weka filter (removing columns 2,3,4,5). So basically each training instance has the values
i have described PLUS the class it belongs (cause the classifier needs to know which class each sample belongs to). Columns
2,3,4,5 (the index of columns starts at 1, so 2 is the second column) were deemed as non relevant but kept for completeness.

With this, you can create a classifier that takes a binary decision: An artifact is either shared or non shared. Now, in order
to decide to which machine we should try to replicate, I took another approach. 
How similar are two queues? By similar, I mean how close and alike the workload the run is. To try to understand this notion, I 
represented each queue as a vector with several columns (see CPResources\Query\get_monthly_queue_data.kql) and measure the 
euclidean distance between these points, with the following observations:

1. I only measure distances beween queues on the same stamp.
2. I penalize (assign distance + max_distance) queues for different architectures.

The closer (less distance) two queues are, the more alike they are. I took this notion to decide which machines should I try
to replicate: Given an input queue, I take N machines from the M closest queues (same architeture and stamp) as the best
possible candidates. For each queue, each machine (as in each machine that has been used by this queue) is also looked up
considering its frequency (used more times by that queue). The most frequent machines are the top candidates.

##################################################################
The Process
##################################################################
You should try to do this weekly and it should not take more than 2-3 hrs. The main
bottlenecks are downloading builds (we can only download one file at the time, each build is 
composed of two zip files, so it takes time) and creating a database (classifiying paths by hash).

1. Sampling

To sample, use the following scripts:

weeklySampleDownload.cmd with the following arguments: 

	a. Configuration file: A valid path to a json configuration file following this spec:
	{ 
		"AnalyzerConfig":{ 
		"Exe": "", 
		"SampleProportion": "<the proportion of non epty artifacts that the sample will contain, 0.1 <= x <= 1.0>", 
		"SampleCountHardLimit": "<the maximum number of artifacts that a sample could contain, e.g. 1000000>"" 
		}, 
		"KustoConfig":{ 
			"Cluster": "https://cbuild.kusto.windows.net", 
			"User": "<a valid id (email)>", 
			"AuthorityId": "", 
			"DefaultDB": "CloudBuildProd" 
		}, 
		"ConcurrencyConfig":{
		"MaxDownloadTasks": <Max. number of threads used to download builds. Usually 1, since the server might not allow concurrent downloads>, 
		"MaxAnalysisTasks": <Max. number of threads used to analyze builds. Each thread will run a bxlanalyzer.exe instance>, 
		"MaxMachineMapTasks": <Max. number of threads for machine map creation. One per processor is fine, since this task is light> 
		} 
	}

	b. The year (int) 
	c. The month (int, 1-12) 
	d. The start day (int, 1-31) 
	e. The number of per-day builds to download (int) 
	f. The output directory (has to exists before)

The span is fixed in this case to 7 days. This means that a certain number of builds (e) will be downloaded from each day starting at month(c)/day(d)/year(b) 
and ending in month(c)/day(d) + 7/year(b). The output json files are created at (f)/Results and are the input for the next phase. 

queueData.cmd with the following arguments:

	a. Configuration file: A valid path to a json configuration file (same as above) 
	b. year (int) 
	c. month (int, 1-12) 
	d. The output directory (has to exists before)

This will download (to (d)/Results) the queue similarity training data to a csv file + 2 directories named 
QueueMap and MachineMap which are necessary to load the content placement classifier. As you probably noticed, this process 
is sampling a month of data for the queues.


createDatabase.cmd with the following arguments:

	a. Configuration file: A valid path to a json configuration file following this spec:
	{ 
		"ConcurrencyConfig":{
			"MaxBuildParsingTasks": <Max threads to read consolidated samples (output of analyzer)>, 
			"MaxArtifactStoreTasks": <Max threads to organize those consolidated samples (per hash)>, 
			"MaxArtifactLinearizationTasks": <Max threads to linearize (vectorzing) artifacst>, 
			"MaxForestCreationTasks": <Max threads to creatre/train random forests> 
		}, 
		"WekaConfig":{ 
			"WekaJar":"CPResources\weka.jar", 
			"MemoryGB": <Memory for weka processes (in GB)>, 
			"WekaRunArffCommand": "weka.core.converters.CSVLoader {0}", 
			"WekaRunRTCommand":"weka.classifiers.meta.FilteredClassifier -F "weka.filters.unsupervised.attribute.Remove -R {0}" -c 1 -t {1} -S 1 -W weka.classifiers.trees.RandomForest -- -P {2} -print -attribute-importance -I {3} -num-slots {4} -K 0 -M 1.0 -V 0.001 -S 0 -num-decimal-places 10", 
			"RandomTreeConfig":{ 
				"RemovedColumns":"2,3,4,5", 
				"RandomTreeCount": 100, 
				"BagSizePercentage": 100, 
				"MaxCreationParallelism": 4, 
				"Classes": "Shared,NonShared" 
			} 
		}
	}

	b. Input dirrectory, which is a valid path containing json consolidated files 
	c. Output directory, which is a valid path (existing) directory on which to write the results.

The output its a set of directories, one for each hash, containing a mapping between hash and queues (one mapping per instance). 
Creating a database might be time consuming but it needs to be done one per sample.

linearizeDatabase.cmd with the following arguments:

	a. Configuration file: A valid path to a json configuration file like specified above. 
	b. Input directory, whcih is a valid path to an existing database (created using createDatabase.cmd) 
	c. Num samples (int default=1), indicating how many samples will be created 
	d. Sample size (int, default=10000) indicating the number of instances in each sample

This will create (c) + 1 csv files in the input directory, containg (c) samples and one csv for all the artifacts. The last csv file should be used to evaluate classifiers created with samples.
You can linearize as many times as you want, notice that the output files will have different names (so the older ones wont be deleted).

2. Training

buildClassifiers.cmd with the following arguments:

	a. Configuration file: A valid path to a json configuration file like specified above. 
	b. Input directory, whcih is a valid path to an existing directory with linearized database (created using linearizeDatabase.cmd)

This will take each "-sample" csv file present in the input directory and will create a random forest for each one (stored with extension wtree in the same directory). After these files are created, each wtree classifier will be evaluated against the main csv file. You will see the precision, recall and performance metrics for each evaluation.
With this, you can select your best classifier (wtree file), take your queue and machine map and create a ContentPlacementClassifier.

3. Building a ContentPlacementClassifier

In order to create a content placement classifier, you need the following configuration file:

{ 
	"RandomForestFile":"<Path to valid .wtree file>, 
	"QueueDistanceMapDirectory":"Path to valid QueueMap directory", 
	"QueueToMachineMapDirectory":"Path to valid MachineMap directory", 
	"QueueToMachineMapInstanceCount": see load mode, 
	"MachinesInstanceCount" see load mode: , 
	"QueueInstanceCount": see load mode, 
	"LoadMode":"<Either MachinesFromClosestQueue or NMachinesPerClosestQueues. If MachinesFromClosestQueue, then MachinesInstanceCount from the closes queue will be taken. 
		In the later, QueueInstanceCount * MachinesInstanceCount are returned for each shared instance>" 
}

With this kind of configuration, simply use the constructor. 


##################################################################
The Code
##################################################################
The code is organized in 4 projects, and each project has documentation, and scripts 
(all described in this file). In general, my code is divided in actions wich are invoked
in blocks (using dataflow framework). Each action passes its results to the next so
its mostly a connected pipeline. The logs for each process can be found at
out\bin\debug\net472\<exename>.log. Notice that the log is cleaned each time you invoke the process.

1. ContentPlacementAnalyzer (bxl analyzer)

Its an analyzer that can, given a build, sample a set of artifacts from the build. It ignores
artifacts with size 0 and it can sample a percentage ([0.1,1.0]) of the build. It creates an output
in the form of json files.

2. ContentPlacement.Core

This is a dll that contains the basic data structures and is the one that should be included when
using a content placement classifier.

3. ContentPlacement.Extraction

This is an exe that downloads and analyze a set of builds given a date range. 

4. ContentPlacement.ML

This is both an exe file and a dll file (used by OfflineMapper) that implements all the training/ml related
section of the project.

5. ContentPlacement.OfflineMapper

This is an exe file used to offline classification of a set of json files (output of ContentPlacement.Extraction). Its
not part of the original scheme and was implemented as a temporary solution. So, the main idea here is that it should
take as input one build from each queue (one analyzed build this is) sampled with 1.0 (so all the files) and will output
a rocks db version of the path-machines mapping. The configuration file needed to run thi process looks like the following:

{
	"ConcurrencyConfig":{  
        "MaxBuildParsingTasks": <max number of threads to read input json files>,
        "MaxArtifactLinearizationTasks": <max number of tasks to linearize (vectorize) an artifact>,
        "MaxArtifactClassificationTasks": <max tasks to classify input artifacts>
    },
    "ClassifierConfiguration":{
	    "RandomForestFile":"<Path to valid .wtree file>, 
		"QueueDistanceMapDirectory":"Path to valid QueueMap directory", 
		"QueueToMachineMapDirectory":"Path to valid MachineMap directory", 
		"QueueToMachineMapInstanceCount": see load mode, 
		"MachinesInstanceCount" see load mode: , 
		"QueueInstanceCount": see load mode, 
		"LoadMode":"<Either MachinesFromClosestQueue or NMachinesPerClosestQueues. If MachinesFromClosestQueue, then MachinesInstanceCount from the closes queue will be taken. 
		In the later, QueueInstanceCount * MachinesInstanceCount are returned for each shared instance>" 
	}
}


##################################################################
Closing Notes
##################################################################
At least at this moment and considering we targeted this project as a net472 project, C# does not have
solid ML libraries, hence we resorted to weka. ML.Net its an alternative (for .net core), and for that
library the best suited algorithm (according to my experiments and dataset) was lightgbm (trees). Accord.Net
was too slow compared to weka, so I could not use it. Outside of these candidates, taking a shot with python
is not a bad idea.

The approach I described approach is clearly not the only one that can be taken. A black-box
approach, in which we solve this problem from the point of view
of the cache (and only the cache) is an interesting future direction for this
project. White box approaches tend to be more powerful (the transparency 
usually allows for better decisions), but that is debatable and specific 
for each problem. Random approaches (or maybe even round robin) also
have advantages, but the main problem is that they cannot give 
optimal solutions. At the end of the day, the best approach is the one
that better meets the goals, thus goals need to be defined first. 

As I pointed out earlier, I believe that among all the current build graphs, some of them 
(hopefully a small set) could be used as representatives of the whole set. This is useful for
any scenario that involves sampling, since now you only need to look at this representatives. 
It can also be an interesting approach for this kind of problem: Are there any graph related point
of view (think homomorphism and other graph properties) that could give hints? Will this be useful
for the Cache? I think the answer is clearly yes, but it was out of the scope of this iteration.

Finally, feel free to drop an email if have a question, I tend to reply the same day. My personal email 
is always the best choice.

Kind Regards,
Cesar A. Stuardo