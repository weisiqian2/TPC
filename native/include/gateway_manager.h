#ifndef TPCWEI_GATEWAY_MANAGER_H
#define TPCWEI_GATEWAY_MANAGER_H

#include "p2p_core.h"
#include "tunnel_manager.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef uint64_t P2PGatewayHandle;
typedef uint64_t P2PGatewayTunnelHandle;

typedef enum P2PGatewayEvent {
    P2P_GATEWAY_EVENT_CONNECTED = 0,
    P2P_GATEWAY_EVENT_DISCONNECTED = 1,
    P2P_GATEWAY_EVENT_TUNNEL_REGISTERED = 2,
    P2P_GATEWAY_EVENT_TUNNEL_STOPPED = 3,
    P2P_GATEWAY_EVENT_CONNECTION_OPENED = 4,
    P2P_GATEWAY_EVENT_CONNECTION_CLOSED = 5,
    P2P_GATEWAY_EVENT_TRAFFIC = 6,
    P2P_GATEWAY_EVENT_ERROR = 7,
    P2P_GATEWAY_EVENT_DIAGNOSTIC = 8
} P2PGatewayEvent;

typedef struct P2PGatewayConfig {
    const char* gateway_host;
    uint16_t control_port;
    const char* token;
    const char* room_code;
    const char* public_code;
    uint8_t auto_reconnect;
} P2PGatewayConfig;

typedef struct P2PGatewayTunnelOptions {
    const char* name;
    const char* local_host;
    uint16_t local_port;
    uint16_t public_port;
    P2PTunnelProtocol protocol;
    uint8_t allow_udp_over_gateway;
} P2PGatewayTunnelOptions;

typedef struct P2PGatewayMetrics {
    uint64_t bytes_up;
    uint64_t bytes_down;
    uint32_t active_connections;
    uint32_t reconnect_count;
    uint32_t error_count;
    uint8_t running;
} P2PGatewayMetrics;

typedef void (P2P_CALL *P2PGatewayCallback)(
    P2PGatewayHandle gateway,
    P2PGatewayTunnelHandle tunnel,
    P2PGatewayEvent event_type,
    int error_code,
    const char* message,
    const P2PGatewayMetrics* metrics,
    void* user_data);

P2P_EXPORT int P2P_CALL p2p_gateway_set_callback(P2PGatewayCallback callback, void* user_data);
P2P_EXPORT int P2P_CALL p2p_gateway_connect(const P2PGatewayConfig* config, P2PGatewayHandle* out_gateway);
P2P_EXPORT int P2P_CALL p2p_gateway_disconnect(P2PGatewayHandle gateway);
P2P_EXPORT int P2P_CALL p2p_gateway_start_tunnel(P2PGatewayHandle gateway, const P2PGatewayTunnelOptions* options, P2PGatewayTunnelHandle* out_tunnel);
P2P_EXPORT int P2P_CALL p2p_gateway_stop_tunnel(P2PGatewayTunnelHandle tunnel);
P2P_EXPORT int P2P_CALL p2p_gateway_get_metrics(P2PGatewayHandle gateway, P2PGatewayTunnelHandle tunnel, P2PGatewayMetrics* out_metrics);

#ifdef __cplusplus
}
#endif

#endif
