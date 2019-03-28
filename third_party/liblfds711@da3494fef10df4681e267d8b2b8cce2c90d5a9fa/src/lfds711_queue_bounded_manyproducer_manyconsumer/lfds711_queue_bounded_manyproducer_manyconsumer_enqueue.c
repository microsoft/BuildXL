/***** includes *****/
#include "lfds711_queue_bounded_manyproducer_manyconsumer_internal.h"





/****************************************************************************/
int lfds711_queue_bmm_enqueue( struct lfds711_queue_bmm_state *qbmms,
                               void *key,
                               void *value )
{
  char unsigned
    result;

  enum lfds711_misc_flag
    finished_flag = LFDS711_MISC_FLAG_LOWERED;

  int
    rv = 1;

  lfds711_pal_uint_t
    sequence_number,
    write_index;

  lfds711_pal_int_t
    difference;

  lfds711_pal_uint_t
    backoff_iteration = LFDS711_BACKOFF_INITIAL_VALUE;

  struct lfds711_queue_bmm_element
    *qbmme = NULL;

  LFDS711_PAL_ASSERT( qbmms != NULL );
  // TRD : key can be NULL
  // TRD : value can be NULL

  LFDS711_MISC_BARRIER_LOAD;

  write_index = qbmms->write_index;

  while( finished_flag == LFDS711_MISC_FLAG_LOWERED )
  {
    qbmme = &qbmms->element_array[ write_index & qbmms->mask ];
    LFDS711_MISC_BARRIER_LOAD;
    sequence_number = qbmme->sequence_number;
    difference = (lfds711_pal_int_t) sequence_number - (lfds711_pal_int_t) write_index;

    if( difference == 0 )
    {
      LFDS711_PAL_ATOMIC_CAS( &qbmms->write_index, &write_index, write_index + 1, LFDS711_MISC_CAS_STRENGTH_WEAK, result );
      if( result == 0 )
        LFDS711_BACKOFF_EXPONENTIAL_BACKOFF( qbmms->enqueue_backoff, backoff_iteration );
      if( result == 1 )
        finished_flag = LFDS711_MISC_FLAG_RAISED;
    }

    if( difference < 0 )
    {
      rv = 0;
      finished_flag = LFDS711_MISC_FLAG_RAISED;
    }

    if( difference > 0 )
    {
      LFDS711_MISC_BARRIER_LOAD;
      write_index = qbmms->write_index;
    }
  }

  if( rv == 1 )
  {
    qbmme->key = key;
    qbmme->value = value;
    LFDS711_MISC_BARRIER_STORE;
    qbmme->sequence_number = write_index + 1;
  }

  LFDS711_BACKOFF_AUTOTUNE( qbmms->enqueue_backoff, backoff_iteration );

  return rv;
}

