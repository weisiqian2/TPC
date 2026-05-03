#ifndef TPCWEI_PROXY_MANAGER_H
#define TPCWEI_PROXY_MANAGER_H

#include "p2p_core.h"
#include "tunnel_manager.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef uint64_t P2PProxyHandle;

typedef enum P2PProxyType {
    P2P_PROXY_TYPE_TCP = 0,
    P2P_PROXY_TYPE_UDP = 1,
    P2P_PROXY_TYPE_HTTP = 2,
    P2P_PROXY_TYPE_HTTPS = 3,
    P2P_PROXY_TYPE_STCP = 4,
    P2P_PROXY_TYPE_SUDP = 5,
    P2P_PROXY_TYPE_XTCP = 6,
    P2P_PROXY_TYPE_TCPMUX = 7,
    P2P_PROXY_TYPE_PORT_RANGE = 8
} P2PProxyType;

typedef enum P2PProxyMode {
    P2P_PROXY_MODE_AUTO = 0,
    P2P_PROXY_MODE_P2P = 1,
    P2P_PROXY_MODE_GATEWAY = 2,
    P2P_PROXY_MODE_SECRET = 3,
    P2P_PROXY_MODE_SMART_DIRECT = 4
} P2PProxyMode;

typedef enum P2PProxyEvent {
    P2P_PROXY_EVENT_STARTED = 0,
    P2P_PROXY_EVENT_STOPPED = 1,
    P2P_PROXY_EVENT_TRAFFIC = 2,
    P2P_PROXY_EVENT_HEALTH_CHANGED = 3,
    P2P_PROXY_EVENT_DIAGNOSTIC = 4,
    P2P_PROXY_EVENT_ERROR = 5
} P2PProxyEvent;

typedef struct P2PProxyMetrics {
    uint64_t bytes_up;
    uint64_t bytes_down;
    uint32_t active_connections;
    uint32_t error_count;
    uint32_t reconnect_count;
    uint32_t health_score;
    uint16_t local_port;
    uint16_t remote_port;
    uint16_t public_port;
    P2PProxyType type;
    P2PProxyMode mode;
    uint8_t running;
} P2PProxyMetrics;

typedef void (P2P_CALL *P2PProxyCallback)(
    P2PProxyHandle proxy,
    P2PProxyEvent event_type,
    int error_code,
    const char* message,
    const P2PProxyMetrics* metrics,
    void* user_data);

P2P_EXPORT int P2P_CALL p2p_proxy_set_callback(P2PProxyCallback callback, void* user_data);
P2P_EXPORT int P2P_CALL p2p_proxy_validate_json(const char* profile_json, char* message_buffer, uint32_t message_buffer_length);
P2P_EXPORT int P2P_CALL p2p_proxy_start_json(P2PNodeHandle node, const char* profile_json, P2PProxyHandle* out_proxy);
P2P_EXPORT int P2P_CALL p2p_proxy_stop(P2PProxyHandle proxy);
P2P_EXPORT int P2P_CALL p2p_proxy_get_metrics(P2PProxyHandle proxy, P2PProxyMetrics* out_metrics);

#ifdef __cplusplus
}
#endif

#endif
