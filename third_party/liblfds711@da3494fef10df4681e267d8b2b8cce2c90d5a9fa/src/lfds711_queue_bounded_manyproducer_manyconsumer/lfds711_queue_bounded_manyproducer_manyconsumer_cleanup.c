/***** includes *****/
#include "lfds711_queue_bounded_manyproducer_manyconsumer_internal.h"





/****************************************************************************/
void lfds711_queue_bmm_cleanup( struct lfds711_queue_bmm_state *qbmms,
                                void (*element_cleanup_callback)(struct lfds711_queue_bmm_state *qbmms, void *key, void *value) )
{
  void
    *key,
    *value;

  LFDS711_PAL_ASSERT( qbmms != NULL );
  // TRD : element_cleanup_callback can be NULL

  LFDS711_MISC_BARRIER_LOAD;

  if( element_cleanup_callback != NULL )
    while( lfds711_queue_bmm_dequeue(qbmms,&key,&value) )
      element_cleanup_callback( qbmms, key, value );

  return;
}

