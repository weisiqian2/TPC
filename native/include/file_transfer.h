#ifndef TPCWEI_FILE_TRANSFER_H
#define TPCWEI_FILE_TRANSFER_H

#include "p2p_core.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef enum P2PTransferStatus {
    P2P_TRANSFER_PENDING = 0,
    P2P_TRANSFER_RUNNING = 1,
    P2P_TRANSFER_COMPLETED = 2,
    P2P_TRANSFER_CANCELLED = 3,
    P2P_TRANSFER_FAILED = 4
} P2PTransferStatus;

typedef enum P2PTransferEvent {
    P2P_TRANSFER_EVENT_STARTED = 0,
    P2P_TRANSFER_EVENT_PROGRESS = 1,
    P2P_TRANSFER_EVENT_COMPLETED = 2,
    P2P_TRANSFER_EVENT_CANCELLED = 3,
    P2P_TRANSFER_EVENT_FAILED = 4
} P2PTransferEvent;

typedef struct P2PFileTransferOptions {
    const char* local_path;
    const char* remote_path;
    const char* peer_host;
    uint16_t peer_port;
    uint8_t resume_enabled;
    uint8_t parallel_paths;
    uint32_t chunk_size;
} P2PFileTransferOptions;

typedef struct P2PTransferProgress {
    uint64_t total_bytes;
    uint64_t transferred_bytes;
    uint64_t bytes_per_second;
    float progress;
    P2PTransferStatus status;
} P2PTransferProgress;

typedef void (P2P_CALL *P2PTransferCallback)(
    P2PTransferHandle transfer,
    P2PTransferEvent event_type,
    const P2PTransferProgress* progress,
    void* user_data);

P2P_EXPORT int P2P_CALL p2p_file_set_callback(P2PTransferCallback callback, void* user_data);
P2P_EXPORT int P2P_CALL p2p_file_send(P2PNodeHandle node, const P2PFileTransferOptions* options, P2PTransferHandle* out_transfer);
P2P_EXPORT int P2P_CALL p2p_file_receive(P2PNodeHandle node, const P2PFileTransferOptions* options, P2PTransferHandle* out_transfer);
P2P_EXPORT int P2P_CALL p2p_file_get_progress(P2PTransferHandle transfer, P2PTransferProgress* out_progress);
P2P_EXPORT int P2P_CALL p2p_file_cancel(P2PTransferHandle transfer);

#ifdef __cplusplus
}
#endif

#endif
