#ifndef TPCWEI_STREAM_MANAGER_H
#define TPCWEI_STREAM_MANAGER_H

#include "p2p_core.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef enum P2PStreamState {
    P2P_STREAM_CLOSED = 0,
    P2P_STREAM_CONNECTING = 1,
    P2P_STREAM_OPEN = 2,
    P2P_STREAM_FAILED = 3
} P2PStreamState;

typedef struct P2PStreamOptions {
    const char* peer_host;
    uint16_t peer_port;
    uint8_t reliable;
    uint32_t max_frame_size;
} P2PStreamOptions;

typedef struct P2PStreamMetrics {
    uint32_t latency_ms;
    float packet_loss;
    uint64_t bytes_sent;
    uint64_t bytes_received;
    P2PStreamState state;
} P2PStreamMetrics;

typedef void (P2P_CALL *P2PStreamReceiveCallback)(
    P2PStreamHandle stream,
    const uint8_t* data,
    uint32_t length,
    void* user_data);

P2P_EXPORT int P2P_CALL p2p_stream_create(P2PNodeHandle node, const P2PStreamOptions* options, P2PStreamHandle* out_stream);
P2P_EXPORT int P2P_CALL p2p_stream_close(P2PStreamHandle stream);
P2P_EXPORT int P2P_CALL p2p_stream_send_frame(P2PStreamHandle stream, const uint8_t* data, uint32_t length);
P2P_EXPORT int P2P_CALL p2p_stream_set_receive_callback(P2PStreamHandle stream, P2PStreamReceiveCallback callback, void* user_data);
P2P_EXPORT int P2P_CALL p2p_stream_get_metrics(P2PStreamHandle stream, P2PStreamMetrics* out_metrics);

#ifdef __cplusplus
}
#endif

#endif
