/***** the library wide include file *****/
#include "../liblfds711_internal.h"

/***** enums *****/
enum lfds711_queue_umm_queue_state
{
  LFDS711_QUEUE_UMM_QUEUE_STATE_UNKNOWN, 
  LFDS711_QUEUE_UMM_QUEUE_STATE_EMPTY,
  LFDS711_QUEUE_UMM_QUEUE_STATE_ENQUEUE_OUT_OF_PLACE,
  LFDS711_QUEUE_UMM_QUEUE_STATE_ATTEMPT_DEQUEUE
};

/***** private prototypes *****/

