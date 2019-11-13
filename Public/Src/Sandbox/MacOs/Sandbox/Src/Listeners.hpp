// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef Listeners_hpp
#define Listeners_hpp

#include <sys/vnode.h>

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-W#warnings"

extern "C" {
    #include <mac.h>
    #include <mac_policy.h>
    #include <mac_framework.h>
}

#pragma clang diagnostic pop

class Listeners
{
public:
    // The dispatcher will point to our BuildXLSandbox instance. Because the TrustedBSD MAC listeners are static in nature
    // and brought in via C symbols, we need to have a pointer to the Sandbox so we can send access reports too. Using
    // a member function pointer poses more challenges and unreadable syntax so we go with a direct void pointer instead!
    static void *g_dispatcher;

    static int buildxl_file_op_listener(kauth_cred_t credential,
                                       void *idata,
                                       kauth_action_t action,
                                       uintptr_t arg0,
                                       uintptr_t arg1,
                                       uintptr_t arg2,
                                       uintptr_t arg3);

    static int buildxl_vnode_listener(kauth_cred_t credential,
                                     void *idata,
                                     kauth_action_t action,
                                     uintptr_t arg0,
                                     uintptr_t arg1,
                                     uintptr_t arg2,
                                     uintptr_t arg3);

    static int mpo_vnode_check_lookup(kauth_cred_t cred,
                                      struct vnode *dvp,
                                      struct label *dlabel,
                                      struct componentname *cnp);

    static int mpo_vnode_check_lookup_pre(kauth_cred_t cred,
                                          struct vnode *dvp,
                                          struct label *dlabel,
                                          const char *path,
                                          size_t pathlen);

    static int mpo_vnode_check_exec(kauth_cred_t cred,
                                    struct vnode *vp,
                                    struct vnode *scriptvp,
                                    struct label *vnodelabel,
                                    struct label *scriptlabel,
                                    struct label *execlabel,
                                    struct componentname *cnp,
                                    u_int *csflags,
                                    void *macpolicyattr,
                                    size_t macpolicyattrlen);

    static void mpo_proc_notify_exit(proc_t proc);

    static int mpo_cred_label_update_execve(kauth_cred_t old_cred,
                                            kauth_cred_t new_cred,
                                            struct proc *p,
                                            struct vnode *vp,
                                            off_t offset,
                                            struct vnode *scriptvp,
                                            struct label *vnodelabel,
                                            struct label *scriptvnodelabel,
                                            struct label *execlabel,
                                            u_int *csflags,
                                            void *macpolicyattr,
                                            size_t macpolicyattrlen,
                                            int *disjointp);

    static int mpo_proc_check_fork(kauth_cred_t cred, proc_t proc);

    static void mpo_cred_label_associate_fork(kauth_cred_t cred, proc_t proc);

    static int mpo_vnode_check_create(kauth_cred_t cred,
                                      struct vnode *dvp,
                                      struct label *dlabel,
                                      struct componentname *cnp,
                                      struct vnode_attr *vap);

    static int mpo_vnode_check_readlink(kauth_cred_t cred,
                                        struct vnode *vp,
                                        struct label *label);


    static int mpo_vnode_check_write(kauth_cred_t active_cred,
                                     kauth_cred_t file_cred,
                                     struct vnode *vp,
                                     struct label *label);

    static int mpo_vnode_check_clone(kauth_cred_t cred,
                                     struct vnode *dvp,
                                     struct label *dlabel,
                                     struct vnode *vp,
                                     struct label *label,
                                     struct componentname *cnp);
};

#endif /* Listeners_hpp */
