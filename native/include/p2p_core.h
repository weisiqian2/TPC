#ifndef TPCWEI_P2P_CORE_H
#define TPCWEI_P2P_CORE_H

#include <stdint.h>

#if defined(_WIN32)
#  if defined(P2P_CORE_EXPORTS)
#    define P2P_EXPORT __declspec(dllexport)
#  else
#    define P2P_EXPORT __declspec(dllimport)
#  endif
#  define P2P_CALL __cdecl
#else
#  define P2P_EXPORT __attribute__((visibility("default")))
#  define P2P_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef uint64_t P2PNodeHandle;
typedef uint64_t P2PTransferHandle;
typedef uint64_t P2PStreamHandle;
typedef uint64_t P2PTunnelHandle;

typedef enum P2PErrorCode {
    P2P_OK = 0,
    P2P_ERROR_UNKNOWN = -1,
    P2P_ERROR_NOT_INITIALIZED = -2,
    P2P_ERROR_ALREADY_INITIALIZED = -3,
    P2P_ERROR_INVALID_ARGUMENT = -4,
    P2P_ERROR_SOCKET = -5,
    P2P_ERROR_BIND_FAILED = -6,
    P2P_ERROR_CONNECT_FAILED = -7,
    P2P_ERROR_TIMEOUT = -8,
    P2P_ERROR_NOT_FOUND = -9,
    P2P_ERROR_UNSUPPORTED = -10,
    P2P_ERROR_CANCELLED = -11,
    P2P_ERROR_IO = -12,
    P2P_ERROR_BUFFER_TOO_SMALL = -13,
    P2P_ERROR_PROTOCOL = -14,
    P2P_ERROR_SECURITY = -15
} P2PErrorCode;

typedef enum P2PNatTraversalState {
    P2P_NAT_STATE_IDLE = 0,
    P2P_NAT_STATE_DISCOVERING = 1,
    P2P_NAT_STATE_PUNCHING_UDP = 2,
    P2P_NAT_STATE_PUNCHING_TCP = 3,
    P2P_NAT_STATE_UPNP_MAPPING = 4,
    P2P_NAT_STATE_CONNECTED = 5,
    P2P_NAT_STATE_FAILED = 6
} P2PNatTraversalState;

typedef struct P2PNodeConfig {
    const char* bind_address;
    uint16_t local_port;
    uint32_t worker_threads;
    uint8_t enable_lan_discovery;
    uint8_t enable_upnp;
} P2PNodeConfig;

typedef struct P2PNodeMetrics {
    uint64_t bytes_sent;
    uint64_t bytes_received;
    uint32_t active_streams;
    uint32_t active_transfers;
    uint16_t udp_port;
    uint16_t tcp_port;
} P2PNodeMetrics;

P2P_EXPORT int P2P_CALL p2p_initialize(void);
P2P_EXPORT int P2P_CALL p2p_shutdown(void);
P2P_EXPORT int P2P_CALL p2p_node_start(const P2PNodeConfig* config, P2PNodeHandle* out_node);
P2P_EXPORT int P2P_CALL p2p_node_stop(P2PNodeHandle node);
P2P_EXPORT int P2P_CALL p2p_node_get_metrics(P2PNodeHandle node, P2PNodeMetrics* out_metrics);
P2P_EXPORT int P2P_CALL p2p_get_last_error(char* buffer, uint32_t buffer_length);

#ifdef __cplusplus
}
#endif

#endif
