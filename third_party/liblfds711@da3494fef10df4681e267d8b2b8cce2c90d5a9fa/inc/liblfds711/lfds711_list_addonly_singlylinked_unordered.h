/***** defines *****/
#define LFDS711_LIST_ASU_GET_START( list_asu_state )                                             ( LFDS711_MISC_BARRIER_LOAD, (list_asu_state).start->next )
#define LFDS711_LIST_ASU_GET_NEXT( list_asu_element )                                            ( LFDS711_MISC_BARRIER_LOAD, (list_asu_element).next )
#define LFDS711_LIST_ASU_GET_START_AND_THEN_NEXT( list_asu_state, pointer_to_list_asu_element )  ( (pointer_to_list_asu_element) == NULL ? ( (pointer_to_list_asu_element) = LFDS711_LIST_ASU_GET_START(list_asu_state) ) : ( (pointer_to_list_asu_element) = LFDS711_LIST_ASU_GET_NEXT(*(pointer_to_list_asu_element)) ) )
#define LFDS711_LIST_ASU_GET_KEY_FROM_ELEMENT( list_asu_element )                                ( (list_asu_element).key )
#define LFDS711_LIST_ASU_SET_KEY_IN_ELEMENT( list_asu_element, new_key )                         ( (list_asu_element).key = (void *) (lfds711_pal_uint_t) (new_key) )
#define LFDS711_LIST_ASU_GET_VALUE_FROM_ELEMENT( list_asu_element )                              ( LFDS711_MISC_BARRIER_LOAD, (list_asu_element).value )
#define LFDS711_LIST_ASU_SET_VALUE_IN_ELEMENT( list_asu_element, new_value )                     { LFDS711_PAL_ATOMIC_SET( &(list_asu_element).value, new_value ); }
#define LFDS711_LIST_ASU_GET_USER_STATE_FROM_STATE( list_asu_state )                             ( (list_asu_state).user_state )

/***** enums *****/
enum lfds711_list_asu_position
{
  LFDS711_LIST_ASU_POSITION_START,
  LFDS711_LIST_ASU_POSITION_END,
  LFDS711_LIST_ASU_POSITION_AFTER
};

enum lfds711_list_asu_query
{
  LFDS711_LIST_ASU_QUERY_GET_POTENTIALLY_INACCURATE_COUNT,
  LFDS711_LIST_ASU_QUERY_SINGLETHREADED_VALIDATE
};

/***** structures *****/
struct lfds711_list_asu_element
{
  struct lfds711_list_asu_element LFDS711_PAL_ALIGN(LFDS711_PAL_ALIGN_SINGLE_POINTER)
    *volatile next;

  void LFDS711_PAL_ALIGN(LFDS711_PAL_ALIGN_SINGLE_POINTER)
    *volatile value;

  void
    *key;
};

struct lfds711_list_asu_state
{
  struct lfds711_list_asu_element LFDS711_PAL_ALIGN(LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES)
    dummy_element;

  struct lfds711_list_asu_element LFDS711_PAL_ALIGN(LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES)
    *volatile end;

  struct lfds711_list_asu_element LFDS711_PAL_ALIGN(LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES)
    *start;

  void
    *user_state;

  struct lfds711_misc_backoff_state
    after_backoff,
    end_backoff,
    start_backoff;
};

/***** public prototypes *****/
void lfds711_list_asu_init_valid_on_current_logical_core( struct lfds711_list_asu_state *lasus,
                                                          void *user_state );
  // TRD : used in conjunction with the #define LFDS711_MISC_MAKE_VALID_ON_CURRENT_LOGICAL_CORE_INITS_COMPLETED_BEFORE_NOW_ON_ANY_OTHER_LOGICAL_CORE

void lfds711_list_asu_cleanup( struct lfds711_list_asu_state *lasus,
                               void (*element_cleanup_callback)(struct lfds711_list_asu_state *lasus, struct lfds711_list_asu_element *lasue) );

void lfds711_list_asu_insert_at_position( struct lfds711_list_asu_state *lasus,
                                          struct lfds711_list_asu_element *lasue,
                                          struct lfds711_list_asu_element *lasue_predecessor,
                                          enum lfds711_list_asu_position position );

void lfds711_list_asu_insert_at_start( struct lfds711_list_asu_state *lasus,
                                       struct lfds711_list_asu_element *lasue );

void lfds711_list_asu_insert_at_end( struct lfds711_list_asu_state *lasus,
                                     struct lfds711_list_asu_element *lasue );

void lfds711_list_asu_insert_after_element( struct lfds711_list_asu_state *lasus,
                                            struct lfds711_list_asu_element *lasue,
                                            struct lfds711_list_asu_element *lasue_predecessor );

int lfds711_list_asu_get_by_key( struct lfds711_list_asu_state *lasus,
                                 int (*key_compare_function)(void const *new_key, void const *existing_key),
                                 void *key, 
                                 struct lfds711_list_asu_element **lasue );

void lfds711_list_asu_query( struct lfds711_list_asu_state *lasus,
                             enum lfds711_list_asu_query query_type,
                             void *query_input,
                             void *query_output );

