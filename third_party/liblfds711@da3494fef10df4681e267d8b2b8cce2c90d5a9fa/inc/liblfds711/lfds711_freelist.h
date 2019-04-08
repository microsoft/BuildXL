/***** defines *****/
#define LFDS711_FREELIST_GET_KEY_FROM_ELEMENT( freelist_element )             ( (freelist_element).key )
#define LFDS711_FREELIST_SET_KEY_IN_ELEMENT( freelist_element, new_key )      ( (freelist_element).key = (void *) (lfds711_pal_uint_t) (new_key) )
#define LFDS711_FREELIST_GET_VALUE_FROM_ELEMENT( freelist_element )           ( (freelist_element).value )
#define LFDS711_FREELIST_SET_VALUE_IN_ELEMENT( freelist_element, new_value )  ( (freelist_element).value = (void *) (lfds711_pal_uint_t) (new_value) )
#define LFDS711_FREELIST_GET_USER_STATE_FROM_STATE( freelist_state )          ( (freelist_state).user_state )

#define LFDS711_FREELIST_ELIMINATION_ARRAY_ELEMENT_SIZE_IN_FREELIST_ELEMENTS  ( LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES / sizeof(struct lfds711_freelist_element *) )

/***** enums *****/
enum lfds711_freelist_query
{
  LFDS711_FREELIST_QUERY_SINGLETHREADED_GET_COUNT,
  LFDS711_FREELIST_QUERY_SINGLETHREADED_VALIDATE,
  LFDS711_FREELIST_QUERY_GET_ELIMINATION_ARRAY_EXTRA_ELEMENTS_IN_FREELIST_ELEMENTS
};

/***** structures *****/
struct lfds711_freelist_element
{
  struct lfds711_freelist_element
    *next;

  void
    *key,
    *value;
};

struct lfds711_freelist_state
{
  struct lfds711_freelist_element LFDS711_PAL_ALIGN(LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES)
    *volatile top[PAC_SIZE];

  lfds711_pal_uint_t LFDS711_PAL_ALIGN(LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES)
    elimination_array_size_in_elements;

  struct lfds711_freelist_element * volatile
    (*elimination_array)[LFDS711_FREELIST_ELIMINATION_ARRAY_ELEMENT_SIZE_IN_FREELIST_ELEMENTS];

  void
    *user_state;

  struct lfds711_misc_backoff_state
    pop_backoff,
    push_backoff;
};

/***** public prototypes *****/
void lfds711_freelist_init_valid_on_current_logical_core( struct lfds711_freelist_state *fs,
                                                          struct lfds711_freelist_element * volatile (*elimination_array)[LFDS711_FREELIST_ELIMINATION_ARRAY_ELEMENT_SIZE_IN_FREELIST_ELEMENTS],
                                                          lfds711_pal_uint_t elimination_array_size_in_elements,
                                                          void *user_state );
  // TRD : used in conjunction with the #define LFDS711_MISC_MAKE_VALID_ON_CURRENT_LOGICAL_CORE_INITS_COMPLETED_BEFORE_NOW_ON_ANY_OTHER_LOGICAL_CORE

void lfds711_freelist_cleanup( struct lfds711_freelist_state *fs,
                               void (*element_cleanup_callback)(struct lfds711_freelist_state *fs, struct lfds711_freelist_element *fe) );

void lfds711_freelist_push( struct lfds711_freelist_state *fs,
                                   struct lfds711_freelist_element *fe,
                                   struct lfds711_prng_st_state *psts );

int lfds711_freelist_pop( struct lfds711_freelist_state *fs,
                          struct lfds711_freelist_element **fe,
                          struct lfds711_prng_st_state *psts );

void lfds711_freelist_query( struct lfds711_freelist_state *fs,
                             enum lfds711_freelist_query query_type,
                             void *query_input,
                             void *query_output );

