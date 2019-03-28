/***** includes *****/
#include "lfds711_queue_unbounded_manyproducer_manyconsumer_internal.h"





/****************************************************************************/
void lfds711_queue_umm_init_valid_on_current_logical_core( struct lfds711_queue_umm_state *qumms,
                                                           struct lfds711_queue_umm_element *qumme_dummy,
                                                           void *user_state )
{
  LFDS711_PAL_ASSERT( qumms != NULL );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &qumms->enqueue % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &qumms->dequeue % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &qumms->user_state % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  LFDS711_PAL_ASSERT( qumme_dummy != NULL );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) qumme_dummy->next % LFDS711_PAL_ALIGN_DOUBLE_POINTER == 0 );
  // TRD : user_state can be NULL

  /* TRD : qumme_dummy is a dummy element, needed for init
           the qumms->enqueue and qumms->dequeue counters do not need to be initialized
           but it does no harm to do so, and stops a valgrind complaint
  */

  LFDS711_PRNG_GENERATE( lfds711_misc_globals.ps, qumms->aba_counter );

  qumms->enqueue[POINTER] = qumme_dummy;
  qumms->enqueue[COUNTER] = (struct lfds711_queue_umm_element *) 0;
  qumms->dequeue[POINTER] = qumme_dummy;
  qumms->dequeue[COUNTER] = (struct lfds711_queue_umm_element *) 0;

  qumme_dummy->next[POINTER] = NULL;
  // TRD : no need here for an atomic add as we have a store barrier and force store below
  qumme_dummy->next[COUNTER] = (struct lfds711_queue_umm_element *) qumms->aba_counter++;
  qumme_dummy->key = NULL;
  qumme_dummy->value = NULL;

  qumms->user_state = user_state;

  lfds711_misc_internal_backoff_init( &qumms->dequeue_backoff );
  lfds711_misc_internal_backoff_init( &qumms->enqueue_backoff );

  LFDS711_MISC_BARRIER_STORE;

  lfds711_misc_force_store();

  return;
}

