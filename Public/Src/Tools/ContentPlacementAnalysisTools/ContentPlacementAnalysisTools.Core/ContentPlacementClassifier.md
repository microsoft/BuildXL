## Creating a content placement classifier

In order to instanciate a content placement classifier, you need:

1. A configuration file, with the following spec (Check class ML.Classifier.ContentPlacementClassifierConfiguration)

####################################################################
## Sample configuration file:
####################################################################
{
    "RandomForestFile":"<Path to valid .wtree file>,
    "QueueDistanceMapDirectory":"Path to valid QueueMap directory",
    "QueueToMachineMapDirectory":"Path to valid MachineMap directory",
    "QueueToMachineMapInstanceCount": <How many machines will be selected as alternatives when LoadMode=MachinesFromClosestQueue is specified>,
    "MachinesInstanceCount": <How many machines per queue will be selected as alternatives when LoadMode=NMachinesPerClosestQueues is specified>,
    "QueueInstanceCount": <How many queues will be selected as alternatives when LoadMode=NMachinesPerClosestQueues is specified>,
    "LoadMode":"<Either MachinesFromClosestQueue or NMachinesPerClosestQueues. In the later, QueueInstanceCount * MachinesInstanceCount are returned for each shared instance>"
}
####################################################################

2. You can use ContentPlacementClassifier(string <path to config file>) to create an instance or ContentPlacementClassifier(ContentPlacementClassifierConfiguration <valid config object>)
to build a classifier. After created, it will be fully loaded and ready to work.

## Instances:

To create an instance, you can use the provided constructor (the one with arguments) at ContentPlacementInstance. You basically need:

1. A queue Name, which is the queue on which the instance is being used.
2. A group of double, namely:

double sizeInBytes => size of the file in bytes
double inputPipCount => how many pips have had this file as input 
double outputPipCount  => how many pips have had this file as output
double avgPositionInputPips  => The avg position of the pips that consumed this file. Position can be calculated using time (pip start time and total build time)
double avgPositionOutputPips  => The avg position of the pips that produced this file. Position can be calculated using time (pip end time and total build time)
double avgDepsInputPips => avg deps for pips that used this file as inputs. These are pip deps, not files
double avgDepsOutputPips => avg deps for pips that produced this file. Thse are pip deps, not files
double avgInputsInputPips => avg number of inputs for the pips that consume this file (file inputs)
double avgInputsOutputPips => avg number of inputs for the pips that produce this file (file inputs) 
double avgOutputsInputPips => avg number of outputs for the pips that consume this file (file inputs) 
double avgOutputsOutputPips => avg number of outputs for the pips that produce this file (file inputs)
double avgPriorityInputPips => avg priority for the PROCESS pips that consume this file
double avgPriorityOutputPips =>  avg priority for the PROCESS pips that produce this file
double avgWeightInputPips => avg weight for the PROCESS pips that consume this file
double avgWeightOutputPips => avg weight for the PROCESS pips that produce this file
double avgTagCountInputPips => avg number of tags for the PROCESS pips that consume this file 
double avgTagCountOutputPips => avg number of tags for the PROCESS pips that produce this file  
double avgSemaphoreCountInputPips => avg number of semaphores for the PROCESS pips that consume this file  
double avgSemaphoreCountOutputPips => avg number of semaphores for the PROCESS pips that produce this file 

In here, its easy to exclude a property. For example, lets say we dont care about "avgTagCountInputPips". You dont need to modify the data structure BUT you need to make sure
that the tree is not created using that property. RandomTreeConfigClass has an attr named Removed columns, wich basically indicated the indexes of the columns (starting at 1)
that will be ignored when creating the tree (these columns are taken from the csv input file). The current columns are:

SharingClassLabel,
SharingClassId,
NumQueues,
NumBuilds,
SizeBytes,
AvgInputPips,
AvgOutputPips,
AvgPositionForInputPips,
AvgPositionForOutputPips,
AvgDepsForInputPips, 
AvgDepsForOutputPips, 
AvgInputsForInputPips,
AvgInputsForOutputPips,
AvgOutputsForInputPips,
AvgOutputsForOutputPips,
AvgPriorityForInputPips,
AvgPriorityForOutputPips,
AvgWeightForInputPips,
AvgWeightForOutputPips,
AvgTagCountForInputPips,
AvgTagCountForOutputPips,
AvgSemaphoreCountForInputPips,
AvgSemaphoreCountForOutputPips

and we should always remove columns 2,3,4 (SharingClassId, NumQueues, NumBuilds). As long as you add the correct indexes in the ignore list and create a tree with the correct values, then 
there no problem on zeroing the constrctor argument, since it wont be used by the tree. As you can imagine, adding a new column is harder. You have to make sure the data structure
(see ContentPlacementAnalysisTools.Core.ML.Classifier.RandomForestInstance) has one attribute (with that EXACT column name) in the dictionary. 

If the tree has more attributes than the instance, then you will have problems in the classification.


## Classifying:

Use the ContentPlacementClassifier#Classify method. There is one overloaded version that takes an int to classify in parallel. I havent seen much improvement when using 2 threads, but depending
on your requirements and resources you might be interested in it (notice that the multiple threads are used to traverse the forest). 

This method takes as input a ContentPlacementInstance as described above and:

1. It throws ArtifactNotSharedException in case the input instance is not shared.
2. It throws NoAlternativesForQueueException in case the input instance was shared BUT no alternatives for the specified queue were found. This is an indication of a problematic situation 
(or might mean that simply you are just interested in replicating in certain queues).
3. It throws NoSuchQueueException in case the input queue (the one specified by you for an instance) is not on the map. This again, its either an indication of a problem or on purpose.
4. It does not throw an exception. In this case, ContentPlacementInstance#PredictedClasses contains a non empty list of strings with one machine name for each replication candidate.
 The order in this list does matter, the first ones are better candidates than the last ones (because they are taken from closest queues). Also, thee should be no repeated 
 elements in this list. YOU SHOULD CONSIDER THIS LIST AS READ ONLY, for perfromace reasons i did not copied from the classifier to the output.




