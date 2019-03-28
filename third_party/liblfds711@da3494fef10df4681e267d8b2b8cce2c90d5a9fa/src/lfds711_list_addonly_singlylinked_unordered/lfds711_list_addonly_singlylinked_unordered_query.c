/***** includes *****/
#include "lfds711_list_addonly_singlylinked_unordered_internal.h"

/***** private prototypes *****/
static void lfds711_list_asu_internal_validate( struct lfds711_list_asu_state *lasus,
                                                struct lfds711_misc_validation_info *vi,
                                                enum lfds711_misc_validity *lfds711_list_asu_validity );





/****************************************************************************/
void lfds711_list_asu_query( struct lfds711_list_asu_state *lasus,
                             enum lfds711_list_asu_query query_type,
                             void *query_input,
                             void *query_output )
{
  LFDS711_PAL_ASSERT( lasus != NULL );
  // TRD : query_type can be any value in its range

  LFDS711_MISC_BARRIER_LOAD;

  switch( query_type )
  {
    case LFDS711_LIST_ASU_QUERY_GET_POTENTIALLY_INACCURATE_COUNT:
    {
      struct lfds711_list_asu_element
        *lasue = NULL;

      LFDS711_PAL_ASSERT( query_input == NULL );
      LFDS711_PAL_ASSERT( query_output != NULL );

      *(lfds711_pal_uint_t *) query_output = 0;

      while( LFDS711_LIST_ASU_GET_START_AND_THEN_NEXT(*lasus, lasue) )
        ( *(lfds711_pal_uint_t *) query_output )++;
    }
    break;

    case LFDS711_LIST_ASU_QUERY_SINGLETHREADED_VALIDATE:
      // TRD : query_input can be NULL
      LFDS711_PAL_ASSERT( query_output != NULL );

      lfds711_list_asu_internal_validate( lasus, (struct lfds711_misc_validation_info *) query_input, (enum lfds711_misc_validity *) query_output );
    break;
  }

  return;
}






/****************************************************************************/
static void lfds711_list_asu_internal_validate( struct lfds711_list_asu_state *lasus,
                                                struct lfds711_misc_validation_info *vi,
                                                enum lfds711_misc_validity *lfds711_list_asu_validity )
{
  lfds711_pal_uint_t
    number_elements = 0;

  struct lfds711_list_asu_element
    *lasue_fast,
    *lasue_slow;

  LFDS711_PAL_ASSERT( lasus!= NULL );
  // TRD : vi can be NULL
  LFDS711_PAL_ASSERT( lfds711_list_asu_validity != NULL );

  *lfds711_list_asu_validity = LFDS711_MISC_VALIDITY_VALID;

  lasue_slow = lasue_fast = lasus->start->next;

  /* TRD : first, check for a loop
           we have two pointers
           both of which start at the start of the list
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

  if( lasue_slow != NULL )
    do
    {
      lasue_slow = lasue_slow->next;

      if( lasue_fast != NULL )
        lasue_fast = lasue_fast->next;

      if( lasue_fast != NULL )
        lasue_fast = lasue_fast->next;
    }
    while( lasue_slow != NULL and lasue_fast != lasue_slow );

  if( lasue_fast != NULL and lasue_slow != NULL and lasue_fast == lasue_slow )
    *lfds711_list_asu_validity = LFDS711_MISC_VALIDITY_INVALID_LOOP;

  /* TRD : now check for expected number of elements
           vi can be NULL, in which case we do not check
           we know we don't have a loop from our earlier check
  */

  if( *lfds711_list_asu_validity == LFDS711_MISC_VALIDITY_VALID and vi != NULL )
  {
    lfds711_list_asu_query( lasus, LFDS711_LIST_ASU_QUERY_GET_POTENTIALLY_INACCURATE_COUNT, NULL, &number_elements );

    if( number_elements < vi->min_elements )
      *lfds711_list_asu_validity = LFDS711_MISC_VALIDITY_INVALID_MISSING_ELEMENTS;

    if( number_elements > vi->max_elements )
      *lfds711_list_asu_validity = LFDS711_MISC_VALIDITY_INVALID_ADDITIONAL_ELEMENTS;
  }

  return;
}

