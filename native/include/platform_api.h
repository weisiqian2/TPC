#ifndef TPCWEI_PLATFORM_API_H
#define TPCWEI_PLATFORM_API_H

#include "p2p_core.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef uint64_t P2PPlatformHandle;

P2P_EXPORT int P2P_CALL p2p_mesh_start_json(const char* config_json, P2PPlatformHandle* out_mesh);
P2P_EXPORT int P2P_CALL p2p_route_get_candidates(const char* request_json, char* response_json_buffer, uint32_t response_json_buffer_length);
P2P_EXPORT int P2P_CALL p2p_identity_authorize_json(const char* policy_json, char* response_json_buffer, uint32_t response_json_buffer_length);
P2P_EXPORT int P2P_CALL p2p_audit_query_json(const char* query_json, char* response_json_buffer, uint32_t response_json_buffer_length);
P2P_EXPORT int P2P_CALL p2p_dht_start_json(const char* config_json, P2PPlatformHandle* out_dht);
P2P_EXPORT int P2P_CALL p2p_game_vlan_start_json(const char* config_json, P2PPlatformHandle* out_session);
P2P_EXPORT int P2P_CALL p2p_remote_session_start_json(const char* config_json, P2PPlatformHandle* out_session);
P2P_EXPORT int P2P_CALL p2p_file_sync_start_json(const char* config_json, P2PPlatformHandle* out_task);
P2P_EXPORT int P2P_CALL p2p_plugin_load_json(const char* manifest_json, char* response_json_buffer, uint32_t response_json_buffer_length);
P2P_EXPORT int P2P_CALL p2p_ai_diagnose_json(const char* diagnostics_json, char* response_json_buffer, uint32_t response_json_buffer_length);

#ifdef __cplusplus
}
#endif

#endif
