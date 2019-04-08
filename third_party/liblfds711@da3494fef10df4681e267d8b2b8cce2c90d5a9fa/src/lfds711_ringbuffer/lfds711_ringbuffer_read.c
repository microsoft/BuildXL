/***** includes *****/
#include "lfds711_ringbuffer_internal.h"





/****************************************************************************/
int lfds711_ringbuffer_read( struct lfds711_ringbuffer_state *rs,
                             void **key,
                             void **value )
{
  int
    rv;

  struct lfds711_queue_umm_element
    *qumme;

  struct lfds711_ringbuffer_element
    *re;

  LFDS711_PAL_ASSERT( rs != NULL );
  // TRD : key can be NULL
  // TRD : value can be NULL
  // TRD : psts can be NULL

  rv = lfds711_queue_umm_dequeue( &rs->qumms, &qumme );

  if( rv == 1 )
  {
    re = LFDS711_QUEUE_UMM_GET_VALUE_FROM_ELEMENT( *qumme );
    re->qumme_use = (struct lfds711_queue_umm_element *) qumme;
    if( key != NULL )
      *key = re->key;
    if( value != NULL )
      *value = re->value;
    LFDS711_FREELIST_SET_VALUE_IN_ELEMENT( re->fe, re );
    lfds711_freelist_push( &rs->fs, &re->fe, NULL );
  }

  return rv;
}

