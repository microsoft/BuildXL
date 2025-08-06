#include <functional>

#include <bpf/bpf.h>
#include "bpf/bpf.h"
#include "bpf/libbpf.h"
#include "bpf/btf.h"
#include "bpf/libbpf_common.h"
#include "sandbox.skel.h"
#include "bxl_observer.hpp"
#include "ConcurrentQueue.h"
#include "ebpfcommon.h"
#include "EventRingBuffer.hpp"

int g_file_access_per_pip_fd = -1;
std::atomic<int> g_capacity_exceeded_called_counter = 0;
BxlObserver *g_bxl = nullptr;
volatile sig_atomic_t g_root_process_exited = 0;
buildxl::common::ConcurrentQueue<ebpf_event *> g_event_queue;
std::atomic<buildxl::linux::ebpf::EventRingBuffer *> g_active_ring_buffer;
int g_event_number = 0;
int g_test_write_ringbuf_fd = -1;

int DoExceedCapacity(buildxl::linux::ebpf::EventRingBuffer *buffer);

void RingBufferOutOfSpaceCallback(buildxl::linux::ebpf::EventRingBuffer *buffer) {
    g_capacity_exceeded_called_counter++;

    printf("Capacity exceeded callback is called %d time(s)\n", g_capacity_exceeded_called_counter.load());

    // Create a new overflow buffer to handle the overflow of the current ring buffer.
    buildxl::linux::ebpf::OverflowEventRingBuffer *overflow_buffer = new buildxl::linux::ebpf::OverflowEventRingBuffer(
        g_bxl, 
        &g_root_process_exited, 
        g_event_queue, 
        RingBufferOutOfSpaceCallback,
        buffer);

    if (overflow_buffer->Initialize())
    {
        delete overflow_buffer;
        return;
    }

    int ring_buffer_fd = overflow_buffer->GetRingBufferFd();

    int key = 0;
    if (bpf_map_update_elem(g_file_access_per_pip_fd, &key, &ring_buffer_fd, BPF_ANY))
    {
        overflow_buffer->NotifyDeactivated();
        delete overflow_buffer;
        return;
    }

    // We exceed the capacity one more time (to exercise the overflow buffer on top of an overflow buffer case).
    if (g_capacity_exceeded_called_counter < 2) {
        printf("Try to exceed capacity again\n");
        DoExceedCapacity(overflow_buffer);
    }

    // Start the overflow polling thread to start emptying the new ring buffer.
    overflow_buffer->NotifyActivated();

    // Swap the active ring buffer to the new overflow buffer. We keep the active ring buffer on this global variable so we can finally
    // wait for it to be done when the runner is about to exit.
    g_active_ring_buffer.store(overflow_buffer);

    // Notify the last buffer that it has been deactivated.
    // This will cause it to wait for the grace period to be over and then move the events from the overflow queue to the main event queue.
    // After the grace period is over, the overflow buffer will automatically release the associated ring buffer.
    buffer->NotifyDeactivated();

}

static inline __u64 ptr_to_u64(const void *ptr)
{
	return (__u64)(unsigned long)ptr;
}

/** Retrieves the program full name of a given bpf_prog_info */
void GetProgramFullName(const struct bpf_prog_info *prog_info, int prog_fd, char *name_buff, size_t buff_len)
{
    const char *prog_name = prog_info->name;
    const struct btf_type *func_type;
    struct bpf_func_info finfo = {};
    struct bpf_prog_info info = {};
    __u32 info_len = sizeof(info);
    struct btf *prog_btf = NULL;

    // If the name is 16 chars or left, it is already contained in the info object
    if (buff_len <= BPF_OBJ_NAME_LEN || strlen(prog_info->name) < BPF_OBJ_NAME_LEN - 1) {
        goto copy_name;
    }

    if (!prog_info->btf_id || prog_info->nr_func_info == 0) {
        goto copy_name;
    }

    info.nr_func_info = 1;
    info.func_info_rec_size = prog_info->func_info_rec_size;
    if (info.func_info_rec_size > sizeof(finfo)) {
        info.func_info_rec_size = sizeof(finfo);
    }
    info.func_info = ptr_to_u64(&finfo);

    // Retrieve full info of the program
    if (bpf_prog_get_info_by_fd(prog_fd, &info, &info_len)) {
        goto copy_name;
    }

    // Load corresponding BTF object
    prog_btf = btf__load_from_kernel_by_id(info.btf_id);
    if (!prog_btf) {
        goto copy_name;
    }

    // Retrieve the function associated to the program and get the name
    func_type = btf__type_by_id(prog_btf, finfo.type_id);
    if (!func_type || !btf_is_func(func_type)) {
        goto copy_name;
    }

    prog_name = btf__name_by_offset(prog_btf, func_type->name_off);

    copy_name:
    snprintf(name_buff, buff_len, "%s", prog_name);

    if (prog_btf) {
        btf__free(prog_btf);
    }
}

int GetTestProgramFd()
{
     __u32 id = 0;
    int err, fd = 0;
    char prog_name[128];

    // Iterate over all bpf programs
    while (true) {
        err = bpf_prog_get_next_id(id, &id);
        if (err) {
            break;
        }

        fd = bpf_prog_get_fd_by_id(id);
        if (fd < 0) {
            continue;
        }

        // We got a program with a valid file descriptor, retrieve its info
        struct bpf_prog_info info = {};
        __u32 len = sizeof(info);

        err = bpf_obj_get_info_by_fd(fd, &info, &len);
        if (err || !info.name)
        {
            continue;
        }
        // Check whether we find a program that is our loading witness
        // (this is just an arbitrarily picked program among all the ones we load)
        GetProgramFullName(&info, fd, prog_name, sizeof(prog_name));

        if (strcmp(prog_name, "test_write_ringbuf") == 0) {
            return fd;
        }

        close(fd);
	}

    return -1;
}

int InitEBPF(){
    struct sandbox_bpf *skel;

    skel = sandbox_bpf::open();
    if (!skel) {
        printf("Failed to open BPF skeleton\n");
        return 1;
    }

    int pin_fd = bpf_obj_get(bpf_map__get_pin_path(skel->maps.file_access_per_pip));
    if (pin_fd < 0)
    {
        printf("Error getting pin path: %s\n", strerror(errno));
        return -1;    
    }
    int err = bpf_map__reuse_fd(skel->maps.file_access_per_pip, pin_fd);
    close(pin_fd);
    if (err)
    {
        printf("Cannot reuse pinned map\n");
        return -1;    
    }
    
    // Retrieve the per-pip file access outer map and create the file access ring buffer
    g_file_access_per_pip_fd = bpf_object__find_map_fd_by_name(skel->obj, "file_access_per_pip");
    if (g_file_access_per_pip_fd < 0) {
        printf("finding file_access_per_pip in obj file failed\n");
        return -1;
    }

    g_test_write_ringbuf_fd = GetTestProgramFd();
    if (g_test_write_ringbuf_fd < 0) {
        fprintf(stderr, "Failed to get fd for test_write_ringbuf program: %s\n", strerror(errno));
        return 1;
    }

    return 0;
}

int CallWriteRingBufferTest(int number) {
    test_write_ringbuf_args args = {
        .runner_pid = 0,
        .number = number
    };

    LIBBPF_OPTS(bpf_test_run_opts, test_run_opts,
        .ctx_in = &args,
        .ctx_size_in = sizeof(args),
    );


    bpf_prog_test_run_opts(g_test_write_ringbuf_fd, &test_run_opts);
    
    if (test_run_opts.retval != 0) {
        fprintf(stderr, "failed to test run test_write_ringbuf_fd: %d - %s\n", test_run_opts.retval, strerror(test_run_opts.retval));
        return 1;
    }

    return 0;
}

int CallWriteRingBufferTest() {
    return CallWriteRingBufferTest(g_event_number++);
}   

int DoExceedCapacity(buildxl::linux::ebpf::EventRingBuffer *buffer) {
    // Before we activate the buffer, push enough events to trigger the capacity exceeded callback.
    while (buffer->GetCapacityThreshold() < buffer->GetAvailableSpace()) {
        if (CallWriteRingBufferTest())
        {
            return -1;
        }
    }

    return 0;
}

int main(int argc, char **argv) {
    // Initialize the BxlObserver
    g_bxl = BxlObserver::GetInstance();
    g_bxl->Init();

    // Initialize the EBPF subsystem
    if (InitEBPF())
    {
        return -1;
    }

    printf("EBPF initialized successfully\n");

    volatile sig_atomic_t stopSignal = 0;
    buildxl::linux::ebpf::EventRingBuffer* ringBuffer = new buildxl::linux::ebpf::EventRingBuffer(g_bxl, &g_root_process_exited, &stopSignal, g_event_queue, RingBufferOutOfSpaceCallback);

    g_active_ring_buffer.store(ringBuffer);

    ringBuffer->Initialize();

    int ringBufferFd = ringBuffer->GetRingBufferFd();

    // The pid 0 is never a user process, so we can use it as a key for the per-pip file access outer map.
    int key = 0;
    if (bpf_map_update_elem(g_file_access_per_pip_fd, &key, &ringBufferFd, BPF_ANY))
    {
        printf("Failed to replace file access ring buffer to outer map for runner PID %d: %s\n", key, strerror(errno));
        return -1;
    }

    printf("Try to exceed capacity for the first time. Capacity threshold: %d, available capacity: %d\n", ringBuffer->GetCapacityThreshold(), ringBuffer->GetAvailableSpace());

    // Before we activate the buffer, push enough events to trigger the capacity exceeded callback.
    if (DoExceedCapacity(ringBuffer))
    {
        printf("Failed to exceed capacity\n");
        return -1;
    }

    // Activate the buffer to start polling the ring buffer. This should also trigger the capacity exceeded callback.
    printf("Buffer activated\n");
    ringBuffer->NotifyActivated();
    // Write a new event after the capacity exceeded callback has been called.
    CallWriteRingBufferTest();

    // Wait for the capacity exceeded callback to be called twice.
    // The first time an overflow buffer is created on top of a regular buffer.
    // The second time an overflow buffer is created on top of an overflow buffer.
    while (g_capacity_exceeded_called_counter < 2) {
       usleep(100000); // Sleep for 100ms
    }

    stopSignal = 1;

    g_active_ring_buffer.load()->NotifyDeactivated();

    g_active_ring_buffer.load()->WaitForInactive();

    printf("Buffer inactive: queue size %d\n", g_event_queue.Size());

    delete g_active_ring_buffer.load();

    printf("Checking message order\n");

    // Now the queue should have all the events that were pushed to the ring buffer.
    // Just check they are all in order
    ebpf_event *event = nullptr;
    int expected_number = 0;

    while (g_event_queue.Size() != 0) {
        g_event_queue.Dequeue(event);
        assert(event->metadata.event_type == DEBUG);
        ebpf_event_debug* event_debug = (ebpf_event_debug*) event;

        std::string message(event_debug->message);
        
        // Messages are in the format ""Test message number: %d""
        message = message.substr(message.find(':') + 1); 

        int msg_nr = std::stoi(message); 
        if (msg_nr != expected_number) {
            printf("Message number %d is out of the expected order %d\n", msg_nr, expected_number);
            return -1;
        }
        expected_number++;

        delete event;
    }

    printf("All messages in order\n");

    close(g_test_write_ringbuf_fd);

    printf("Test successful\n");

    return 0;
}
