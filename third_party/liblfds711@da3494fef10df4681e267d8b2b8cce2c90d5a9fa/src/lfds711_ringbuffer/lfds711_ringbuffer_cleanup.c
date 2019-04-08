/***** includes *****/
#include "lfds711_ringbuffer_internal.h"

/***** private prototypes *****/
static void lfds711_ringbuffer_internal_queue_umm_element_cleanup_callback( struct lfds711_queue_umm_state *qumms,
                                                                            struct lfds711_queue_umm_element *qumme,
                                                                            enum lfds711_misc_flag dummy_element_flag );
static void lfds711_ringbuffer_internal_freelist_element_cleanup_callback( struct lfds711_freelist_state *fs,
                                                                           struct lfds711_freelist_element *fe );





/****************************************************************************/
void lfds711_ringbuffer_cleanup( struct lfds711_ringbuffer_state *rs,
                                 void (*element_cleanup_callback)(struct lfds711_ringbuffer_state *rs, void *key, void *value, enum lfds711_misc_flag unread_flag) )
{
  LFDS711_PAL_ASSERT( rs != NULL );
  // TRD : element_cleanup_callback can be NULL

  if( element_cleanup_callback != NULL )
  {
    rs->element_cleanup_callback = element_cleanup_callback;
    lfds711_queue_umm_cleanup( &rs->qumms, lfds711_ringbuffer_internal_queue_umm_element_cleanup_callback );
    lfds711_freelist_cleanup( &rs->fs, lfds711_ringbuffer_internal_freelist_element_cleanup_callback );
  }

  return;
}





/****************************************************************************/
#pragma warning( disable : 4100 )

static void lfds711_ringbuffer_internal_queue_umm_element_cleanup_callback( struct lfds711_queue_umm_state *qumms,
                                                                            struct lfds711_queue_umm_element *qumme,
                                                                            enum lfds711_misc_flag dummy_element_flag )
{
  struct lfds711_ringbuffer_element
    *re;

  struct lfds711_ringbuffer_state
    *rs;

  LFDS711_PAL_ASSERT( qumms != NULL );
  LFDS711_PAL_ASSERT( qumme != NULL );
  // TRD : dummy_element can be any value in its range

  rs = (struct lfds711_ringbuffer_state *) LFDS711_QUEUE_UMM_GET_USER_STATE_FROM_STATE( *qumms );
  re = (struct lfds711_ringbuffer_element *) LFDS711_QUEUE_UMM_GET_VALUE_FROM_ELEMENT( *qumme );

  if( dummy_element_flag == LFDS711_MISC_FLAG_LOWERED )
    rs->element_cleanup_callback( rs, re->key, re->value, LFDS711_MISC_FLAG_RAISED );

  return;
}

#pragma warning( default : 4100 )





/****************************************************************************/
#pragma warning( disable : 4100 )

static void lfds711_ringbuffer_internal_freelist_element_cleanup_callback( struct lfds711_freelist_state *fs,
                                                                           struct lfds711_freelist_element *fe )
{
  struct lfds711_ringbuffer_element
    *re;

  struct lfds711_ringbuffer_state
    *rs;

  LFDS711_PAL_ASSERT( fs != NULL );
  LFDS711_PAL_ASSERT( fe != NULL );

  rs = (struct lfds711_ringbuffer_state *) LFDS711_FREELIST_GET_USER_STATE_FROM_STATE( *fs );
  re = (struct lfds711_ringbuffer_element *) LFDS711_FREELIST_GET_VALUE_FROM_ELEMENT( *fe );

  rs->element_cleanup_callback( rs, re->key, re->value, LFDS711_MISC_FLAG_LOWERED );

  return;
}

#pragma warning( default : 4100 )

