#ifndef TPCWEI_TUNNEL_MANAGER_H
#define TPCWEI_TUNNEL_MANAGER_H

#include "p2p_core.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef enum P2PTunnelProtocol {
    P2P_TUNNEL_PROTOCOL_TCP = 0,
    P2P_TUNNEL_PROTOCOL_UDP = 1
} P2PTunnelProtocol;

typedef enum P2PTunnelEvent {
    P2P_TUNNEL_EVENT_STARTED = 0,
    P2P_TUNNEL_EVENT_STOPPED = 1,
    P2P_TUNNEL_EVENT_CONNECTION_OPENED = 2,
    P2P_TUNNEL_EVENT_CONNECTION_CLOSED = 3,
    P2P_TUNNEL_EVENT_TRAFFIC = 4,
    P2P_TUNNEL_EVENT_ERROR = 5
} P2PTunnelEvent;

typedef struct P2PTunnelOptions {
    const char* local_bind_address;
    uint16_t local_port;
    const char* peer_host;
    uint16_t peer_port;
    P2PTunnelProtocol protocol;
    uint8_t aggressive_reconnect;
    uint8_t allow_lan_clients;
} P2PTunnelOptions;

typedef struct P2PTunnelMetrics {
    uint64_t bytes_up;
    uint64_t bytes_down;
    uint32_t active_connections;
    uint16_t local_port;
    uint16_t peer_port;
    P2PTunnelProtocol protocol;
    uint8_t running;
} P2PTunnelMetrics;

typedef void (P2P_CALL *P2PTunnelCallback)(
    P2PTunnelHandle tunnel,
    P2PTunnelEvent event_type,
    const P2PTunnelMetrics* metrics,
    void* user_data);

P2P_EXPORT int P2P_CALL p2p_tunnel_set_callback(P2PTunnelCallback callback, void* user_data);
P2P_EXPORT int P2P_CALL p2p_tunnel_start(P2PNodeHandle node, const P2PTunnelOptions* options, P2PTunnelHandle* out_tunnel);
P2P_EXPORT int P2P_CALL p2p_tunnel_stop(P2PTunnelHandle tunnel);
P2P_EXPORT int P2P_CALL p2p_tunnel_get_metrics(P2PTunnelHandle tunnel, P2PTunnelMetrics* out_metrics);

#ifdef __cplusplus
}
#endif

#endif
