#include "proxy_manager.h"

#include <algorithm>
#include <atomic>
#include <cctype>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <map>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <vector>

namespace {

std::atomic_uint64_t g_next_proxy{1};
std::mutex g_callback_mutex;
P2PProxyCallback g_callback = nullptr;
void* g_callback_user = nullptr;

struct ProxyProfile {
    std::string name = "TPCwei Proxy";
    std::string type_text = "tcp";
    std::string mode_text = "auto";
    std::string local_host = "127.0.0.1";
    std::string peer_host = "127.0.0.1";
    uint16_t local_port = 0;
    uint16_t remote_port = 0;
    uint16_t public_port = 0;
    bool prefer_p2p = true;
    bool allow_gateway_fallback = true;
    bool compression = false;
    bool encryption = true;
};

struct ProxyInstance {
    P2PProxyHandle handle = 0;
    P2PTunnelHandle tunnel = 0;
    ProxyProfile profile;
    std::atomic_bool running{false};
    std::atomic_uint32_t error_count{0};
    std::atomic_uint32_t reconnect_count{0};
};

std::mutex g_proxy_mutex;
std::map<P2PProxyHandle, std::shared_ptr<ProxyInstance>> g_proxies;

std::string trim(const std::string& value) {
    const auto begin = value.find_first_not_of(" \t\r\n");
    if (begin == std::string::npos) {
        return {};
    }
    const auto end = value.find_last_not_of(" \t\r\n");
    return value.substr(begin, end - begin + 1);
}

std::string to_lower(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return value;
}

bool copy_to_buffer(const std::string& value, char* buffer, uint32_t buffer_length) {
    if (!buffer || buffer_length == 0) {
        return false;
    }
    if (value.size() + 1 > buffer_length) {
        return false;
    }
    std::memcpy(buffer, value.c_str(), value.size() + 1);
    return true;
}

std::string json_unescape(const std::string& value) {
    std::string result;
    result.reserve(value.size());
    bool escaped = false;
    for (char ch : value) {
        if (escaped) {
            switch (ch) {
            case 'n': result.push_back('\n'); break;
            case 'r': result.push_back('\r'); break;
            case 't': result.push_back('\t'); break;
            case '"': result.push_back('"'); break;
            case '\\': result.push_back('\\'); break;
            default: result.push_back(ch); break;
            }
            escaped = false;
        } else if (ch == '\\') {
            escaped = true;
        } else {
            result.push_back(ch);
        }
    }
    return result;
}

bool json_string(const std::string& json, const std::string& key, std::string* out) {
    const std::string needle = "\"" + key + "\"";
    size_t pos = json.find(needle);
    if (pos == std::string::npos) {
        return false;
    }
    pos = json.find(':', pos + needle.size());
    if (pos == std::string::npos) {
        return false;
    }
    pos = json.find('"', pos + 1);
    if (pos == std::string::npos) {
        return false;
    }
    std::string value;
    bool escaped = false;
    for (++pos; pos < json.size(); ++pos) {
        const char ch = json[pos];
        if (!escaped && ch == '"') {
            if (out) {
                *out = json_unescape(value);
            }
            return true;
        }
        if (!escaped && ch == '\\') {
            escaped = true;
            value.push_back(ch);
            continue;
        }
        escaped = false;
        value.push_back(ch);
    }
    return false;
}

bool json_uint(const std::string& json, const std::string& key, uint16_t* out) {
    const std::string needle = "\"" + key + "\"";
    size_t pos = json.find(needle);
    if (pos == std::string::npos) {
        return false;
    }
    pos = json.find(':', pos + needle.size());
    if (pos == std::string::npos) {
        return false;
    }
    ++pos;
    while (pos < json.size() && std::isspace(static_cast<unsigned char>(json[pos]))) {
        ++pos;
    }
    size_t end = pos;
    while (end < json.size() && std::isdigit(static_cast<unsigned char>(json[end]))) {
        ++end;
    }
    if (end == pos) {
        return false;
    }
    const unsigned long value = std::strtoul(json.substr(pos, end - pos).c_str(), nullptr, 10);
    if (value == 0 || value > 65535) {
        return false;
    }
    if (out) {
        *out = static_cast<uint16_t>(value);
    }
    return true;
}

bool json_bool(const std::string& json, const std::string& key, bool* out) {
    const std::string needle = "\"" + key + "\"";
    size_t pos = json.find(needle);
    if (pos == std::string::npos) {
        return false;
    }
    pos = json.find(':', pos + needle.size());
    if (pos == std::string::npos) {
        return false;
    }
    ++pos;
    while (pos < json.size() && std::isspace(static_cast<unsigned char>(json[pos]))) {
        ++pos;
    }
    if (json.compare(pos, 4, "true") == 0) {
        if (out) {
            *out = true;
        }
        return true;
    }
    if (json.compare(pos, 5, "false") == 0) {
        if (out) {
            *out = false;
        }
        return true;
    }
    return false;
}

P2PProxyType proxy_type_from_text(const std::string& text) {
    const std::string value = to_lower(text);
    if (value == "udp" || value == "sudp") {
        return value == "sudp" ? P2P_PROXY_TYPE_SUDP : P2P_PROXY_TYPE_UDP;
    }
    if (value == "http") {
        return P2P_PROXY_TYPE_HTTP;
    }
    if (value == "https" || value == "sni") {
        return P2P_PROXY_TYPE_HTTPS;
    }
    if (value == "stcp") {
        return P2P_PROXY_TYPE_STCP;
    }
    if (value == "xtcp") {
        return P2P_PROXY_TYPE_XTCP;
    }
    if (value == "tcpmux") {
        return P2P_PROXY_TYPE_TCPMUX;
    }
    if (value == "range" || value == "port-range") {
        return P2P_PROXY_TYPE_PORT_RANGE;
    }
    return P2P_PROXY_TYPE_TCP;
}

P2PProxyMode proxy_mode_from_text(const std::string& text) {
    const std::string value = to_lower(text);
    if (value == "p2p") {
        return P2P_PROXY_MODE_P2P;
    }
    if (value == "gateway") {
        return P2P_PROXY_MODE_GATEWAY;
    }
    if (value == "secret" || value == "private" || value == "stcp" || value == "sudp") {
        return P2P_PROXY_MODE_SECRET;
    }
    if (value == "smart" || value == "xtcp" || value == "smart-direct") {
        return P2P_PROXY_MODE_SMART_DIRECT;
    }
    return P2P_PROXY_MODE_AUTO;
}

bool profile_from_json(const char* profile_json, ProxyProfile* out, std::string* error) {
    if (!profile_json || !out) {
        if (error) {
            *error = "Profile JSON 为空";
        }
        return false;
    }

    const std::string json = trim(profile_json);
    if (json.empty() || json.front() != '{') {
        if (error) {
            *error = "Profile 必须是 JSON 对象";
        }
        return false;
    }

    ProxyProfile profile;
    json_string(json, "name", &profile.name);
    json_string(json, "type", &profile.type_text);
    json_string(json, "mode", &profile.mode_text);
    json_string(json, "localHost", &profile.local_host);
    if (!json_string(json, "peerHost", &profile.peer_host)) {
        json_string(json, "remoteHost", &profile.peer_host);
    }
    json_uint(json, "localPort", &profile.local_port);
    if (!json_uint(json, "remotePort", &profile.remote_port)) {
        json_uint(json, "peerPort", &profile.remote_port);
    }
    if (!json_uint(json, "publicPort", &profile.public_port)) {
        profile.public_port = profile.remote_port;
    }
    json_bool(json, "preferP2p", &profile.prefer_p2p);
    json_bool(json, "allowGatewayFallback", &profile.allow_gateway_fallback);
    json_bool(json, "compression", &profile.compression);
    json_bool(json, "encryption", &profile.encryption);

    if (profile.local_port == 0) {
        if (error) {
            *error = "Profile 缺少 localPort";
        }
        return false;
    }
    if (profile.remote_port == 0) {
        if (error) {
            *error = "Profile 缺少 remotePort";
        }
        return false;
    }
    if (profile.peer_host.empty()) {
        profile.peer_host = "127.0.0.1";
    }
    if (profile.local_host.empty()) {
        profile.local_host = "127.0.0.1";
    }

    *out = std::move(profile);
    return true;
}

P2PProxyMetrics metrics_for(const std::shared_ptr<ProxyInstance>& proxy) {
    P2PProxyMetrics metrics{};
    if (!proxy) {
        return metrics;
    }
    P2PTunnelMetrics tunnel_metrics{};
    if (proxy->tunnel != 0 && p2p_tunnel_get_metrics(proxy->tunnel, &tunnel_metrics) == P2P_OK) {
        metrics.bytes_up = tunnel_metrics.bytes_up;
        metrics.bytes_down = tunnel_metrics.bytes_down;
        metrics.active_connections = tunnel_metrics.active_connections;
        metrics.running = tunnel_metrics.running;
    } else {
        metrics.running = proxy->running.load() ? 1 : 0;
    }
    metrics.error_count = proxy->error_count.load();
    metrics.reconnect_count = proxy->reconnect_count.load();
    metrics.health_score = metrics.error_count == 0 ? 100 : (metrics.error_count < 5 ? 70 : 35);
    metrics.local_port = proxy->profile.local_port;
    metrics.remote_port = proxy->profile.remote_port;
    metrics.public_port = proxy->profile.public_port;
    metrics.type = proxy_type_from_text(proxy->profile.type_text);
    metrics.mode = proxy_mode_from_text(proxy->profile.mode_text);
    return metrics;
}

void notify_proxy(const std::shared_ptr<ProxyInstance>& proxy, P2PProxyEvent event_type, int error_code, const std::string& message) {
    P2PProxyCallback callback = nullptr;
    void* user = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_callback_mutex);
        callback = g_callback;
        user = g_callback_user;
    }
    if (!callback) {
        return;
    }
    const P2PProxyMetrics metrics = metrics_for(proxy);
    callback(proxy ? proxy->handle : 0, event_type, error_code, message.c_str(), &metrics, user);
}

std::shared_ptr<ProxyInstance> get_proxy(P2PProxyHandle handle) {
    std::lock_guard<std::mutex> lock(g_proxy_mutex);
    auto it = g_proxies.find(handle);
    return it == g_proxies.end() ? nullptr : it->second;
}

} // namespace

extern "C" {

P2P_EXPORT int P2P_CALL p2p_proxy_set_callback(P2PProxyCallback callback, void* user_data) {
    std::lock_guard<std::mutex> lock(g_callback_mutex);
    g_callback = callback;
    g_callback_user = user_data;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_proxy_validate_json(const char* profile_json, char* message_buffer, uint32_t message_buffer_length) {
    ProxyProfile profile;
    std::string error;
    if (!profile_from_json(profile_json, &profile, &error)) {
        copy_to_buffer(error, message_buffer, message_buffer_length);
        return static_cast<int>(P2P_ERROR_INVALID_ARGUMENT);
    }

    std::ostringstream message;
    message << "Profile 有效：" << profile.name << " "
            << to_lower(profile.type_text) << " "
            << profile.local_host << ":" << profile.local_port
            << " -> " << profile.peer_host << ":" << profile.remote_port;
    copy_to_buffer(message.str(), message_buffer, message_buffer_length);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_proxy_start_json(P2PNodeHandle node, const char* profile_json, P2PProxyHandle* out_proxy) {
    if (!out_proxy) {
        return static_cast<int>(P2P_ERROR_INVALID_ARGUMENT);
    }

    ProxyProfile profile;
    std::string error;
    if (!profile_from_json(profile_json, &profile, &error)) {
        return static_cast<int>(P2P_ERROR_INVALID_ARGUMENT);
    }

    const P2PProxyType type = proxy_type_from_text(profile.type_text);
    P2PTunnelOptions tunnel_options{};
    tunnel_options.local_bind_address = profile.local_host.c_str();
    tunnel_options.local_port = profile.local_port;
    tunnel_options.peer_host = profile.peer_host.c_str();
    tunnel_options.peer_port = profile.remote_port;
    tunnel_options.protocol = (type == P2P_PROXY_TYPE_UDP || type == P2P_PROXY_TYPE_SUDP)
        ? P2P_TUNNEL_PROTOCOL_UDP
        : P2P_TUNNEL_PROTOCOL_TCP;
    tunnel_options.aggressive_reconnect = 1;
    tunnel_options.allow_lan_clients = profile.local_host == "0.0.0.0" ? 1 : 0;

    P2PTunnelHandle tunnel = 0;
    const int result = p2p_tunnel_start(node, &tunnel_options, &tunnel);
    if (result != P2P_OK) {
        return result;
    }

    auto proxy = std::make_shared<ProxyInstance>();
    proxy->handle = g_next_proxy.fetch_add(1);
    proxy->tunnel = tunnel;
    proxy->profile = std::move(profile);
    proxy->running.store(true);

    {
        std::lock_guard<std::mutex> lock(g_proxy_mutex);
        g_proxies[proxy->handle] = proxy;
    }

    *out_proxy = proxy->handle;
    notify_proxy(proxy, P2P_PROXY_EVENT_STARTED, P2P_OK, "代理规则已启动");
    if (proxy_mode_from_text(proxy->profile.mode_text) == P2P_PROXY_MODE_SMART_DIRECT) {
        notify_proxy(proxy, P2P_PROXY_EVENT_DIAGNOSTIC, P2P_OK, "智能直连已进入路径竞速：P2P 优先，网关保底");
    }
    if (proxy_mode_from_text(proxy->profile.mode_text) == P2P_PROXY_MODE_SECRET) {
        notify_proxy(proxy, P2P_PROXY_EVENT_DIAGNOSTIC, P2P_OK, "私密访问已启用：需要可信设备包授权");
    }
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_proxy_stop(P2PProxyHandle proxy_handle) {
    std::shared_ptr<ProxyInstance> proxy;
    {
        std::lock_guard<std::mutex> lock(g_proxy_mutex);
        auto it = g_proxies.find(proxy_handle);
        if (it == g_proxies.end()) {
            return static_cast<int>(P2P_ERROR_NOT_FOUND);
        }
        proxy = it->second;
        g_proxies.erase(it);
    }

    proxy->running.store(false);
    if (proxy->tunnel != 0) {
        p2p_tunnel_stop(proxy->tunnel);
    }
    notify_proxy(proxy, P2P_PROXY_EVENT_STOPPED, P2P_OK, "代理规则已停止");
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_proxy_get_metrics(P2PProxyHandle proxy_handle, P2PProxyMetrics* out_metrics) {
    if (!out_metrics) {
        return static_cast<int>(P2P_ERROR_INVALID_ARGUMENT);
    }
    auto proxy = get_proxy(proxy_handle);
    if (!proxy) {
        return static_cast<int>(P2P_ERROR_NOT_FOUND);
    }
    *out_metrics = metrics_for(proxy);
    return static_cast<int>(P2P_OK);
}

} // extern "C"
