/***** includes *****/
#include "lfds711_btree_addonly_unbalanced_internal.h"

/***** private prototypes *****/
static void lfds711_btree_au_internal_inorder_walk_from_largest_get_next_smallest_element( struct lfds711_btree_au_element **baue );
static void lfds711_btree_au_internal_inorder_walk_from_smallest_get_next_largest_element( struct lfds711_btree_au_element **baue );





/****************************************************************************/
int lfds711_btree_au_get_by_key( struct lfds711_btree_au_state *baus,
                                 int (*key_compare_function)(void const *new_key, void const *existing_key),
                                 void *key,
                                 struct lfds711_btree_au_element **baue )
{
  int
    compare_result = !0,
    rv = 1;

  LFDS711_PAL_ASSERT( baus != NULL );
  // TRD : key_compare_function can be NULL
  // TRD : key can be NULL
  LFDS711_PAL_ASSERT( baue != NULL );

  if( key_compare_function == NULL )
    key_compare_function = baus->key_compare_function;

  LFDS711_MISC_BARRIER_LOAD;

  *baue = baus->root;

  LFDS711_MISC_BARRIER_LOAD;

  while( *baue != NULL and compare_result != 0 )
  {
    compare_result = key_compare_function( key, (*baue)->key );

    if( compare_result < 0 )
    {
      *baue = (*baue)->left;
      LFDS711_MISC_BARRIER_LOAD;
    }

    if( compare_result > 0 )
    {
      *baue = (*baue)->right;
      LFDS711_MISC_BARRIER_LOAD;
    }
  }

  if( *baue == NULL )
    rv = 0;

  return rv;
}





/****************************************************************************/
int lfds711_btree_au_get_by_absolute_position( struct lfds711_btree_au_state *baus,
                                               struct lfds711_btree_au_element **baue,
                                               enum lfds711_btree_au_absolute_position absolute_position )
{
  int
    rv = 1;

  LFDS711_PAL_ASSERT( baus != NULL );
  LFDS711_PAL_ASSERT( baue != NULL );
  // TRD : absolute_position can be any value in its range

  LFDS711_MISC_BARRIER_LOAD;

  *baue = baus->root;

  LFDS711_MISC_BARRIER_LOAD;

  switch( absolute_position )
  {
    case LFDS711_BTREE_AU_ABSOLUTE_POSITION_ROOT:
    break;

    case LFDS711_BTREE_AU_ABSOLUTE_POSITION_LARGEST_IN_TREE:
      if( *baue != NULL )
        while( (*baue)->right != NULL )
        {
          *baue = (*baue)->right;
          LFDS711_MISC_BARRIER_LOAD;
        }
    break;

    case LFDS711_BTREE_AU_ABSOLUTE_POSITION_SMALLEST_IN_TREE:
      if( *baue != NULL )
        while( (*baue)->left != NULL )
        {
          *baue = (*baue)->left;
          LFDS711_MISC_BARRIER_LOAD;
        }
    break;
  }

  if( *baue == NULL )
    rv = 0;

  return rv;
}





/****************************************************************************/
int lfds711_btree_au_get_by_relative_position( struct lfds711_btree_au_element **baue,
                                               enum lfds711_btree_au_relative_position relative_position )
{
  int
    rv = 1;

  LFDS711_PAL_ASSERT( baue != NULL );
  // TRD : relative_position can baue any value in its range

  if( *baue == NULL )
    return 0;

  LFDS711_MISC_BARRIER_LOAD;

  switch( relative_position )
  {
    case LFDS711_BTREE_AU_RELATIVE_POSITION_UP:
      *baue = (*baue)->up;
      // TRD : no load barrier - up already existed, so is known to be safely propagated
    break;

    case LFDS711_BTREE_AU_RELATIVE_POSITION_LEFT:
      *baue = (*baue)->left;
      LFDS711_MISC_BARRIER_LOAD;
    break;

    case LFDS711_BTREE_AU_RELATIVE_POSITION_RIGHT:
      *baue = (*baue)->right;
      LFDS711_MISC_BARRIER_LOAD;
    break;

    case LFDS711_BTREE_AU_RELATIVE_POSITION_SMALLEST_ELEMENT_BELOW_CURRENT_ELEMENT:
      *baue = (*baue)->left;
      if( *baue != NULL )
      {
        LFDS711_MISC_BARRIER_LOAD;
        while( (*baue)->right != NULL )
        {
          *baue = (*baue)->right;
          LFDS711_MISC_BARRIER_LOAD;
        }
      }
    break;

    case LFDS711_BTREE_AU_RELATIVE_POSITION_LARGEST_ELEMENT_BELOW_CURRENT_ELEMENT:
      *baue = (*baue)->right;
      if( *baue != NULL )
      {
        LFDS711_MISC_BARRIER_LOAD;
        while( (*baue)->left != NULL )
        {
          *baue = (*baue)->left;
          LFDS711_MISC_BARRIER_LOAD;
        }
      }
    break;

    case LFDS711_BTREE_AU_RELATIVE_POSITION_NEXT_SMALLER_ELEMENT_IN_ENTIRE_TREE:
      lfds711_btree_au_internal_inorder_walk_from_largest_get_next_smallest_element( baue );
    break;

    case LFDS711_BTREE_AU_RELATIVE_POSITION_NEXT_LARGER_ELEMENT_IN_ENTIRE_TREE:
      lfds711_btree_au_internal_inorder_walk_from_smallest_get_next_largest_element( baue );
    break;
  }

  if( *baue == NULL )
    rv = 0;

  return rv;
}





/****************************************************************************/
static void lfds711_btree_au_internal_inorder_walk_from_largest_get_next_smallest_element( struct lfds711_btree_au_element **baue )
{
  enum lfds711_btree_au_move
    action = LFDS711_BTREE_AU_MOVE_INVALID;

  enum lfds711_misc_flag
    finished_flag = LFDS711_MISC_FLAG_LOWERED,
    load_finished_flag = LFDS711_MISC_FLAG_LOWERED;

  struct lfds711_btree_au_element
    *left = NULL,
    *right = NULL,
    *up = NULL,
    *up_left = NULL,
    *up_right = NULL;

  LFDS711_PAL_ASSERT( baue != NULL );

  /* TRD : from any given element, the next smallest element is;
           1. if we have a left, it's the largest element on the right branch of our left child
           2. if we don't have a left, and we're on the right of our parent, then it's our parent
           3. if we don't have a left, and we're on the left of our parent or we have no parent,
              iterative up the tree until we find the first child who is on the right of its parent; then it's the parent
  */

  /* TRD : we need to ensure the variables we use to decide our action are self-consistent
           to do this, we make local copies of them all
           then, if they are all not NULL, we can know they cannot change and we can continue
           if however any of them are NULL, they could have changed while we were reading
           and so our variables could be non-self-consistent
           to check for this, we issue another processor read barrier
           and then compare our local variables with the values in the tree
           if they all match, then we know our variable set is self-consistent
           (even though it may now be wrong - but we will discover this when we try the atomic operation)
  */

  LFDS711_MISC_BARRIER_LOAD;

  while( load_finished_flag == LFDS711_MISC_FLAG_LOWERED )
  {
    left = (*baue)->left;
    right = (*baue)->right;
    up = (*baue)->up;
    if( up != NULL )
    {
      up_left = (*baue)->up->left;
      up_right = (*baue)->up->right;
    }

    // TRD : optimization - if all already not NULL, given we're add-only, they won't change
    if( left != NULL and right != NULL and (up == NULL or (up != NULL and up_left != NULL and up_right != NULL)) )
      break;

    LFDS711_MISC_BARRIER_LOAD;

    if( left == (*baue)->left and right == (*baue)->right and (up == NULL or (up != NULL and up == (*baue)->up and up_left == (*baue)->up->left and up_right == (*baue)->up->right)) )
      load_finished_flag = LFDS711_MISC_FLAG_RAISED;
  }

  if( left != NULL )
    action = LFDS711_BTREE_AU_MOVE_LARGEST_FROM_LEFT_CHILD;

  if( left == NULL and up != NULL and up_right == *baue )
    action = LFDS711_BTREE_AU_MOVE_GET_PARENT;

  if( (left == NULL and up == NULL) or (up != NULL and up_left == *baue and left == NULL) )
    action = LFDS711_BTREE_AU_MOVE_MOVE_UP_TREE;

  switch( action )
  {
    case LFDS711_BTREE_AU_MOVE_INVALID:
    case LFDS711_BTREE_AU_MOVE_SMALLEST_FROM_RIGHT_CHILD:
      // TRD : eliminates a compiler warning
    break;

    case LFDS711_BTREE_AU_MOVE_LARGEST_FROM_LEFT_CHILD:
      *baue = left;
      if( *baue != NULL )
      {
        LFDS711_MISC_BARRIER_LOAD;
        while( (*baue)->right != NULL )
        {
          *baue = (*baue)->right;
          LFDS711_MISC_BARRIER_LOAD;
        }
      }
    break;

    case LFDS711_BTREE_AU_MOVE_GET_PARENT:
      *baue = up;
    break;

    case LFDS711_BTREE_AU_MOVE_MOVE_UP_TREE:
      while( finished_flag == LFDS711_MISC_FLAG_LOWERED )
      {
        load_finished_flag = LFDS711_MISC_FLAG_LOWERED;

        while( load_finished_flag == LFDS711_MISC_FLAG_LOWERED )
        {
          up = (*baue)->up;
          if( up != NULL )
            up_left = (*baue)->up->left;

          // TRD : optimization - if all already not NULL, given we're add-only, they won't change
          if( up == NULL or (up != NULL and up_left != NULL) )
            break;

          LFDS711_MISC_BARRIER_LOAD;

          if( up == (*baue)->up and up_left == (*baue)->up->left )
            load_finished_flag = LFDS711_MISC_FLAG_RAISED;
        }

        if( *baue != NULL and up != NULL and *baue == up_left )
          *baue = up;
        else
          finished_flag = LFDS711_MISC_FLAG_RAISED;
      }

      *baue = up;

      /*

      while( *baue != NULL and (*baue)->up != NULL and *baue == (*baue)->up->left )
        *baue = (*baue)->up;

      *baue = (*baue)->up;

      */
    break;
  }

  return;
}





/****************************************************************************/
static void lfds711_btree_au_internal_inorder_walk_from_smallest_get_next_largest_element( struct lfds711_btree_au_element **baue )
{
  enum lfds711_btree_au_move
    action = LFDS711_BTREE_AU_MOVE_INVALID;

  enum lfds711_misc_flag
    finished_flag = LFDS711_MISC_FLAG_LOWERED,
    load_finished_flag = LFDS711_MISC_FLAG_LOWERED;

  struct lfds711_btree_au_element
    *left = NULL,
    *right = NULL,
    *up = NULL,
    *up_left = NULL,
    *up_right = NULL;

  LFDS711_PAL_ASSERT( baue != NULL );

  /* TRD : from any given element, the next largest element is;
           1. if we have a right, it's the smallest element on the left branch of our right child
           2. if we don't have a right, and we're on the left of our parent, then it's our parent
           3. if we don't have a right, and we're on the right of our parent or we have no parent,
              iterate up the tree until we find the first child who is on the left of its parent; then it's the parent
  */

  LFDS711_MISC_BARRIER_LOAD;

  while( load_finished_flag == LFDS711_MISC_FLAG_LOWERED )
  {
    left = (*baue)->left;
    right = (*baue)->right;
    up = (*baue)->up;
    if( up != NULL )
    {
      up_left = (*baue)->up->left;
      up_right = (*baue)->up->right;
    }

    // TRD : optimization - if all already not NULL, given we're add-only, they won't change
    if( left != NULL and right != NULL and (up == NULL or (up != NULL and up_left != NULL and up_right != NULL)) )
      break;

    LFDS711_MISC_BARRIER_LOAD;

    if( left == (*baue)->left and right == (*baue)->right and (up == NULL or (up != NULL and up == (*baue)->up and up_left == (*baue)->up->left and up_right == (*baue)->up->right)) )
      load_finished_flag = LFDS711_MISC_FLAG_RAISED;
  }

  if( right != NULL )
    action = LFDS711_BTREE_AU_MOVE_SMALLEST_FROM_RIGHT_CHILD;

  if( right == NULL and up != NULL and up_left == *baue )
    action = LFDS711_BTREE_AU_MOVE_GET_PARENT;

  if( (right == NULL and up == NULL) or (up != NULL and up_right == *baue and right == NULL) )
    action = LFDS711_BTREE_AU_MOVE_MOVE_UP_TREE;

  switch( action )
  {
    case LFDS711_BTREE_AU_MOVE_INVALID:
    case LFDS711_BTREE_AU_MOVE_LARGEST_FROM_LEFT_CHILD:
      // TRD : remove compiler warning
    break;

    case LFDS711_BTREE_AU_MOVE_SMALLEST_FROM_RIGHT_CHILD:
      *baue = right;
      if( *baue != NULL )
      {
        LFDS711_MISC_BARRIER_LOAD;
        while( (*baue)->left != NULL )
        {
          *baue = (*baue)->left;
          LFDS711_MISC_BARRIER_LOAD;
        }
      }
    break;

    case LFDS711_BTREE_AU_MOVE_GET_PARENT:
      *baue = up;
    break;

    case LFDS711_BTREE_AU_MOVE_MOVE_UP_TREE:
      while( finished_flag == LFDS711_MISC_FLAG_LOWERED )
      {
        load_finished_flag = LFDS711_MISC_FLAG_LOWERED;

        while( load_finished_flag == LFDS711_MISC_FLAG_LOWERED )
        {
          up = (*baue)->up;
          if( up != NULL )
            up_right = (*baue)->up->right;

          // TRD : optimization - if all already not NULL, given we're add-only, they won't change
          if( up == NULL or (up != NULL and up_right != NULL) )
            break;

          LFDS711_MISC_BARRIER_LOAD;

          if( up == (*baue)->up and up_right == (*baue)->up->right )
            load_finished_flag = LFDS711_MISC_FLAG_RAISED;
        }

        if( *baue != NULL and up != NULL and *baue == up_right )
          *baue = up;
        else
          finished_flag = LFDS711_MISC_FLAG_RAISED;
      }

      *baue = up;

      /*

      while( *baue != NULL and (*baue)->up != NULL and *baue == (*baue)->up->right )
        *baue = (*baue)->up;

      *baue = (*baue)->up;

      */
    break;
  }

  return;
}





/****************************************************************************/
int lfds711_btree_au_get_by_absolute_position_and_then_by_relative_position( struct lfds711_btree_au_state *baus,
                                                                             struct lfds711_btree_au_element **baue,
                                                                             enum lfds711_btree_au_absolute_position absolute_position,
                                                                             enum lfds711_btree_au_relative_position relative_position )
{
  int
    rv;

  LFDS711_PAL_ASSERT( baus != NULL );
  LFDS711_PAL_ASSERT( baue != NULL );
  // TRD: absolute_position can be any value in its range
  // TRD: relative_position can be any value in its range

  if( *baue == NULL )
    rv = lfds711_btree_au_get_by_absolute_position( baus, baue, absolute_position );
  else
    rv = lfds711_btree_au_get_by_relative_position( baue, relative_position );

  return rv;
}

