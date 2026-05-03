#include "gateway_manager.h"

#if defined(_WIN32)
#  define NOMINMAX
#  include <winsock2.h>
#  include <ws2tcpip.h>
#else
#  include <arpa/inet.h>
#  include <errno.h>
#  include <fcntl.h>
#  include <netdb.h>
#  include <netinet/in.h>
#  include <sys/select.h>
#  include <sys/socket.h>
#  include <sys/time.h>
#  include <unistd.h>
#endif

#include <atomic>
#include <chrono>
#include <cstdint>
#include <ctime>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

namespace {

#if defined(_WIN32)
using socket_t = SOCKET;
constexpr socket_t kInvalidSocket = INVALID_SOCKET;
#else
using socket_t = int;
constexpr socket_t kInvalidSocket = -1;
#endif

std::atomic_uint64_t g_next_gateway{1};
std::atomic_uint64_t g_next_gateway_tunnel{1};

std::mutex g_callback_mutex;
P2PGatewayCallback g_callback = nullptr;
void* g_callback_user = nullptr;

struct GatewayTunnel;

struct GatewaySession {
    P2PGatewayHandle handle = 0;
    socket_t control_socket = kInvalidSocket;
    std::string gateway_host;
    uint16_t control_port = 0;
    std::string token;
    std::string room_code;
    std::string public_code;
    bool auto_reconnect = true;
    std::atomic_bool running{false};
    std::atomic_uint64_t bytes_up{0};
    std::atomic_uint64_t bytes_down{0};
    std::atomic_uint32_t active_connections{0};
    std::atomic_uint32_t reconnect_count{0};
    std::atomic_uint32_t error_count{0};
    std::mutex write_mutex;
    std::mutex tunnel_mutex;
    std::map<P2PGatewayTunnelHandle, std::shared_ptr<GatewayTunnel>> tunnels;
    std::thread control_thread;

    ~GatewaySession();
};

struct GatewayTunnel {
    P2PGatewayTunnelHandle handle = 0;
    std::weak_ptr<GatewaySession> session;
    std::string name;
    std::string local_host;
    uint16_t local_port = 0;
    uint16_t public_port = 0;
    P2PTunnelProtocol protocol = P2P_TUNNEL_PROTOCOL_TCP;
    std::atomic_bool running{false};
    std::atomic_uint64_t bytes_up{0};
    std::atomic_uint64_t bytes_down{0};
    std::atomic_uint32_t active_connections{0};
    std::atomic_uint32_t error_count{0};
};

std::mutex g_sessions_mutex;
std::map<P2PGatewayHandle, std::shared_ptr<GatewaySession>> g_sessions;

std::mutex g_tunnels_mutex;
std::map<P2PGatewayTunnelHandle, std::shared_ptr<GatewayTunnel>> g_tunnels;

void close_socket(socket_t s) {
    if (s == kInvalidSocket) {
        return;
    }
#if defined(_WIN32)
    closesocket(s);
#else
    close(s);
#endif
}

void shutdown_socket(socket_t s) {
    if (s == kInvalidSocket) {
        return;
    }
#if defined(_WIN32)
    shutdown(s, SD_BOTH);
#else
    shutdown(s, SHUT_RDWR);
#endif
}

void set_receive_timeout(socket_t s, uint32_t timeout_ms) {
#if defined(_WIN32)
    DWORD timeout = timeout_ms;
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, reinterpret_cast<const char*>(&timeout), sizeof(timeout));
#else
    timeval tv{};
    tv.tv_sec = static_cast<long>(timeout_ms / 1000);
    tv.tv_usec = static_cast<long>((timeout_ms % 1000) * 1000);
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
#endif
}

void set_nonblocking(socket_t s, bool nonblocking) {
#if defined(_WIN32)
    u_long mode = nonblocking ? 1UL : 0UL;
    ioctlsocket(s, FIONBIO, &mode);
#else
    int flags = fcntl(s, F_GETFL, 0);
    if (flags >= 0) {
        fcntl(s, F_SETFL, nonblocking ? (flags | O_NONBLOCK) : (flags & ~O_NONBLOCK));
    }
#endif
}

bool resolve_ipv4(const std::string& host, uint16_t port, sockaddr_in* out_address) {
    if (!out_address || host.empty() || port == 0) {
        return false;
    }

    addrinfo hints{};
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_STREAM;
    hints.ai_protocol = IPPROTO_TCP;

    addrinfo* result = nullptr;
    const std::string port_text = std::to_string(port);
    if (getaddrinfo(host.c_str(), port_text.c_str(), &hints, &result) != 0 || !result) {
        return false;
    }

    std::memcpy(out_address, result->ai_addr, sizeof(sockaddr_in));
    freeaddrinfo(result);
    return true;
}

bool poll_connect(socket_t s, const sockaddr_in& address, uint32_t timeout_ms) {
    set_nonblocking(s, true);
    int result = connect(s, reinterpret_cast<const sockaddr*>(&address), sizeof(address));
    if (result == 0) {
        set_nonblocking(s, false);
        return true;
    }

#if defined(_WIN32)
    const int last_error = WSAGetLastError();
    if (last_error != WSAEWOULDBLOCK && last_error != WSAEINPROGRESS) {
        set_nonblocking(s, false);
        return false;
    }
#else
    if (errno != EINPROGRESS && errno != EWOULDBLOCK) {
        set_nonblocking(s, false);
        return false;
    }
#endif

    fd_set write_set;
    FD_ZERO(&write_set);
    FD_SET(s, &write_set);

    timeval tv{};
    tv.tv_sec = static_cast<long>(timeout_ms / 1000);
    tv.tv_usec = static_cast<long>((timeout_ms % 1000) * 1000);

    result = select(static_cast<int>(s + 1), nullptr, &write_set, nullptr, &tv);
    if (result <= 0) {
        set_nonblocking(s, false);
        return false;
    }

    int socket_error = 0;
#if defined(_WIN32)
    int len = sizeof(socket_error);
#else
    socklen_t len = sizeof(socket_error);
#endif
    getsockopt(s, SOL_SOCKET, SO_ERROR, reinterpret_cast<char*>(&socket_error), &len);
    set_nonblocking(s, false);
    return socket_error == 0;
}

socket_t connect_tcp(const std::string& host, uint16_t port, uint32_t timeout_ms = 5000) {
    sockaddr_in address{};
    if (!resolve_ipv4(host, port, &address)) {
        return kInvalidSocket;
    }

    socket_t s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (s == kInvalidSocket) {
        return kInvalidSocket;
    }

    if (!poll_connect(s, address, timeout_ms)) {
        close_socket(s);
        return kInvalidSocket;
    }

    return s;
}

bool send_all(socket_t s, const char* data, size_t length) {
    size_t sent = 0;
    while (sent < length) {
        const int chunk = send(s, data + sent, static_cast<int>(length - sent), 0);
        if (chunk <= 0) {
            return false;
        }
        sent += static_cast<size_t>(chunk);
    }
    return true;
}

bool send_text(socket_t s, const std::string& text) {
    return send_all(s, text.data(), text.size());
}

bool read_line(socket_t s, std::string* line) {
    if (!line) {
        return false;
    }
    line->clear();
    char ch = 0;
    while (true) {
        const int received = recv(s, &ch, 1, 0);
        if (received <= 0) {
            return false;
        }
        if (ch == '\n') {
            return true;
        }
        if (ch != '\r') {
            line->push_back(ch);
        }
        if (line->size() > 4096) {
            return false;
        }
    }
}

bool read_exact(socket_t s, char* data, size_t length) {
    size_t received_total = 0;
    while (received_total < length) {
        const int received = recv(s, data + received_total, static_cast<int>(length - received_total), 0);
        if (received <= 0) {
            return false;
        }
        received_total += static_cast<size_t>(received);
    }
    return true;
}

bool send_frame(socket_t s, const char* data, uint32_t length) {
    const uint32_t network_length = htonl(length);
    if (!send_all(s, reinterpret_cast<const char*>(&network_length), sizeof(network_length))) {
        return false;
    }
    return length == 0 || send_all(s, data, length);
}

bool read_frame(socket_t s, std::vector<char>* frame) {
    if (!frame) {
        return false;
    }

    uint32_t network_length = 0;
    if (!read_exact(s, reinterpret_cast<char*>(&network_length), sizeof(network_length))) {
        return false;
    }

    const uint32_t length = ntohl(network_length);
    if (length > 1024 * 1024) {
        return false;
    }

    frame->assign(length, 0);
    return length == 0 || read_exact(s, frame->data(), length);
}

std::vector<std::string> split_words(const std::string& line) {
    std::istringstream iss(line);
    std::vector<std::string> words;
    std::string word;
    while (iss >> word) {
        words.push_back(word);
    }
    return words;
}

std::string safe_word(std::string value) {
    for (char& ch : value) {
        if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n') {
            ch = '_';
        }
    }
    return value.empty() ? "-" : value;
}

P2PGatewayMetrics metrics_for(const std::shared_ptr<GatewaySession>& session, const std::shared_ptr<GatewayTunnel>& tunnel) {
    P2PGatewayMetrics metrics{};
    if (session) {
        metrics.bytes_up = session->bytes_up.load();
        metrics.bytes_down = session->bytes_down.load();
        metrics.active_connections = session->active_connections.load();
        metrics.reconnect_count = session->reconnect_count.load();
        metrics.error_count = session->error_count.load();
        metrics.running = session->running.load() ? 1 : 0;
    }
    if (tunnel) {
        metrics.bytes_up = tunnel->bytes_up.load();
        metrics.bytes_down = tunnel->bytes_down.load();
        metrics.active_connections = tunnel->active_connections.load();
        metrics.error_count += tunnel->error_count.load();
        metrics.running = tunnel->running.load() ? 1 : metrics.running;
    }
    return metrics;
}

void notify_gateway(
    const std::shared_ptr<GatewaySession>& session,
    const std::shared_ptr<GatewayTunnel>& tunnel,
    P2PGatewayEvent event_type,
    int error_code,
    const std::string& message) {
    P2PGatewayCallback callback = nullptr;
    void* user = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_callback_mutex);
        callback = g_callback;
        user = g_callback_user;
    }
    if (!callback) {
        return;
    }

    const P2PGatewayMetrics metrics = metrics_for(session, tunnel);
    callback(
        session ? session->handle : 0,
        tunnel ? tunnel->handle : 0,
        event_type,
        error_code,
        message.c_str(),
        &metrics,
        user);
}

std::shared_ptr<GatewaySession> get_session(P2PGatewayHandle handle) {
    std::lock_guard<std::mutex> lock(g_sessions_mutex);
    auto it = g_sessions.find(handle);
    return it == g_sessions.end() ? nullptr : it->second;
}

std::shared_ptr<GatewayTunnel> get_tunnel(P2PGatewayTunnelHandle handle) {
    std::lock_guard<std::mutex> lock(g_tunnels_mutex);
    auto it = g_tunnels.find(handle);
    return it == g_tunnels.end() ? nullptr : it->second;
}

bool write_control(const std::shared_ptr<GatewaySession>& session, const std::string& line) {
    if (!session || !session->running.load()) {
        return false;
    }
    std::lock_guard<std::mutex> lock(session->write_mutex);
    return send_text(session->control_socket, line);
}

void pipe_socket(
    socket_t source,
    socket_t target,
    std::atomic_bool& running,
    std::atomic_uint64_t& counter) {
    std::vector<char> buffer(64 * 1024);
    while (running.load()) {
        const int received = recv(source, buffer.data(), static_cast<int>(buffer.size()), 0);
        if (received <= 0) {
            break;
        }
        if (!send_all(target, buffer.data(), static_cast<size_t>(received))) {
            break;
        }
        counter.fetch_add(static_cast<uint64_t>(received));
    }
    running.store(false);
    shutdown_socket(source);
    shutdown_socket(target);
}

void bridge_tcp(
    const std::shared_ptr<GatewaySession>& session,
    const std::shared_ptr<GatewayTunnel>& tunnel,
    socket_t local_socket,
    socket_t gateway_socket) {
    std::atomic_bool running{true};
    session->active_connections.fetch_add(1);
    tunnel->active_connections.fetch_add(1);
    notify_gateway(session, tunnel, P2P_GATEWAY_EVENT_CONNECTION_OPENED, P2P_OK, "网关 TCP 数据通道已建立");

    std::thread up([&]() {
        pipe_socket(local_socket, gateway_socket, running, tunnel->bytes_up);
    });
    std::thread down([&]() {
        pipe_socket(gateway_socket, local_socket, running, tunnel->bytes_down);
    });
    if (up.joinable()) {
        up.join();
    }
    if (down.joinable()) {
        down.join();
    }

    close_socket(local_socket);
    close_socket(gateway_socket);
    session->bytes_up.fetch_add(tunnel->bytes_up.load());
    session->bytes_down.fetch_add(tunnel->bytes_down.load());
    session->active_connections.fetch_sub(1);
    tunnel->active_connections.fetch_sub(1);
    notify_gateway(session, tunnel, P2P_GATEWAY_EVENT_CONNECTION_CLOSED, P2P_OK, "网关 TCP 数据通道已关闭");
}

void bridge_udp_packet(
    const std::shared_ptr<GatewaySession>& session,
    const std::shared_ptr<GatewayTunnel>& tunnel,
    socket_t data_socket) {
    std::vector<char> packet;
    if (!read_frame(data_socket, &packet) || packet.empty()) {
        close_socket(data_socket);
        tunnel->error_count.fetch_add(1);
        notify_gateway(session, tunnel, P2P_GATEWAY_EVENT_ERROR, P2P_ERROR_PROTOCOL, "网关 UDP 数据帧无效");
        return;
    }

    sockaddr_in local{};
    if (!resolve_ipv4(tunnel->local_host, tunnel->local_port, &local)) {
        close_socket(data_socket);
        tunnel->error_count.fetch_add(1);
        notify_gateway(session, tunnel, P2P_GATEWAY_EVENT_ERROR, P2P_ERROR_INVALID_ARGUMENT, "无法解析本地 UDP 服务地址");
        return;
    }

    socket_t udp = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (udp == kInvalidSocket) {
        close_socket(data_socket);
        tunnel->error_count.fetch_add(1);
        notify_gateway(session, tunnel, P2P_GATEWAY_EVENT_ERROR, P2P_ERROR_SOCKET, "无法创建本地 UDP 套接字");
        return;
    }

    set_receive_timeout(udp, 1500);
    const int sent = sendto(udp, packet.data(), static_cast<int>(packet.size()), 0, reinterpret_cast<sockaddr*>(&local), sizeof(local));
    if (sent <= 0) {
        close_socket(udp);
        close_socket(data_socket);
        tunnel->error_count.fetch_add(1);
        notify_gateway(session, tunnel, P2P_GATEWAY_EVENT_ERROR, P2P_ERROR_SOCKET, "发送到本地 UDP 服务失败");
        return;
    }

    tunnel->bytes_down.fetch_add(static_cast<uint64_t>(sent));
    std::vector<char> response(64 * 1024);
    sockaddr_in from{};
#if defined(_WIN32)
    int from_len = sizeof(from);
#else
    socklen_t from_len = sizeof(from);
#endif
    const int received = recvfrom(udp, response.data(), static_cast<int>(response.size()), 0, reinterpret_cast<sockaddr*>(&from), &from_len);
    if (received > 0) {
        send_frame(data_socket, response.data(), static_cast<uint32_t>(received));
        tunnel->bytes_up.fetch_add(static_cast<uint64_t>(received));
        notify_gateway(session, tunnel, P2P_GATEWAY_EVENT_TRAFFIC, P2P_OK, "网关 UDP 包已转发");
    }

    close_socket(udp);
    close_socket(data_socket);
}

void open_gateway_data_channel(
    const std::shared_ptr<GatewaySession>& session,
    const std::shared_ptr<GatewayTunnel>& tunnel,
    const std::string& connection_id) {
    if (!session || !tunnel) {
        return;
    }

    socket_t data_socket = connect_tcp(session->gateway_host, session->control_port, 5000);
    if (data_socket == kInvalidSocket) {
        tunnel->error_count.fetch_add(1);
        notify_gateway(session, tunnel, P2P_GATEWAY_EVENT_ERROR, P2P_ERROR_CONNECT_FAILED, "无法创建网关数据通道");
        return;
    }

    const std::string line =
        "DATA " + safe_word(session->token) + " " +
        std::to_string(tunnel->handle) + " " + safe_word(connection_id) + "\n";
    if (!send_text(data_socket, line)) {
        close_socket(data_socket);
        tunnel->error_count.fetch_add(1);
        notify_gateway(session, tunnel, P2P_GATEWAY_EVENT_ERROR, P2P_ERROR_SOCKET, "网关数据通道握手失败");
        return;
    }

    if (tunnel->protocol == P2P_TUNNEL_PROTOCOL_UDP) {
        bridge_udp_packet(session, tunnel, data_socket);
        return;
    }

    socket_t local_socket = connect_tcp(tunnel->local_host, tunnel->local_port, 5000);
    if (local_socket == kInvalidSocket) {
        close_socket(data_socket);
        tunnel->error_count.fetch_add(1);
        notify_gateway(session, tunnel, P2P_GATEWAY_EVENT_ERROR, P2P_ERROR_CONNECT_FAILED, "无法连接本地服务，请检查本地端口是否已启动");
        return;
    }

    bridge_tcp(session, tunnel, local_socket, data_socket);
}

void control_loop(std::shared_ptr<GatewaySession> session) {
    std::string line;
    while (session->running.load() && read_line(session->control_socket, &line)) {
        auto words = split_words(line);
        if (words.empty()) {
            continue;
        }

        if (words[0] == "OPEN" && words.size() >= 3) {
            const auto tunnel_handle = static_cast<P2PGatewayTunnelHandle>(std::strtoull(words[1].c_str(), nullptr, 10));
            std::shared_ptr<GatewayTunnel> tunnel;
            {
                std::lock_guard<std::mutex> lock(session->tunnel_mutex);
                auto it = session->tunnels.find(tunnel_handle);
                if (it != session->tunnels.end()) {
                    tunnel = it->second;
                }
            }
            if (tunnel) {
                std::thread(open_gateway_data_channel, session, tunnel, words[2]).detach();
            }
        } else if (words[0] == "OK") {
            notify_gateway(session, nullptr, P2P_GATEWAY_EVENT_DIAGNOSTIC, P2P_OK, line);
        } else if (words[0] == "ERR") {
            session->error_count.fetch_add(1);
            notify_gateway(session, nullptr, P2P_GATEWAY_EVENT_ERROR, P2P_ERROR_PROTOCOL, line);
        } else if (words[0] == "PING") {
            write_control(session, "PONG\n");
        }
    }

    session->running.store(false);
    notify_gateway(session, nullptr, P2P_GATEWAY_EVENT_DISCONNECTED, P2P_ERROR_CONNECT_FAILED, "网关控制连接已断开");
}

GatewaySession::~GatewaySession() {
    running.store(false);
    shutdown_socket(control_socket);
    close_socket(control_socket);
    control_socket = kInvalidSocket;
    if (control_thread.joinable()) {
        control_thread.join();
    }
}

} // namespace

extern "C" {

P2P_EXPORT int P2P_CALL p2p_gateway_set_callback(P2PGatewayCallback callback, void* user_data) {
    std::lock_guard<std::mutex> lock(g_callback_mutex);
    g_callback = callback;
    g_callback_user = user_data;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_gateway_connect(const P2PGatewayConfig* config, P2PGatewayHandle* out_gateway) {
    if (!config || !out_gateway || !config->gateway_host || config->control_port == 0 || !config->token) {
        return static_cast<int>(P2P_ERROR_INVALID_ARGUMENT);
    }

    socket_t control = connect_tcp(config->gateway_host, config->control_port, 5000);
    if (control == kInvalidSocket) {
        return static_cast<int>(P2P_ERROR_CONNECT_FAILED);
    }

    set_receive_timeout(control, 5000);
    const std::string hello =
        "HELLO2 " + safe_word(config->token) + " " +
        std::to_string(static_cast<int64_t>(std::time(nullptr))) + " " +
        std::to_string(g_next_gateway.load()) + " " +
        safe_word(config->room_code ? config->room_code : "-") + " " +
        safe_word(config->public_code ? config->public_code : "-") + "\n";
    if (!send_text(control, hello)) {
        close_socket(control);
        return static_cast<int>(P2P_ERROR_SOCKET);
    }

    std::string response;
    if (!read_line(control, &response) || response.rfind("OK", 0) != 0) {
        close_socket(control);
        return static_cast<int>(P2P_ERROR_SECURITY);
    }

    set_receive_timeout(control, 0);

    auto session = std::make_shared<GatewaySession>();
    session->handle = g_next_gateway.fetch_add(1);
    session->control_socket = control;
    session->gateway_host = config->gateway_host;
    session->control_port = config->control_port;
    session->token = config->token;
    session->room_code = config->room_code ? config->room_code : "";
    session->public_code = config->public_code ? config->public_code : "";
    session->auto_reconnect = config->auto_reconnect != 0;
    session->running.store(true);
    session->control_thread = std::thread(control_loop, session);

    {
        std::lock_guard<std::mutex> lock(g_sessions_mutex);
        g_sessions[session->handle] = session;
    }

    *out_gateway = session->handle;
    notify_gateway(session, nullptr, P2P_GATEWAY_EVENT_CONNECTED, P2P_OK, "已连接自建公网网关");
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_gateway_disconnect(P2PGatewayHandle gateway) {
    std::shared_ptr<GatewaySession> session;
    {
        std::lock_guard<std::mutex> lock(g_sessions_mutex);
        auto it = g_sessions.find(gateway);
        if (it == g_sessions.end()) {
            return static_cast<int>(P2P_ERROR_NOT_FOUND);
        }
        session = it->second;
        g_sessions.erase(it);
    }

    session->running.store(false);
    shutdown_socket(session->control_socket);
    close_socket(session->control_socket);
    notify_gateway(session, nullptr, P2P_GATEWAY_EVENT_DISCONNECTED, P2P_OK, "已断开公网网关");
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_gateway_start_tunnel(P2PGatewayHandle gateway, const P2PGatewayTunnelOptions* options, P2PGatewayTunnelHandle* out_tunnel) {
    if (!options || !out_tunnel || !options->local_host || options->local_port == 0 || options->public_port == 0) {
        return static_cast<int>(P2P_ERROR_INVALID_ARGUMENT);
    }

    auto session = get_session(gateway);
    if (!session || !session->running.load()) {
        return static_cast<int>(P2P_ERROR_NOT_FOUND);
    }

    auto tunnel = std::make_shared<GatewayTunnel>();
    tunnel->handle = g_next_gateway_tunnel.fetch_add(1);
    tunnel->session = session;
    tunnel->name = options->name ? options->name : "tunnel";
    tunnel->local_host = options->local_host;
    tunnel->local_port = options->local_port;
    tunnel->public_port = options->public_port;
    tunnel->protocol = options->protocol;
    tunnel->running.store(true);

    {
        std::lock_guard<std::mutex> lock(session->tunnel_mutex);
        session->tunnels[tunnel->handle] = tunnel;
    }
    {
        std::lock_guard<std::mutex> lock(g_tunnels_mutex);
        g_tunnels[tunnel->handle] = tunnel;
    }

    const std::string protocol = tunnel->protocol == P2P_TUNNEL_PROTOCOL_UDP ? "UDP" : "TCP";
    const std::string line =
        "TUNNEL " + safe_word(session->token) + " " +
        std::to_string(tunnel->handle) + " " + protocol + " " +
        std::to_string(tunnel->public_port) + " " +
        safe_word(tunnel->local_host) + " " +
        std::to_string(tunnel->local_port) + " " +
        safe_word(tunnel->name) + "\n";

    if (!write_control(session, line)) {
        tunnel->running.store(false);
        return static_cast<int>(P2P_ERROR_SOCKET);
    }

    *out_tunnel = tunnel->handle;
    notify_gateway(session, tunnel, P2P_GATEWAY_EVENT_TUNNEL_REGISTERED, P2P_OK, "网关隧道已注册");
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_gateway_stop_tunnel(P2PGatewayTunnelHandle tunnel_handle) {
    auto tunnel = get_tunnel(tunnel_handle);
    if (!tunnel) {
        return static_cast<int>(P2P_ERROR_NOT_FOUND);
    }

    auto session = tunnel->session.lock();
    tunnel->running.store(false);
    if (session) {
        write_control(session, "STOP " + safe_word(session->token) + " " + std::to_string(tunnel_handle) + "\n");
        std::lock_guard<std::mutex> lock(session->tunnel_mutex);
        session->tunnels.erase(tunnel_handle);
    }
    {
        std::lock_guard<std::mutex> lock(g_tunnels_mutex);
        g_tunnels.erase(tunnel_handle);
    }

    notify_gateway(session, tunnel, P2P_GATEWAY_EVENT_TUNNEL_STOPPED, P2P_OK, "网关隧道已停止");
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_gateway_get_metrics(P2PGatewayHandle gateway, P2PGatewayTunnelHandle tunnel_handle, P2PGatewayMetrics* out_metrics) {
    if (!out_metrics) {
        return static_cast<int>(P2P_ERROR_INVALID_ARGUMENT);
    }

    auto session = gateway == 0 ? nullptr : get_session(gateway);
    auto tunnel = tunnel_handle == 0 ? nullptr : get_tunnel(tunnel_handle);
    if (!session && !tunnel) {
        return static_cast<int>(P2P_ERROR_NOT_FOUND);
    }

    *out_metrics = metrics_for(session, tunnel);
    return static_cast<int>(P2P_OK);
}

} // extern "C"
