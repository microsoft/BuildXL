/***** includes *****/
#include "lfds711_queue_unbounded_manyproducer_manyconsumer_internal.h"

/***** private prototypes *****/
static void lfds711_queue_umm_internal_validate( struct lfds711_queue_umm_state *qumms,
                                                 struct lfds711_misc_validation_info *vi,
                                                 enum lfds711_misc_validity *lfds711_queue_umm_validity );





/****************************************************************************/
void lfds711_queue_umm_query( struct lfds711_queue_umm_state *qumms,
                              enum lfds711_queue_umm_query query_type,
                              void *query_input,
                              void *query_output )
{
  struct lfds711_queue_umm_element
    *qumme;

  LFDS711_MISC_BARRIER_LOAD;

  LFDS711_PAL_ASSERT( qumms != NULL );
  // TRD : query_type can be any value in its range

  switch( query_type )
  {
    case LFDS711_QUEUE_UMM_QUERY_SINGLETHREADED_GET_COUNT:
      LFDS711_PAL_ASSERT( query_input == NULL );
      LFDS711_PAL_ASSERT( query_output != NULL );

      *(lfds711_pal_uint_t *) query_output = 0;

      qumme = (struct lfds711_queue_umm_element *) qumms->dequeue[POINTER];

      while( qumme != NULL )
      {
        ( *(lfds711_pal_uint_t *) query_output )++;
        qumme = (struct lfds711_queue_umm_element *) qumme->next[POINTER];
      }

      // TRD : remember there is a dummy element in the queue
      ( *(lfds711_pal_uint_t *) query_output )--;
    break;

    case LFDS711_QUEUE_UMM_QUERY_SINGLETHREADED_VALIDATE:
      // TRD : query_input can be NULL
      LFDS711_PAL_ASSERT( query_output != NULL );

      lfds711_queue_umm_internal_validate( qumms, (struct lfds711_misc_validation_info *) query_input, (enum lfds711_misc_validity *) query_output );
    break;
  }

  return;
}





/****************************************************************************/
static void lfds711_queue_umm_internal_validate( struct lfds711_queue_umm_state *qumms,
                                                 struct lfds711_misc_validation_info *vi,
                                                 enum lfds711_misc_validity *lfds711_queue_umm_validity )
{
  lfds711_pal_uint_t
    number_elements = 0;

  struct lfds711_queue_umm_element
    *qumme_fast,
    *qumme_slow;

  LFDS711_PAL_ASSERT( qumms != NULL );
  // TRD : vi can be NULL
  LFDS711_PAL_ASSERT( lfds711_queue_umm_validity != NULL );

  *lfds711_queue_umm_validity = LFDS711_MISC_VALIDITY_VALID;

  qumme_slow = qumme_fast = (struct lfds711_queue_umm_element *) qumms->dequeue[POINTER];

  /* TRD : first, check for a loop
           we have two pointers
           both of which start at the dequeue end of the queue
           we enter a loop
           and on each iteration
           we advance one pointer by one element
           and the other by two

           we exit the loop when both pointers are NULL
           (have reached the end of the queue)

           or

           if we fast pointer 'sees' the slow pointer
           which means we have a loop
  */

  if( qumme_slow != NULL )
    do
    {
      qumme_slow = qumme_slow->next[POINTER];

      if( qumme_fast != NULL )
        qumme_fast = qumme_fast->next[POINTER];

      if( qumme_fast != NULL )
        qumme_fast = qumme_fast->next[POINTER];
    }
    while( qumme_slow != NULL and qumme_fast != qumme_slow );

  if( qumme_fast != NULL and qumme_slow != NULL and qumme_fast == qumme_slow )
    *lfds711_queue_umm_validity = LFDS711_MISC_VALIDITY_INVALID_LOOP;

  /* TRD : now check for expected number of elements
           vi can be NULL, in which case we do not check
           we know we don't have a loop from our earlier check
  */

  if( *lfds711_queue_umm_validity == LFDS711_MISC_VALIDITY_VALID and vi != NULL )
  {
    lfds711_queue_umm_query( qumms, LFDS711_QUEUE_UMM_QUERY_SINGLETHREADED_GET_COUNT, NULL, (void *) &number_elements );

    if( number_elements < vi->min_elements )
      *lfds711_queue_umm_validity = LFDS711_MISC_VALIDITY_INVALID_MISSING_ELEMENTS;

    if( number_elements > vi->max_elements )
      *lfds711_queue_umm_validity = LFDS711_MISC_VALIDITY_INVALID_ADDITIONAL_ELEMENTS;
  }

  return;
}

