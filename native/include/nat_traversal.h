#ifndef TPCWEI_NAT_TRAVERSAL_H
#define TPCWEI_NAT_TRAVERSAL_H

#include "p2p_core.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef enum P2PNatType {
    P2P_NAT_TYPE_UNKNOWN = 0,
    P2P_NAT_TYPE_OPEN_INTERNET = 1,
    P2P_NAT_TYPE_FULL_CONE = 2,
    P2P_NAT_TYPE_RESTRICTED_CONE = 3,
    P2P_NAT_TYPE_PORT_RESTRICTED_CONE = 4,
    P2P_NAT_TYPE_SYMMETRIC = 5,
    P2P_NAT_TYPE_BLOCKED = 6
} P2PNatType;

typedef enum P2PNatEvent {
    P2P_NAT_EVENT_STATE_CHANGED = 0,
    P2P_NAT_EVENT_UDP_PUNCH_SUCCESS = 1,
    P2P_NAT_EVENT_UDP_PUNCH_FAILED = 2,
    P2P_NAT_EVENT_TCP_PUNCH_SUCCESS = 3,
    P2P_NAT_EVENT_TCP_PUNCH_FAILED = 4,
    P2P_NAT_EVENT_UPNP_MAPPING_SUCCESS = 5,
    P2P_NAT_EVENT_UPNP_MAPPING_FAILED = 6,
    P2P_NAT_EVENT_EXTERNAL_ENDPOINT = 7
} P2PNatEvent;

typedef void (P2P_CALL *P2PNatCallback)(
    P2PNatEvent event_type,
    int error_code,
    const char* address,
    uint16_t port,
    void* user_data);

P2P_EXPORT int P2P_CALL p2p_nat_get_type(P2PNatType* out_nat_type);
P2P_EXPORT int P2P_CALL p2p_nat_set_callback(P2PNatCallback callback, void* user_data);
P2P_EXPORT int P2P_CALL p2p_nat_udp_punch(P2PNodeHandle node, const char* peer_host, uint16_t peer_port, uint32_t attempts, uint32_t interval_ms);
P2P_EXPORT int P2P_CALL p2p_nat_tcp_punch(P2PNodeHandle node, const char* peer_host, uint16_t peer_port, uint32_t timeout_ms);
P2P_EXPORT int P2P_CALL p2p_nat_upnp_add_mapping(uint16_t local_port, uint16_t external_port, const char* protocol, uint32_t lease_seconds);
P2P_EXPORT int P2P_CALL p2p_nat_upnp_remove_mapping(uint16_t external_port, const char* protocol);
P2P_EXPORT int P2P_CALL p2p_nat_get_external_endpoint(char* address_buffer, uint32_t address_buffer_length, uint16_t* out_port);

#ifdef __cplusplus
}
#endif

#endif
