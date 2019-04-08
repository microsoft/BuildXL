/***** includes *****/
#include "lfds711_misc_internal.h"





/****************************************************************************/
void lfds711_misc_internal_backoff_init( struct lfds711_misc_backoff_state *bs )
{
  LFDS711_PAL_ASSERT( bs != NULL );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &bs->lock % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );

  bs->lock = LFDS711_MISC_FLAG_LOWERED;
  bs->backoff_iteration_frequency_counters[0] = 0;
  bs->backoff_iteration_frequency_counters[1] = 0;
  bs->metric = 1;
  bs->total_operations = 0;

  return;
}

