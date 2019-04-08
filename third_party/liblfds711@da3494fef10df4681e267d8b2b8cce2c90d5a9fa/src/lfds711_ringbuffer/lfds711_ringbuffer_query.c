/***** includes *****/
#include "lfds711_ringbuffer_internal.h"

/***** private prototypes *****/
static void lfds711_ringbuffer_internal_validate( struct lfds711_ringbuffer_state *rs,
                                                  struct lfds711_misc_validation_info *vi,
                                                  enum lfds711_misc_validity *lfds711_queue_umm_validity,
                                                  enum lfds711_misc_validity *lfds711_freelist_validity );



/****************************************************************************/
void lfds711_ringbuffer_query( struct lfds711_ringbuffer_state *rs,
                               enum lfds711_ringbuffer_query query_type,
                               void *query_input,
                               void *query_output )
{
  LFDS711_PAL_ASSERT( rs != NULL );
  // TRD : query_type can be any value in its range

  LFDS711_MISC_BARRIER_LOAD;

  switch( query_type )
  {
    case LFDS711_RINGBUFFER_QUERY_SINGLETHREADED_GET_COUNT:
      LFDS711_PAL_ASSERT( query_input == NULL );
      LFDS711_PAL_ASSERT( query_output != NULL );

      lfds711_queue_umm_query( &rs->qumms, LFDS711_QUEUE_UMM_QUERY_SINGLETHREADED_GET_COUNT, NULL, query_output );
    break;

    case LFDS711_RINGBUFFER_QUERY_SINGLETHREADED_VALIDATE:
      // TRD : query_input can be NULL
      LFDS711_PAL_ASSERT( query_output != NULL );

      lfds711_ringbuffer_internal_validate( rs, (struct lfds711_misc_validation_info *) query_input, (enum lfds711_misc_validity *) query_output, ((enum lfds711_misc_validity *) query_output)+1 );
    break;
  }

  return;
}





/****************************************************************************/
static void lfds711_ringbuffer_internal_validate( struct lfds711_ringbuffer_state *rs,
                                                  struct lfds711_misc_validation_info *vi,
                                                  enum lfds711_misc_validity *lfds711_queue_umm_validity,
                                                  enum lfds711_misc_validity *lfds711_freelist_validity )
{
  LFDS711_PAL_ASSERT( rs != NULL );
  // TRD : vi can be NULL
  LFDS711_PAL_ASSERT( lfds711_queue_umm_validity != NULL );
  LFDS711_PAL_ASSERT( lfds711_freelist_validity != NULL );

  if( vi == NULL )
  {
    lfds711_queue_umm_query( &rs->qumms, LFDS711_QUEUE_UMM_QUERY_SINGLETHREADED_VALIDATE, NULL, lfds711_queue_umm_validity );
    lfds711_freelist_query( &rs->fs, LFDS711_FREELIST_QUERY_SINGLETHREADED_VALIDATE, NULL, lfds711_freelist_validity );
  }

  if( vi != NULL )
  {
    struct lfds711_misc_validation_info
      freelist_vi,
      queue_vi;

    queue_vi.min_elements = 0;
    freelist_vi.min_elements = 0;
    queue_vi.max_elements = vi->max_elements;
    freelist_vi.max_elements = vi->max_elements;

    lfds711_queue_umm_query( &rs->qumms, LFDS711_QUEUE_UMM_QUERY_SINGLETHREADED_VALIDATE, &queue_vi, lfds711_queue_umm_validity );
    lfds711_freelist_query( &rs->fs, LFDS711_FREELIST_QUERY_SINGLETHREADED_VALIDATE, &freelist_vi, lfds711_freelist_validity );
  }

  return;
}

