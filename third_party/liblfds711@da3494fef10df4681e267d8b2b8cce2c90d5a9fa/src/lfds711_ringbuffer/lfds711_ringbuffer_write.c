/***** includes *****/
#include "lfds711_ringbuffer_internal.h"





/****************************************************************************/
void lfds711_ringbuffer_write( struct lfds711_ringbuffer_state *rs,
                               void *key,
                               void *value,
                               enum lfds711_misc_flag *overwrite_occurred_flag,
                               void **overwritten_key,
                               void **overwritten_value )
{
  int
    rv = 0;

  struct lfds711_freelist_element
    *fe;

  struct lfds711_queue_umm_element
    *qumme;

  struct lfds711_ringbuffer_element
    *re = NULL;

  LFDS711_PAL_ASSERT( rs != NULL );
  // TRD : key can be NULL
  // TRD : value can be NULL
  // TRD : overwrite_occurred_flag can be NULL
  // TRD : overwritten_key can be NULL
  // TRD : overwritten_value can be NULL
  // TRD : psts can be NULL

  if( overwrite_occurred_flag != NULL )
    *overwrite_occurred_flag = LFDS711_MISC_FLAG_LOWERED;

  do
  {
    rv = lfds711_freelist_pop( &rs->fs, &fe, NULL );

    if( rv == 1 )
      re = LFDS711_FREELIST_GET_VALUE_FROM_ELEMENT( *fe );

    if( rv == 0 )
    {
      // TRD : the queue can return empty as well - remember, we're lock-free; anything could have happened since the previous instruction
      rv = lfds711_queue_umm_dequeue( &rs->qumms, &qumme );

      if( rv == 1 )
      {
        re = LFDS711_QUEUE_UMM_GET_VALUE_FROM_ELEMENT( *qumme );
        re->qumme_use = (struct lfds711_queue_umm_element *) qumme;

        if( overwrite_occurred_flag != NULL )
          *overwrite_occurred_flag = LFDS711_MISC_FLAG_RAISED;

        if( overwritten_key != NULL )
          *overwritten_key = re->key;

        if( overwritten_value != NULL )
          *overwritten_value = re->value;
      }
    }
  }
  while( rv == 0 );

  re->key = key;
  re->value = value;

  LFDS711_QUEUE_UMM_SET_VALUE_IN_ELEMENT( *re->qumme_use, re );
  lfds711_queue_umm_enqueue( &rs->qumms, re->qumme_use );

  return;
}

