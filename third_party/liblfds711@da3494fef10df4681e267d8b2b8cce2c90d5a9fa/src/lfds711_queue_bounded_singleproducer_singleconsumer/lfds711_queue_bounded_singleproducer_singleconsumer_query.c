/***** includes *****/
#include "lfds711_queue_bounded_singleproducer_singleconsumer_internal.h"

/***** private prototypes *****/
static void lfds711_queue_bss_internal_validate( struct lfds711_queue_bss_state *qbsss,
                                                 struct lfds711_misc_validation_info *vi,
                                                 enum lfds711_misc_validity *lfds711_validity );





/****************************************************************************/
void lfds711_queue_bss_query( struct lfds711_queue_bss_state *qbsss,
                              enum lfds711_queue_bss_query query_type,
                              void *query_input,
                              void *query_output )
{
  LFDS711_PAL_ASSERT( qbsss != NULL );
  // TRD : query_type can be any value in its range

  switch( query_type )
  {
    case LFDS711_QUEUE_BSS_QUERY_GET_POTENTIALLY_INACCURATE_COUNT:
    {
      lfds711_pal_uint_t
        local_read_index,
        local_write_index;

      LFDS711_PAL_ASSERT( query_input == NULL );
      LFDS711_PAL_ASSERT( query_output != NULL );

      LFDS711_MISC_BARRIER_LOAD;

      local_read_index = qbsss->read_index;
      local_write_index = qbsss->write_index;

      *(lfds711_pal_uint_t *) query_output = +( local_write_index - local_read_index );

      if( local_read_index > local_write_index )
        *(lfds711_pal_uint_t *) query_output = qbsss->number_elements - *(lfds711_pal_uint_t *) query_output;
    }
    break;

    case LFDS711_QUEUE_BSS_QUERY_VALIDATE:
      // TRD : query_input can be NULL
      LFDS711_PAL_ASSERT( query_output != NULL );

      lfds711_queue_bss_internal_validate( qbsss, (struct lfds711_misc_validation_info *) query_input, (enum lfds711_misc_validity *) query_output );
    break;
  }

  return;
}





/****************************************************************************/
static void lfds711_queue_bss_internal_validate( struct lfds711_queue_bss_state *qbsss,
                                                 struct lfds711_misc_validation_info *vi,
                                                 enum lfds711_misc_validity *lfds711_validity )
{
  LFDS711_PAL_ASSERT( qbsss != NULL );
  // TRD : vi can be NULL
  LFDS711_PAL_ASSERT( lfds711_validity != NULL );

  *lfds711_validity = LFDS711_MISC_VALIDITY_VALID;

  if( vi != NULL )
  {
    lfds711_pal_uint_t
      number_elements;

    lfds711_queue_bss_query( qbsss, LFDS711_QUEUE_BSS_QUERY_GET_POTENTIALLY_INACCURATE_COUNT, NULL, (void *) &number_elements );

    if( number_elements < vi->min_elements )
      *lfds711_validity = LFDS711_MISC_VALIDITY_INVALID_MISSING_ELEMENTS;

    if( number_elements > vi->max_elements )
      *lfds711_validity = LFDS711_MISC_VALIDITY_INVALID_ADDITIONAL_ELEMENTS;
  }

  return;
}

