/***** defines *****/
#define LFDS711_BTREE_AU_GET_KEY_FROM_ELEMENT( btree_au_element )             ( (btree_au_element).key )
#define LFDS711_BTREE_AU_SET_KEY_IN_ELEMENT( btree_au_element, new_key )      ( (btree_au_element).key = (void *) (lfds711_pal_uint_t) (new_key) )
#define LFDS711_BTREE_AU_GET_VALUE_FROM_ELEMENT( btree_au_element )           ( LFDS711_MISC_BARRIER_LOAD, (btree_au_element).value )
#define LFDS711_BTREE_AU_SET_VALUE_IN_ELEMENT( btree_au_element, new_value )  { LFDS711_PAL_ATOMIC_SET( &(btree_au_element).value, new_value ); }
#define LFDS711_BTREE_AU_GET_USER_STATE_FROM_STATE( btree_au_state )          ( (btree_au_state).user_state )

/***** enums *****/
enum lfds711_btree_au_absolute_position
{
  LFDS711_BTREE_AU_ABSOLUTE_POSITION_ROOT,
  LFDS711_BTREE_AU_ABSOLUTE_POSITION_SMALLEST_IN_TREE,
  LFDS711_BTREE_AU_ABSOLUTE_POSITION_LARGEST_IN_TREE
};

enum lfds711_btree_au_existing_key
{
  LFDS711_BTREE_AU_EXISTING_KEY_OVERWRITE,
  LFDS711_BTREE_AU_EXISTING_KEY_FAIL
};

enum lfds711_btree_au_insert_result
{
  LFDS711_BTREE_AU_INSERT_RESULT_FAILURE_EXISTING_KEY,
  LFDS711_BTREE_AU_INSERT_RESULT_SUCCESS_OVERWRITE,
  LFDS711_BTREE_AU_INSERT_RESULT_SUCCESS
};

enum lfds711_btree_au_query
{
  LFDS711_BTREE_AU_QUERY_GET_POTENTIALLY_INACCURATE_COUNT,
  LFDS711_BTREE_AU_QUERY_SINGLETHREADED_VALIDATE
};

enum lfds711_btree_au_relative_position
{
  LFDS711_BTREE_AU_RELATIVE_POSITION_UP,
  LFDS711_BTREE_AU_RELATIVE_POSITION_LEFT,
  LFDS711_BTREE_AU_RELATIVE_POSITION_RIGHT,
  LFDS711_BTREE_AU_RELATIVE_POSITION_SMALLEST_ELEMENT_BELOW_CURRENT_ELEMENT,
  LFDS711_BTREE_AU_RELATIVE_POSITION_LARGEST_ELEMENT_BELOW_CURRENT_ELEMENT,
  LFDS711_BTREE_AU_RELATIVE_POSITION_NEXT_SMALLER_ELEMENT_IN_ENTIRE_TREE,
  LFDS711_BTREE_AU_RELATIVE_POSITION_NEXT_LARGER_ELEMENT_IN_ENTIRE_TREE
};

/***** structs *****/
struct lfds711_btree_au_element
{
  /* TRD : we are add-only, so these elements are only written once
           as such, the write is wholly negligible
           we are only concerned with getting as many structs in one cache line as we can
  */

  struct lfds711_btree_au_element LFDS711_PAL_ALIGN(LFDS711_PAL_ALIGN_SINGLE_POINTER)
    *volatile left,
    *volatile right,
    *volatile up;

  void LFDS711_PAL_ALIGN(LFDS711_PAL_ALIGN_SINGLE_POINTER)
    *volatile value;

  void
    *key;
};

struct lfds711_btree_au_state
{
  struct lfds711_btree_au_element LFDS711_PAL_ALIGN(LFDS711_PAL_ALIGN_SINGLE_POINTER)
    *volatile root;

  int
    (*key_compare_function)( void const *new_key, void const *existing_key );

  enum lfds711_btree_au_existing_key 
    existing_key;

  void
    *user_state;

  struct lfds711_misc_backoff_state
    insert_backoff;
};

/***** public prototypes *****/
void lfds711_btree_au_init_valid_on_current_logical_core( struct lfds711_btree_au_state *baus,
                                                          int (*key_compare_function)(void const *new_key, void const *existing_key),
                                                          enum lfds711_btree_au_existing_key existing_key,
                                                          void *user_state );
  // TRD : used in conjunction with the #define LFDS711_MISC_MAKE_VALID_ON_CURRENT_LOGICAL_CORE_INITS_COMPLETED_BEFORE_NOW_ON_ANY_OTHER_LOGICAL_CORE

void lfds711_btree_au_cleanup( struct lfds711_btree_au_state *baus,
                               void (*element_cleanup_callback)(struct lfds711_btree_au_state *baus, struct lfds711_btree_au_element *baue) );

enum lfds711_btree_au_insert_result lfds711_btree_au_insert( struct lfds711_btree_au_state *baus,
                                                             struct lfds711_btree_au_element *baue,
                                                             struct lfds711_btree_au_element **existing_baue );
  // TRD : if a link collides with an existing key and existing_baue is non-NULL, existing_baue is set to the existing element

int lfds711_btree_au_get_by_key( struct lfds711_btree_au_state *baus, 
                                 int (*key_compare_function)(void const *new_key, void const *existing_key),
                                 void *key,
                                 struct lfds711_btree_au_element **baue );

int lfds711_btree_au_get_by_absolute_position_and_then_by_relative_position( struct lfds711_btree_au_state *baus,
                                                                             struct lfds711_btree_au_element **baue,
                                                                             enum lfds711_btree_au_absolute_position absolute_position,
                                                                             enum lfds711_btree_au_relative_position relative_position );
  // TRD : if *baue is NULL, we get the element at position, otherwise we move from *baue according to direction

int lfds711_btree_au_get_by_absolute_position( struct lfds711_btree_au_state *baus,
                                               struct lfds711_btree_au_element **baue,
                                               enum lfds711_btree_au_absolute_position absolute_position );

int lfds711_btree_au_get_by_relative_position( struct lfds711_btree_au_element **baue,
                                               enum lfds711_btree_au_relative_position relative_position );

void lfds711_btree_au_query( struct lfds711_btree_au_state *baus,
                             enum lfds711_btree_au_query query_type,
                             void *query_input,
                             void *query_output );

