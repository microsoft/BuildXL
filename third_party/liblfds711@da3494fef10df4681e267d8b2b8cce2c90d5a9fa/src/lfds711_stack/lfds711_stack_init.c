/***** includes *****/
#include "lfds711_stack_internal.h"





/****************************************************************************/
void lfds711_stack_init_valid_on_current_logical_core( struct lfds711_stack_state *ss,
                                                       void *user_state )
{
  LFDS711_PAL_ASSERT( ss != NULL );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) ss->top % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &ss->user_state % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  // TRD : user_state can be NULL

  ss->top[POINTER] = NULL;
  ss->top[COUNTER] = 0;

  ss->user_state = user_state;

  lfds711_misc_internal_backoff_init( &ss->pop_backoff );
  lfds711_misc_internal_backoff_init( &ss->push_backoff );

  LFDS711_MISC_BARRIER_STORE;

  lfds711_misc_force_store();

  return;
}

