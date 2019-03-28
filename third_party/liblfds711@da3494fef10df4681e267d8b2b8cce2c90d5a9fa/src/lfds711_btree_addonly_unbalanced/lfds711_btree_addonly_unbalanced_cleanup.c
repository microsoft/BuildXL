/***** includes *****/
#include "lfds711_btree_addonly_unbalanced_internal.h"





/****************************************************************************/
void lfds711_btree_au_cleanup( struct lfds711_btree_au_state *baus,
                               void (*element_cleanup_callback)(struct lfds711_btree_au_state *baus, struct lfds711_btree_au_element *baue) )
{
  enum lfds711_btree_au_delete_action
    delete_action = LFDS711_BTREE_AU_DELETE_SELF; // TRD : to remove compiler warning

  struct lfds711_btree_au_element
    *baue;

  struct lfds711_btree_au_element
    *temp;

  LFDS711_PAL_ASSERT( baus != NULL );
  // TRD : element_delete_function can be NULL

  /* TRD : we're not lock-free now, so delete at will
           but be iterative, so can be used in kernels (where there's little stack)
           and be performant, since the user may be
           creating/destroying many of these trees
           also remember the user may be deallocating user data
           so we cannot visit an element twice

           we start at the root and iterate till we go to NULL
           if the element has zero children, we delete it and move up to its parent
           if the element has one child, we delete it, move its child into its place, and continue from its child
           if the element has two children, we move left

           the purpose of this is to minimize walking around the tree
           to prevent visiting an element twice
           while also minimizing code complexity
  */

  if( element_cleanup_callback == NULL )
    return;

  LFDS711_MISC_BARRIER_LOAD;

  lfds711_btree_au_get_by_absolute_position( baus, &baue, LFDS711_BTREE_AU_ABSOLUTE_POSITION_ROOT );

  while( baue != NULL )
  {
    if( baue->left == NULL and baue->right == NULL )
      delete_action = LFDS711_BTREE_AU_DELETE_SELF;

    if( baue->left != NULL and baue->right == NULL )
      delete_action = LFDS711_BTREE_AU_DELETE_SELF_REPLACE_WITH_LEFT_CHILD;

    if( baue->left == NULL and baue->right != NULL )
      delete_action = LFDS711_BTREE_AU_DELETE_SELF_REPLACE_WITH_RIGHT_CHILD;

    if( baue->left != NULL and baue->right != NULL )
      delete_action = LFDS711_BTREE_AU_DELETE_MOVE_LEFT;

    switch( delete_action )
    {
      case LFDS711_BTREE_AU_DELETE_SELF:
        // TRD : if we have a parent (we could be root) set his point to us to NULL
        if( baue->up != NULL )
        {
          if( baue->up->left == baue )
            baue->up->left = NULL;
          if( baue->up->right == baue )
            baue->up->right = NULL;
        }

        temp = baue;
        lfds711_btree_au_get_by_relative_position( &baue, LFDS711_BTREE_AU_RELATIVE_POSITION_UP );
        element_cleanup_callback( baus, temp );
      break;

      case LFDS711_BTREE_AU_DELETE_SELF_REPLACE_WITH_LEFT_CHILD:
        baue->left->up = baue->up;
        if( baue->up != NULL )
        {
          if( baue->up->left == baue )
            baue->up->left = baue->left;
          if( baue->up->right == baue )
            baue->up->right = baue->left;
        }

        temp = baue;
        lfds711_btree_au_get_by_relative_position( &baue, LFDS711_BTREE_AU_RELATIVE_POSITION_LEFT );
        element_cleanup_callback( baus, temp );
      break;

      case LFDS711_BTREE_AU_DELETE_SELF_REPLACE_WITH_RIGHT_CHILD:
        baue->right->up = baue->up;
        if( baue->up != NULL )
        {
          if( baue->up->left == baue )
            baue->up->left = baue->right;
          if( baue->up->right == baue )
            baue->up->right = baue->right;
        }

        temp = baue;
        lfds711_btree_au_get_by_relative_position( &baue, LFDS711_BTREE_AU_RELATIVE_POSITION_RIGHT );
        element_cleanup_callback( baus, temp );
      break;

      case LFDS711_BTREE_AU_DELETE_MOVE_LEFT:
        lfds711_btree_au_get_by_relative_position( &baue, LFDS711_BTREE_AU_RELATIVE_POSITION_LEFT );
      break;
    }
  }

  return;
}

