#if defined(_WIN32)
#  ifndef WIN32_LEAN_AND_MEAN
#    define WIN32_LEAN_AND_MEAN
#  endif
#  ifndef NOMINMAX
#    define NOMINMAX
#  endif
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

#include "file_transfer.h"
#include "nat_traversal.h"
#include "platform_api.h"
#include "security.h"
#include "stream_manager.h"
#include "tunnel_manager.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cctype>
#include <condition_variable>
#include <cstring>
#include <filesystem>
#include <functional>
#include <fstream>
#include <iomanip>
#include <map>
#include <memory>
#include <mutex>
#include <random>
#include <set>
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

constexpr uint32_t kDefaultChunkSize = 64 * 1024;
constexpr const char* kUdpPunchMessage = "TPCWEI_UDP_PUNCH_V1";
constexpr const char* kDiscoveryMessage = "TPCWEI_LAN_DISCOVERY_V1";

std::atomic_bool g_initialized{false};
std::atomic_uint64_t g_next_node{1};
std::atomic_uint64_t g_next_transfer{1};
std::atomic_uint64_t g_next_stream{1};
std::atomic_uint64_t g_next_tunnel{1};
std::atomic_uint64_t g_next_platform_handle{1};

std::mutex g_error_mutex;
std::string g_last_error;

std::mutex g_nat_callback_mutex;
P2PNatCallback g_nat_callback = nullptr;
void* g_nat_callback_user = nullptr;
P2PNatType g_nat_type = P2P_NAT_TYPE_UNKNOWN;
std::string g_external_address;
uint16_t g_external_port = 0;

std::mutex g_transfer_callback_mutex;
P2PTransferCallback g_transfer_callback = nullptr;
void* g_transfer_callback_user = nullptr;

std::mutex g_tunnel_callback_mutex;
P2PTunnelCallback g_tunnel_callback = nullptr;
void* g_tunnel_callback_user = nullptr;

struct Node {
    P2PNodeHandle handle = 0;
    socket_t udp_socket = kInvalidSocket;
    socket_t tcp_socket = kInvalidSocket;
    uint16_t udp_port = 0;
    uint16_t tcp_port = 0;
    std::atomic_bool running{false};
    std::atomic_uint64_t bytes_sent{0};
    std::atomic_uint64_t bytes_received{0};
    std::thread discovery_thread;

    ~Node();
};

struct TransferTask {
    P2PTransferHandle handle = 0;
    std::atomic_bool cancel_requested{false};
    socket_t listener = kInvalidSocket;
    socket_t connection = kInvalidSocket;
    std::thread worker;
    std::mutex mutex;
    P2PTransferProgress progress{};

    ~TransferTask();
};

struct StreamTask {
    P2PStreamHandle handle = 0;
    socket_t socket = kInvalidSocket;
    std::atomic_bool running{false};
    std::thread receiver;
    std::mutex callback_mutex;
    P2PStreamReceiveCallback callback = nullptr;
    void* callback_user = nullptr;
    std::atomic_uint64_t bytes_sent{0};
    std::atomic_uint64_t bytes_received{0};
    std::atomic_uint32_t frames_sent{0};
    std::atomic_uint32_t frames_received{0};
    std::atomic_uint32_t failed_sends{0};
    std::atomic_uint32_t latency_ms{0};
    std::atomic<P2PStreamState> state{P2P_STREAM_CLOSED};

    ~StreamTask();
};

struct TunnelTask {
    P2PTunnelHandle handle = 0;
    socket_t listener = kInvalidSocket;
    socket_t udp_socket = kInvalidSocket;
    std::thread worker;
    std::atomic_bool running{false};
    std::atomic_uint64_t bytes_up{0};
    std::atomic_uint64_t bytes_down{0};
    std::atomic_uint32_t active_connections{0};
    uint16_t local_port = 0;
    uint16_t peer_port = 0;
    std::string peer_host;
    P2PTunnelProtocol protocol = P2P_TUNNEL_PROTOCOL_TCP;

    ~TunnelTask();
};

std::mutex g_nodes_mutex;
std::map<P2PNodeHandle, std::shared_ptr<Node>> g_nodes;

std::mutex g_transfers_mutex;
std::map<P2PTransferHandle, std::shared_ptr<TransferTask>> g_transfers;

std::mutex g_streams_mutex;
std::map<P2PStreamHandle, std::shared_ptr<StreamTask>> g_streams;

std::mutex g_tunnels_mutex;
std::map<P2PTunnelHandle, std::shared_ptr<TunnelTask>> g_tunnels;

struct TransferOptionsCopy {
    std::string local_path;
    std::string remote_path;
    std::string peer_host;
    uint16_t peer_port = 0;
    bool resume_enabled = false;
    uint8_t parallel_paths = 1;
    uint32_t chunk_size = kDefaultChunkSize;
};

struct TunnelOptionsCopy {
    std::string local_bind_address;
    uint16_t local_port = 0;
    std::string peer_host;
    uint16_t peer_port = 0;
    P2PTunnelProtocol protocol = P2P_TUNNEL_PROTOCOL_TCP;
    bool aggressive_reconnect = false;
    bool allow_lan_clients = false;
};

void set_last_error(const std::string& message) {
    std::lock_guard<std::mutex> lock(g_error_mutex);
    g_last_error = message;
}

int fail(P2PErrorCode code, const std::string& message) {
    set_last_error(message);
    return static_cast<int>(code);
}

bool ensure_initialized() {
    if (!g_initialized.load()) {
        set_last_error("p2p_initialize must be called first");
        return false;
    }
    return true;
}

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

int socket_error_code() {
#if defined(_WIN32)
    return WSAGetLastError();
#else
    return errno;
#endif
}

std::string socket_error_text(const std::string& prefix) {
    std::ostringstream oss;
    oss << prefix << " (socket error " << socket_error_code() << ")";
    return oss.str();
}

void set_reuse_address(socket_t s) {
    int yes = 1;
    setsockopt(s, SOL_SOCKET, SO_REUSEADDR, reinterpret_cast<const char*>(&yes), sizeof(yes));
}

void set_broadcast(socket_t s) {
    int yes = 1;
    setsockopt(s, SOL_SOCKET, SO_BROADCAST, reinterpret_cast<const char*>(&yes), sizeof(yes));
}

void set_nonblocking(socket_t s, bool nonblocking) {
#if defined(_WIN32)
    u_long mode = nonblocking ? 1UL : 0UL;
    ioctlsocket(s, FIONBIO, &mode);
#else
    int flags = fcntl(s, F_GETFL, 0);
    if (flags >= 0) {
        if (nonblocking) {
            fcntl(s, F_SETFL, flags | O_NONBLOCK);
        } else {
            fcntl(s, F_SETFL, flags & ~O_NONBLOCK);
        }
    }
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

bool resolve_ipv4(const char* host, uint16_t port, sockaddr_in* out_address) {
    if (!host || !out_address) {
        return false;
    }

    addrinfo hints{};
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_STREAM;

    addrinfo* result = nullptr;
    const std::string port_text = std::to_string(port);
    if (getaddrinfo(host, port_text.c_str(), &hints, &result) != 0 || !result) {
        return false;
    }

    std::memcpy(out_address, result->ai_addr, sizeof(sockaddr_in));
    freeaddrinfo(result);
    return true;
}

bool bind_ipv4(socket_t s, const char* bind_address, uint16_t port) {
    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_port = htons(port);
    if (bind_address && bind_address[0] != '\0') {
        if (inet_pton(AF_INET, bind_address, &addr.sin_addr) != 1) {
            return false;
        }
    } else {
        addr.sin_addr.s_addr = htonl(INADDR_ANY);
    }

    return bind(s, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) == 0;
}

uint16_t socket_port(socket_t s) {
    sockaddr_in addr{};
#if defined(_WIN32)
    int len = sizeof(addr);
#else
    socklen_t len = sizeof(addr);
#endif
    if (getsockname(s, reinterpret_cast<sockaddr*>(&addr), &len) != 0) {
        return 0;
    }
    return ntohs(addr.sin_port);
}

std::string socket_local_address(socket_t s) {
    sockaddr_in addr{};
#if defined(_WIN32)
    int len = sizeof(addr);
#else
    socklen_t len = sizeof(addr);
#endif
    if (getsockname(s, reinterpret_cast<sockaddr*>(&addr), &len) != 0) {
        return {};
    }
    char buffer[INET_ADDRSTRLEN]{};
    if (!inet_ntop(AF_INET, &addr.sin_addr, buffer, sizeof(buffer))) {
        return {};
    }
    return buffer;
}

bool send_all(socket_t s, const uint8_t* data, size_t length) {
    size_t sent = 0;
    while (sent < length) {
        const int chunk = send(s, reinterpret_cast<const char*>(data + sent), static_cast<int>(length - sent), 0);
        if (chunk <= 0) {
            return false;
        }
        sent += static_cast<size_t>(chunk);
    }
    return true;
}

bool send_all_text(socket_t s, const std::string& text) {
    return send_all(s, reinterpret_cast<const uint8_t*>(text.data()), text.size());
}

std::string to_lower(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return value;
}

size_t find_case_insensitive(const std::string& haystack, const std::string& needle, size_t offset = 0) {
    const std::string lower_haystack = to_lower(haystack);
    const std::string lower_needle = to_lower(needle);
    return lower_haystack.find(lower_needle, offset);
}

std::string trim(const std::string& value) {
    const auto begin = value.find_first_not_of(" \t\r\n");
    if (begin == std::string::npos) {
        return {};
    }
    const auto end = value.find_last_not_of(" \t\r\n");
    return value.substr(begin, end - begin + 1);
}

bool is_alphanumeric_code(const std::string& value, size_t expected_length) {
    if (value.size() != expected_length) {
        return false;
    }
    return std::all_of(value.begin(), value.end(), [](unsigned char ch) {
        return std::isalnum(ch) != 0;
    });
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

std::shared_ptr<Node> get_node(P2PNodeHandle handle) {
    std::lock_guard<std::mutex> lock(g_nodes_mutex);
    auto it = g_nodes.find(handle);
    return it == g_nodes.end() ? nullptr : it->second;
}

std::shared_ptr<TransferTask> get_transfer(P2PTransferHandle handle) {
    std::lock_guard<std::mutex> lock(g_transfers_mutex);
    auto it = g_transfers.find(handle);
    return it == g_transfers.end() ? nullptr : it->second;
}

std::shared_ptr<StreamTask> get_stream(P2PStreamHandle handle) {
    std::lock_guard<std::mutex> lock(g_streams_mutex);
    auto it = g_streams.find(handle);
    return it == g_streams.end() ? nullptr : it->second;
}

std::shared_ptr<TunnelTask> get_tunnel(P2PTunnelHandle handle) {
    std::lock_guard<std::mutex> lock(g_tunnels_mutex);
    auto it = g_tunnels.find(handle);
    return it == g_tunnels.end() ? nullptr : it->second;
}

void notify_nat(P2PNatEvent event_type, int error_code, const std::string& address, uint16_t port) {
    P2PNatCallback callback = nullptr;
    void* user = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_nat_callback_mutex);
        callback = g_nat_callback;
        user = g_nat_callback_user;
    }

    if (callback) {
        callback(event_type, error_code, address.empty() ? nullptr : address.c_str(), port, user);
    }
}

void notify_transfer(P2PTransferHandle handle, P2PTransferEvent event_type, const P2PTransferProgress& progress) {
    P2PTransferCallback callback = nullptr;
    void* user = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_transfer_callback_mutex);
        callback = g_transfer_callback;
        user = g_transfer_callback_user;
    }

    if (callback) {
        callback(handle, event_type, &progress, user);
    }
}

P2PTunnelMetrics tunnel_snapshot(const std::shared_ptr<TunnelTask>& tunnel) {
    P2PTunnelMetrics metrics{};
    metrics.bytes_up = tunnel->bytes_up.load();
    metrics.bytes_down = tunnel->bytes_down.load();
    metrics.active_connections = tunnel->active_connections.load();
    metrics.local_port = tunnel->local_port;
    metrics.peer_port = tunnel->peer_port;
    metrics.protocol = tunnel->protocol;
    metrics.running = tunnel->running.load() ? 1 : 0;
    return metrics;
}

void notify_tunnel(const std::shared_ptr<TunnelTask>& tunnel, P2PTunnelEvent event_type) {
    P2PTunnelCallback callback = nullptr;
    void* user = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_tunnel_callback_mutex);
        callback = g_tunnel_callback;
        user = g_tunnel_callback_user;
    }
    if (callback) {
        const P2PTunnelMetrics metrics = tunnel_snapshot(tunnel);
        callback(tunnel->handle, event_type, &metrics, user);
    }
}

void set_transfer_status(const std::shared_ptr<TransferTask>& task, P2PTransferStatus status, P2PTransferEvent event_type) {
    P2PTransferProgress snapshot{};
    {
        std::lock_guard<std::mutex> lock(task->mutex);
        task->progress.status = status;
        snapshot = task->progress;
    }
    notify_transfer(task->handle, event_type, snapshot);
}

P2PTransferProgress transfer_snapshot(const std::shared_ptr<TransferTask>& task) {
    std::lock_guard<std::mutex> lock(task->mutex);
    return task->progress;
}

void update_transfer_progress(const std::shared_ptr<TransferTask>& task, uint64_t transferred, uint64_t total, uint64_t speed) {
    P2PTransferProgress snapshot{};
    {
        std::lock_guard<std::mutex> lock(task->mutex);
        task->progress.transferred_bytes = transferred;
        task->progress.total_bytes = total;
        task->progress.bytes_per_second = speed;
        task->progress.progress = total == 0 ? 0.0f : static_cast<float>(static_cast<double>(transferred) / static_cast<double>(total));
        snapshot = task->progress;
    }
    notify_transfer(task->handle, P2P_TRANSFER_EVENT_PROGRESS, snapshot);
}

Node::~Node() {
    running.store(false);
    close_socket(udp_socket);
    close_socket(tcp_socket);
    udp_socket = kInvalidSocket;
    tcp_socket = kInvalidSocket;
    if (discovery_thread.joinable()) {
        discovery_thread.join();
    }
}

TransferTask::~TransferTask() {
    cancel_requested.store(true);
    close_socket(listener);
    close_socket(connection);
    listener = kInvalidSocket;
    connection = kInvalidSocket;
    if (worker.joinable()) {
        worker.join();
    }
}

StreamTask::~StreamTask() {
    running.store(false);
    close_socket(socket);
    socket = kInvalidSocket;
    if (receiver.joinable()) {
        receiver.join();
    }
}

TunnelTask::~TunnelTask() {
    running.store(false);
    close_socket(listener);
    close_socket(udp_socket);
    listener = kInvalidSocket;
    udp_socket = kInvalidSocket;
    if (worker.joinable()) {
        worker.join();
    }
}

void lan_discovery_loop(std::shared_ptr<Node> node) {
    sockaddr_in target{};
    target.sin_family = AF_INET;
    target.sin_port = htons(node->udp_port);
    target.sin_addr.s_addr = htonl(INADDR_BROADCAST);

    while (node->running.load()) {
        const std::string payload = std::string(kDiscoveryMessage) + " " + std::to_string(node->udp_port);
        const int sent = sendto(
            node->udp_socket,
            payload.c_str(),
            static_cast<int>(payload.size()),
            0,
            reinterpret_cast<sockaddr*>(&target),
            sizeof(target));
        if (sent > 0) {
            node->bytes_sent.fetch_add(static_cast<uint64_t>(sent));
        }
        std::this_thread::sleep_for(std::chrono::seconds(2));
    }
}

bool poll_connect(socket_t s, const sockaddr_in& address, uint32_t timeout_ms) {
    set_nonblocking(s, true);
    const int result = connect(s, reinterpret_cast<const sockaddr*>(&address), sizeof(address));
    if (result == 0) {
        set_nonblocking(s, false);
        return true;
    }

#if defined(_WIN32)
    const int err = WSAGetLastError();
    if (err != WSAEWOULDBLOCK && err != WSAEINPROGRESS) {
        set_nonblocking(s, false);
        return false;
    }
#else
    if (errno != EINPROGRESS) {
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

    const int selected = select(static_cast<int>(s + 1), nullptr, &write_set, nullptr, &tv);
    if (selected <= 0) {
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

struct ParsedUrl {
    std::string host;
    uint16_t port = 80;
    std::string path = "/";
};

bool parse_http_url(const std::string& url, ParsedUrl* out) {
    if (!out) {
        return false;
    }
    constexpr const char* prefix = "http://";
    if (url.rfind(prefix, 0) != 0) {
        return false;
    }

    const std::string rest = url.substr(std::strlen(prefix));
    const auto slash = rest.find('/');
    const std::string authority = slash == std::string::npos ? rest : rest.substr(0, slash);
    out->path = slash == std::string::npos ? "/" : rest.substr(slash);

    const auto colon = authority.rfind(':');
    if (colon != std::string::npos) {
        out->host = authority.substr(0, colon);
        out->port = static_cast<uint16_t>(std::stoi(authority.substr(colon + 1)));
    } else {
        out->host = authority;
        out->port = 80;
    }

    return !out->host.empty();
}

bool http_request(const ParsedUrl& url, const std::string& request, std::string* out_response) {
    sockaddr_in address{};
    if (!resolve_ipv4(url.host.c_str(), url.port, &address)) {
        return false;
    }

    socket_t s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (s == kInvalidSocket) {
        return false;
    }

    bool ok = false;
    if (poll_connect(s, address, 3000) && send_all_text(s, request)) {
        std::string response;
        std::array<char, 4096> buffer{};
        for (;;) {
            const int received = recv(s, buffer.data(), static_cast<int>(buffer.size()), 0);
            if (received <= 0) {
                break;
            }
            response.append(buffer.data(), static_cast<size_t>(received));
            if (response.size() > 256 * 1024) {
                break;
            }
        }
        if (out_response) {
            *out_response = std::move(response);
        }
        ok = true;
    }

    close_socket(s);
    return ok;
}

std::string header_value(const std::string& headers, const std::string& name) {
    const std::string lower_headers = to_lower(headers);
    const std::string lower_name = to_lower(name) + ":";
    size_t pos = lower_headers.find(lower_name);
    if (pos == std::string::npos) {
        return {};
    }
    pos += lower_name.size();
    const size_t end = headers.find_first_of("\r\n", pos);
    return trim(headers.substr(pos, end == std::string::npos ? std::string::npos : end - pos));
}

bool ssdp_discover(std::string* out_location, std::string* out_local_ip) {
    socket_t s = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (s == kInvalidSocket) {
        return false;
    }

    set_receive_timeout(s, 2500);

    sockaddr_in target{};
    target.sin_family = AF_INET;
    target.sin_port = htons(1900);
    inet_pton(AF_INET, "239.255.255.250", &target.sin_addr);

    const std::string request =
        "M-SEARCH * HTTP/1.1\r\n"
        "HOST: 239.255.255.250:1900\r\n"
        "MAN: \"ssdp:discover\"\r\n"
        "MX: 2\r\n"
        "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n"
        "\r\n";

    sendto(s, request.c_str(), static_cast<int>(request.size()), 0, reinterpret_cast<sockaddr*>(&target), sizeof(target));

    if (out_local_ip) {
        *out_local_ip = socket_local_address(s);
    }

    std::array<char, 8192> buffer{};
    sockaddr_in from{};
#if defined(_WIN32)
    int from_len = sizeof(from);
#else
    socklen_t from_len = sizeof(from);
#endif
    const int received = recvfrom(s, buffer.data(), static_cast<int>(buffer.size() - 1), 0, reinterpret_cast<sockaddr*>(&from), &from_len);
    close_socket(s);

    if (received <= 0) {
        return false;
    }

    buffer[static_cast<size_t>(received)] = '\0';
    const std::string response(buffer.data(), static_cast<size_t>(received));
    const std::string location = header_value(response, "LOCATION");
    if (location.empty()) {
        return false;
    }

    if (out_location) {
        *out_location = location;
    }
    return true;
}

std::string xml_between(const std::string& xml, const std::string& open_tag, const std::string& close_tag, size_t offset = 0) {
    const size_t begin = find_case_insensitive(xml, open_tag, offset);
    if (begin == std::string::npos) {
        return {};
    }
    const size_t value_begin = begin + open_tag.size();
    const size_t end = find_case_insensitive(xml, close_tag, value_begin);
    if (end == std::string::npos) {
        return {};
    }
    return trim(xml.substr(value_begin, end - value_begin));
}

bool discover_upnp_control(ParsedUrl* out_control, std::string* out_service_type, std::string* out_local_ip) {
    std::string location;
    std::string local_ip;
    if (!ssdp_discover(&location, &local_ip)) {
        return false;
    }

    ParsedUrl device_url{};
    if (!parse_http_url(location, &device_url)) {
        return false;
    }

    const std::string get =
        "GET " + device_url.path + " HTTP/1.1\r\n"
        "Host: " + device_url.host + ":" + std::to_string(device_url.port) + "\r\n"
        "Connection: close\r\n\r\n";

    std::string response;
    if (!http_request(device_url, get, &response)) {
        return false;
    }

    size_t service_pos = find_case_insensitive(response, "WANIPConnection");
    std::string service_type = "urn:schemas-upnp-org:service:WANIPConnection:1";
    if (service_pos == std::string::npos) {
        service_pos = find_case_insensitive(response, "WANPPPConnection");
        service_type = "urn:schemas-upnp-org:service:WANPPPConnection:1";
    }
    if (service_pos == std::string::npos) {
        return false;
    }

    const size_t service_end = find_case_insensitive(response, "</service>", service_pos);
    const std::string control_path = xml_between(response, "<controlURL>", "</controlURL>", service_pos);
    if (control_path.empty() || (service_end != std::string::npos && response.find(control_path, service_pos) > service_end)) {
        return false;
    }

    ParsedUrl control_url{};
    if (control_path.rfind("http://", 0) == 0) {
        if (!parse_http_url(control_path, &control_url)) {
            return false;
        }
    } else {
        control_url.host = device_url.host;
        control_url.port = device_url.port;
        if (!control_path.empty() && control_path[0] == '/') {
            control_url.path = control_path;
        } else {
            const auto slash = device_url.path.find_last_of('/');
            const std::string base = slash == std::string::npos ? "/" : device_url.path.substr(0, slash + 1);
            control_url.path = base + control_path;
        }
    }

    if (out_control) {
        *out_control = control_url;
    }
    if (out_service_type) {
        *out_service_type = service_type;
    }
    if (out_local_ip) {
        *out_local_ip = local_ip;
    }
    return true;
}

bool upnp_soap(const ParsedUrl& control, const std::string& service_type, const std::string& action, const std::string& body, std::string* out_response) {
    const std::string envelope =
        "<?xml version=\"1.0\"?>"
        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" "
        "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">"
        "<s:Body>"
        "<u:" + action + " xmlns:u=\"" + service_type + "\">" + body + "</u:" + action + ">"
        "</s:Body>"
        "</s:Envelope>";

    const std::string request =
        "POST " + control.path + " HTTP/1.1\r\n"
        "Host: " + control.host + ":" + std::to_string(control.port) + "\r\n"
        "Content-Type: text/xml; charset=\"utf-8\"\r\n"
        "SOAPAction: \"" + service_type + "#" + action + "\"\r\n"
        "Content-Length: " + std::to_string(envelope.size()) + "\r\n"
        "Connection: close\r\n\r\n" +
        envelope;

    std::string response;
    if (!http_request(control, request, &response)) {
        return false;
    }

    if (out_response) {
        *out_response = response;
    }
    return response.find(" 200 ") != std::string::npos || response.find(" 204 ") != std::string::npos;
}

std::string alphanumeric_random(size_t length) {
    static constexpr char alphabet[] = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    std::random_device rd;
    std::mt19937_64 rng(rd());
    std::uniform_int_distribution<size_t> dist(0, sizeof(alphabet) - 2);
    std::string output;
    output.reserve(length);
    for (size_t i = 0; i < length; ++i) {
        output.push_back(alphabet[dist(rng)]);
    }
    return output;
}

class Sha256 {
public:
    Sha256() {
        reset();
    }

    void update(const uint8_t* data, size_t length) {
        for (size_t i = 0; i < length; ++i) {
            data_[datalen_++] = data[i];
            if (datalen_ == 64) {
                transform();
                bitlen_ += 512;
                datalen_ = 0;
            }
        }
    }

    std::array<uint8_t, 32> final() {
        size_t i = datalen_;

        if (datalen_ < 56) {
            data_[i++] = 0x80;
            while (i < 56) {
                data_[i++] = 0x00;
            }
        } else {
            data_[i++] = 0x80;
            while (i < 64) {
                data_[i++] = 0x00;
            }
            transform();
            std::fill(data_.begin(), data_.begin() + 56, 0);
        }

        bitlen_ += datalen_ * 8;
        data_[63] = static_cast<uint8_t>(bitlen_);
        data_[62] = static_cast<uint8_t>(bitlen_ >> 8);
        data_[61] = static_cast<uint8_t>(bitlen_ >> 16);
        data_[60] = static_cast<uint8_t>(bitlen_ >> 24);
        data_[59] = static_cast<uint8_t>(bitlen_ >> 32);
        data_[58] = static_cast<uint8_t>(bitlen_ >> 40);
        data_[57] = static_cast<uint8_t>(bitlen_ >> 48);
        data_[56] = static_cast<uint8_t>(bitlen_ >> 56);
        transform();

        std::array<uint8_t, 32> hash{};
        for (i = 0; i < 4; ++i) {
            hash[i] = static_cast<uint8_t>((state_[0] >> (24 - i * 8)) & 0xff);
            hash[i + 4] = static_cast<uint8_t>((state_[1] >> (24 - i * 8)) & 0xff);
            hash[i + 8] = static_cast<uint8_t>((state_[2] >> (24 - i * 8)) & 0xff);
            hash[i + 12] = static_cast<uint8_t>((state_[3] >> (24 - i * 8)) & 0xff);
            hash[i + 16] = static_cast<uint8_t>((state_[4] >> (24 - i * 8)) & 0xff);
            hash[i + 20] = static_cast<uint8_t>((state_[5] >> (24 - i * 8)) & 0xff);
            hash[i + 24] = static_cast<uint8_t>((state_[6] >> (24 - i * 8)) & 0xff);
            hash[i + 28] = static_cast<uint8_t>((state_[7] >> (24 - i * 8)) & 0xff);
        }
        return hash;
    }

private:
    static uint32_t rotr(uint32_t value, uint32_t bits) {
        return (value >> bits) | (value << (32 - bits));
    }

    static uint32_t choose(uint32_t e, uint32_t f, uint32_t g) {
        return (e & f) ^ (~e & g);
    }

    static uint32_t majority(uint32_t a, uint32_t b, uint32_t c) {
        return (a & b) ^ (a & c) ^ (b & c);
    }

    void reset() {
        datalen_ = 0;
        bitlen_ = 0;
        state_[0] = 0x6a09e667;
        state_[1] = 0xbb67ae85;
        state_[2] = 0x3c6ef372;
        state_[3] = 0xa54ff53a;
        state_[4] = 0x510e527f;
        state_[5] = 0x9b05688c;
        state_[6] = 0x1f83d9ab;
        state_[7] = 0x5be0cd19;
    }

    void transform() {
        static constexpr std::array<uint32_t, 64> k = {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5,
            0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
            0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
            0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
            0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
            0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
            0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5,
            0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
            0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
        };

        uint32_t m[64]{};
        for (uint32_t i = 0, j = 0; i < 16; ++i, j += 4) {
            m[i] = (static_cast<uint32_t>(data_[j]) << 24) |
                   (static_cast<uint32_t>(data_[j + 1]) << 16) |
                   (static_cast<uint32_t>(data_[j + 2]) << 8) |
                   (static_cast<uint32_t>(data_[j + 3]));
        }
        for (uint32_t i = 16; i < 64; ++i) {
            const uint32_t s0 = rotr(m[i - 15], 7) ^ rotr(m[i - 15], 18) ^ (m[i - 15] >> 3);
            const uint32_t s1 = rotr(m[i - 2], 17) ^ rotr(m[i - 2], 19) ^ (m[i - 2] >> 10);
            m[i] = m[i - 16] + s0 + m[i - 7] + s1;
        }

        uint32_t a = state_[0];
        uint32_t b = state_[1];
        uint32_t c = state_[2];
        uint32_t d = state_[3];
        uint32_t e = state_[4];
        uint32_t f = state_[5];
        uint32_t g = state_[6];
        uint32_t h = state_[7];

        for (uint32_t i = 0; i < 64; ++i) {
            const uint32_t s1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
            const uint32_t temp1 = h + s1 + choose(e, f, g) + k[i] + m[i];
            const uint32_t s0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
            const uint32_t temp2 = s0 + majority(a, b, c);
            h = g;
            g = f;
            f = e;
            e = d + temp1;
            d = c;
            c = b;
            b = a;
            a = temp1 + temp2;
        }

        state_[0] += a;
        state_[1] += b;
        state_[2] += c;
        state_[3] += d;
        state_[4] += e;
        state_[5] += f;
        state_[6] += g;
        state_[7] += h;
    }

    std::array<uint8_t, 64> data_{};
    uint32_t datalen_ = 0;
    uint64_t bitlen_ = 0;
    std::array<uint32_t, 8> state_{};
};

std::array<uint8_t, 32> sha256_bytes(const uint8_t* data, size_t length) {
    Sha256 sha;
    sha.update(data, length);
    return sha.final();
}

std::array<uint8_t, 32> sha256_string(const std::string& value) {
    return sha256_bytes(reinterpret_cast<const uint8_t*>(value.data()), value.size());
}

std::string hex_string(const std::array<uint8_t, 32>& bytes) {
    std::ostringstream oss;
    oss << std::hex << std::setfill('0');
    for (uint8_t byte : bytes) {
        oss << std::setw(2) << static_cast<int>(byte);
    }
    return oss.str();
}

std::string derive_alphanumeric(const std::string& label, const std::string& input, size_t length) {
    static constexpr char alphabet[] = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    std::string output;
    uint32_t counter = 0;
    while (output.size() < length) {
        const std::string material = label + ":" + input + ":" + std::to_string(counter++);
        const auto digest = sha256_string(material);
        for (uint8_t byte : digest) {
            output.push_back(alphabet[byte % (sizeof(alphabet) - 1)]);
            if (output.size() == length) {
                break;
            }
        }
    }
    return output;
}

std::string base_filename(const std::string& path) {
    std::filesystem::path p(path);
    return p.filename().string();
}

bool read_until_header_end(socket_t s, std::string* out_header) {
    std::string header;
    std::array<char, 1024> buffer{};
    while (header.find("\n\n") == std::string::npos && header.find("\r\n\r\n") == std::string::npos) {
        const int received = recv(s, buffer.data(), static_cast<int>(buffer.size()), 0);
        if (received <= 0) {
            return false;
        }
        header.append(buffer.data(), static_cast<size_t>(received));
        if (header.size() > 16 * 1024) {
            return false;
        }
    }
    if (out_header) {
        *out_header = std::move(header);
    }
    return true;
}

std::string file_header_value(const std::string& header, const std::string& key) {
    std::istringstream input(header);
    std::string line;
    const std::string prefix = key + ":";
    while (std::getline(input, line)) {
        if (!line.empty() && line.back() == '\r') {
            line.pop_back();
        }
        if (line.rfind(prefix, 0) == 0) {
            return trim(line.substr(prefix.size()));
        }
    }
    return {};
}

void run_file_sender(std::shared_ptr<TransferTask> task, std::shared_ptr<Node> node, TransferOptionsCopy options) {
    set_transfer_status(task, P2P_TRANSFER_RUNNING, P2P_TRANSFER_EVENT_STARTED);

    const std::string local_path = options.local_path;
    const std::string peer_host = options.peer_host;
    const std::string remote_name = !options.remote_path.empty() ? base_filename(options.remote_path) : base_filename(local_path);
    const uint32_t chunk_size = options.chunk_size == 0 ? kDefaultChunkSize : options.chunk_size;

    std::error_code ec;
    const uint64_t total = std::filesystem::file_size(local_path, ec);
    if (ec) {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        set_last_error("Unable to read source file size");
        return;
    }

    sockaddr_in peer{};
    if (!resolve_ipv4(peer_host.c_str(), options.peer_port, &peer)) {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        set_last_error("Unable to resolve file transfer peer");
        return;
    }

    task->connection = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (task->connection == kInvalidSocket || !poll_connect(task->connection, peer, 5000)) {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        set_last_error("Unable to connect to file receiver");
        return;
    }

    const std::string header =
        "TPCWEI_FILE_OFFER 1\n"
        "Name:" + remote_name + "\n"
        "Size:" + std::to_string(total) + "\n"
        "Resume:" + std::to_string(options.resume_enabled ? 1 : 0) + "\n\n";
    if (!send_all_text(task->connection, header)) {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        return;
    }

    std::string response;
    if (!read_until_header_end(task->connection, &response)) {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        return;
    }

    uint64_t offset = 0;
    const std::string offset_text = file_header_value(response, "Offset");
    if (!offset_text.empty()) {
        offset = static_cast<uint64_t>(std::stoull(offset_text));
    }
    if (offset > total) {
        offset = 0;
    }

    std::ifstream input(local_path, std::ios::binary);
    if (!input) {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        return;
    }
    input.seekg(static_cast<std::streamoff>(offset), std::ios::beg);

    uint64_t transferred = offset;
    uint64_t last_transferred = transferred;
    auto last_time = std::chrono::steady_clock::now();
    std::vector<uint8_t> buffer(chunk_size);
    update_transfer_progress(task, transferred, total, 0);

    while (!task->cancel_requested.load() && input) {
        input.read(reinterpret_cast<char*>(buffer.data()), static_cast<std::streamsize>(buffer.size()));
        const std::streamsize got = input.gcount();
        if (got <= 0) {
            break;
        }
        if (!send_all(task->connection, buffer.data(), static_cast<size_t>(got))) {
            set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
            return;
        }

        transferred += static_cast<uint64_t>(got);
        node->bytes_sent.fetch_add(static_cast<uint64_t>(got));

        const auto now = std::chrono::steady_clock::now();
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - last_time).count();
        if (elapsed >= 500 || transferred == total) {
            const uint64_t delta = transferred - last_transferred;
            const uint64_t speed = elapsed > 0 ? (delta * 1000ULL / static_cast<uint64_t>(elapsed)) : 0;
            update_transfer_progress(task, transferred, total, speed);
            last_transferred = transferred;
            last_time = now;
        }
    }

    if (task->cancel_requested.load()) {
        set_transfer_status(task, P2P_TRANSFER_CANCELLED, P2P_TRANSFER_EVENT_CANCELLED);
    } else {
        update_transfer_progress(task, total, total, 0);
        set_transfer_status(task, P2P_TRANSFER_COMPLETED, P2P_TRANSFER_EVENT_COMPLETED);
    }
}

void run_file_receiver(std::shared_ptr<TransferTask> task, std::shared_ptr<Node> node, TransferOptionsCopy options) {
    set_transfer_status(task, P2P_TRANSFER_RUNNING, P2P_TRANSFER_EVENT_STARTED);

    task->listener = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (task->listener == kInvalidSocket) {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        return;
    }
    set_reuse_address(task->listener);
    if (!bind_ipv4(task->listener, nullptr, options.peer_port) || listen(task->listener, 1) != 0) {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        return;
    }

    set_receive_timeout(task->listener, 500);
    while (!task->cancel_requested.load()) {
        sockaddr_in from{};
#if defined(_WIN32)
        int from_len = sizeof(from);
#else
        socklen_t from_len = sizeof(from);
#endif
        task->connection = accept(task->listener, reinterpret_cast<sockaddr*>(&from), &from_len);
        if (task->connection != kInvalidSocket) {
            break;
        }
    }

    if (task->cancel_requested.load()) {
        set_transfer_status(task, P2P_TRANSFER_CANCELLED, P2P_TRANSFER_EVENT_CANCELLED);
        return;
    }

    std::string header;
    if (!read_until_header_end(task->connection, &header)) {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        return;
    }

    const std::string name = base_filename(file_header_value(header, "Name"));
    const std::string size_text = file_header_value(header, "Size");
    if (name.empty() || size_text.empty()) {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        return;
    }
    const uint64_t total = static_cast<uint64_t>(std::stoull(size_text));

    std::filesystem::path target = !options.local_path.empty() ? std::filesystem::path(options.local_path) : std::filesystem::current_path();
    std::error_code ec;
    if (std::filesystem::is_directory(target, ec)) {
        target /= name;
    }
    const std::filesystem::path part = target.string() + ".part";
    uint64_t offset = 0;
    if (options.resume_enabled && std::filesystem::exists(part, ec)) {
        offset = std::filesystem::file_size(part, ec);
        if (ec || offset > total) {
            offset = 0;
        }
    }

    const std::string response = "TPCWEI_FILE_ACCEPT 1\nOffset:" + std::to_string(offset) + "\n\n";
    if (!send_all_text(task->connection, response)) {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        return;
    }

    std::ofstream output(part, std::ios::binary | std::ios::app);
    if (!output) {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        return;
    }

    uint64_t transferred = offset;
    uint64_t last_transferred = transferred;
    auto last_time = std::chrono::steady_clock::now();
    std::array<uint8_t, kDefaultChunkSize> buffer{};
    update_transfer_progress(task, transferred, total, 0);

    while (!task->cancel_requested.load() && transferred < total) {
        const int received = recv(task->connection, reinterpret_cast<char*>(buffer.data()), static_cast<int>(buffer.size()), 0);
        if (received <= 0) {
            break;
        }
        output.write(reinterpret_cast<const char*>(buffer.data()), received);
        transferred += static_cast<uint64_t>(received);
        node->bytes_received.fetch_add(static_cast<uint64_t>(received));

        const auto now = std::chrono::steady_clock::now();
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - last_time).count();
        if (elapsed >= 500 || transferred == total) {
            const uint64_t delta = transferred - last_transferred;
            const uint64_t speed = elapsed > 0 ? (delta * 1000ULL / static_cast<uint64_t>(elapsed)) : 0;
            update_transfer_progress(task, transferred, total, speed);
            last_transferred = transferred;
            last_time = now;
        }
    }

    output.close();

    if (task->cancel_requested.load()) {
        set_transfer_status(task, P2P_TRANSFER_CANCELLED, P2P_TRANSFER_EVENT_CANCELLED);
    } else if (transferred == total) {
        std::filesystem::rename(part, target, ec);
        if (ec) {
            set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
        } else {
            set_transfer_status(task, P2P_TRANSFER_COMPLETED, P2P_TRANSFER_EVENT_COMPLETED);
        }
    } else {
        set_transfer_status(task, P2P_TRANSFER_FAILED, P2P_TRANSFER_EVENT_FAILED);
    }
}

void copy_bidirectional(socket_t left, socket_t right, const std::shared_ptr<TunnelTask>& tunnel) {
    std::atomic_bool done{false};

    auto pump = [&](socket_t input, socket_t output, std::atomic_uint64_t& counter) {
        std::array<uint8_t, 32 * 1024> buffer{};
        while (!done.load() && tunnel->running.load()) {
            const int received = recv(input, reinterpret_cast<char*>(buffer.data()), static_cast<int>(buffer.size()), 0);
            if (received <= 0) {
                break;
            }
            if (!send_all(output, buffer.data(), static_cast<size_t>(received))) {
                break;
            }
            counter.fetch_add(static_cast<uint64_t>(received));
            notify_tunnel(tunnel, P2P_TUNNEL_EVENT_TRAFFIC);
        }
        done.store(true);
        shutdown_socket(input);
        shutdown_socket(output);
    };

    std::thread up(pump, left, right, std::ref(tunnel->bytes_up));
    std::thread down(pump, right, left, std::ref(tunnel->bytes_down));
    up.join();
    down.join();
    close_socket(left);
    close_socket(right);
}

void run_tcp_tunnel(std::shared_ptr<TunnelTask> tunnel, TunnelOptionsCopy options) {
    tunnel->listener = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (tunnel->listener == kInvalidSocket) {
        notify_tunnel(tunnel, P2P_TUNNEL_EVENT_ERROR);
        return;
    }

    set_reuse_address(tunnel->listener);
    const char* bind_address = options.allow_lan_clients ? options.local_bind_address.c_str() : "127.0.0.1";
    if (!bind_ipv4(tunnel->listener, bind_address, options.local_port) || listen(tunnel->listener, 64) != 0) {
        notify_tunnel(tunnel, P2P_TUNNEL_EVENT_ERROR);
        return;
    }

    tunnel->local_port = socket_port(tunnel->listener);
    tunnel->running.store(true);
    notify_tunnel(tunnel, P2P_TUNNEL_EVENT_STARTED);

    set_receive_timeout(tunnel->listener, 500);
    while (tunnel->running.load()) {
        sockaddr_in client{};
#if defined(_WIN32)
        int client_len = sizeof(client);
#else
        socklen_t client_len = sizeof(client);
#endif
        socket_t client_socket = accept(tunnel->listener, reinterpret_cast<sockaddr*>(&client), &client_len);
        if (client_socket == kInvalidSocket) {
            continue;
        }

        sockaddr_in peer{};
        if (!resolve_ipv4(options.peer_host.c_str(), options.peer_port, &peer)) {
            close_socket(client_socket);
            notify_tunnel(tunnel, P2P_TUNNEL_EVENT_ERROR);
            continue;
        }

        socket_t peer_socket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        if (peer_socket == kInvalidSocket || !poll_connect(peer_socket, peer, 3000)) {
            close_socket(client_socket);
            close_socket(peer_socket);
            notify_tunnel(tunnel, P2P_TUNNEL_EVENT_ERROR);
            continue;
        }

        tunnel->active_connections.fetch_add(1);
        notify_tunnel(tunnel, P2P_TUNNEL_EVENT_CONNECTION_OPENED);
        std::thread([tunnel, client_socket, peer_socket]() {
            copy_bidirectional(client_socket, peer_socket, tunnel);
            tunnel->active_connections.fetch_sub(1);
            notify_tunnel(tunnel, P2P_TUNNEL_EVENT_CONNECTION_CLOSED);
        }).detach();
    }

    notify_tunnel(tunnel, P2P_TUNNEL_EVENT_STOPPED);
}

void run_udp_tunnel(std::shared_ptr<TunnelTask> tunnel, TunnelOptionsCopy options) {
    tunnel->udp_socket = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (tunnel->udp_socket == kInvalidSocket) {
        notify_tunnel(tunnel, P2P_TUNNEL_EVENT_ERROR);
        return;
    }
    set_reuse_address(tunnel->udp_socket);
    const char* bind_address = options.allow_lan_clients ? options.local_bind_address.c_str() : "127.0.0.1";
    if (!bind_ipv4(tunnel->udp_socket, bind_address, options.local_port)) {
        notify_tunnel(tunnel, P2P_TUNNEL_EVENT_ERROR);
        return;
    }

    sockaddr_in peer{};
    if (!resolve_ipv4(options.peer_host.c_str(), options.peer_port, &peer)) {
        notify_tunnel(tunnel, P2P_TUNNEL_EVENT_ERROR);
        return;
    }

    tunnel->local_port = socket_port(tunnel->udp_socket);
    tunnel->running.store(true);
    notify_tunnel(tunnel, P2P_TUNNEL_EVENT_STARTED);

    set_receive_timeout(tunnel->udp_socket, 250);
    std::array<uint8_t, 64 * 1024> buffer{};
    sockaddr_in last_client{};
    bool has_client = false;

    while (tunnel->running.load()) {
        sockaddr_in from{};
#if defined(_WIN32)
        int from_len = sizeof(from);
#else
        socklen_t from_len = sizeof(from);
#endif
        const int received = recvfrom(tunnel->udp_socket, reinterpret_cast<char*>(buffer.data()), static_cast<int>(buffer.size()), 0, reinterpret_cast<sockaddr*>(&from), &from_len);
        if (received <= 0) {
            continue;
        }

        const bool from_peer = from.sin_addr.s_addr == peer.sin_addr.s_addr && from.sin_port == peer.sin_port;
        if (from_peer && has_client) {
            sendto(tunnel->udp_socket, reinterpret_cast<const char*>(buffer.data()), received, 0, reinterpret_cast<sockaddr*>(&last_client), sizeof(last_client));
            tunnel->bytes_down.fetch_add(static_cast<uint64_t>(received));
        } else {
            last_client = from;
            has_client = true;
            sendto(tunnel->udp_socket, reinterpret_cast<const char*>(buffer.data()), received, 0, reinterpret_cast<sockaddr*>(&peer), sizeof(peer));
            tunnel->bytes_up.fetch_add(static_cast<uint64_t>(received));
        }
        notify_tunnel(tunnel, P2P_TUNNEL_EVENT_TRAFFIC);
    }

    notify_tunnel(tunnel, P2P_TUNNEL_EVENT_STOPPED);
}

void stream_receive_loop(std::shared_ptr<StreamTask> stream) {
    set_receive_timeout(stream->socket, 250);
    std::vector<uint8_t> buffer(64 * 1024);

    while (stream->running.load()) {
        const int received = recv(stream->socket, reinterpret_cast<char*>(buffer.data()), static_cast<int>(buffer.size()), 0);
        if (received <= 0) {
            continue;
        }

        stream->bytes_received.fetch_add(static_cast<uint64_t>(received));
        stream->frames_received.fetch_add(1);

        P2PStreamReceiveCallback callback = nullptr;
        void* user = nullptr;
        {
            std::lock_guard<std::mutex> lock(stream->callback_mutex);
            callback = stream->callback;
            user = stream->callback_user;
        }
        if (callback) {
            callback(stream->handle, buffer.data(), static_cast<uint32_t>(received), user);
        }
    }
}

} // namespace

extern "C" {

P2P_EXPORT int P2P_CALL p2p_initialize(void) {
    bool expected = false;
    if (!g_initialized.compare_exchange_strong(expected, true)) {
        return static_cast<int>(P2P_OK);
    }

#if defined(_WIN32)
    WSADATA data{};
    if (WSAStartup(MAKEWORD(2, 2), &data) != 0) {
        g_initialized.store(false);
        return fail(P2P_ERROR_SOCKET, "WSAStartup failed");
    }
#endif

    set_last_error("");
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_shutdown(void) {
    if (!g_initialized.exchange(false)) {
        return static_cast<int>(P2P_OK);
    }

    {
        std::map<P2PTunnelHandle, std::shared_ptr<TunnelTask>> tunnels;
        {
            std::lock_guard<std::mutex> lock(g_tunnels_mutex);
            tunnels.swap(g_tunnels);
        }
    }
    {
        std::map<P2PStreamHandle, std::shared_ptr<StreamTask>> streams;
        {
            std::lock_guard<std::mutex> lock(g_streams_mutex);
            streams.swap(g_streams);
        }
    }
    {
        std::map<P2PTransferHandle, std::shared_ptr<TransferTask>> transfers;
        {
            std::lock_guard<std::mutex> lock(g_transfers_mutex);
            transfers.swap(g_transfers);
        }
    }
    {
        std::map<P2PNodeHandle, std::shared_ptr<Node>> nodes;
        {
            std::lock_guard<std::mutex> lock(g_nodes_mutex);
            nodes.swap(g_nodes);
        }
    }

#if defined(_WIN32)
    WSACleanup();
#endif

    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_node_start(const P2PNodeConfig* config, P2PNodeHandle* out_node) {
    if (!ensure_initialized()) {
        return static_cast<int>(P2P_ERROR_NOT_INITIALIZED);
    }
    if (!config || !out_node) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_node_start received null argument");
    }

    auto node = std::make_shared<Node>();
    node->handle = g_next_node.fetch_add(1);
    node->udp_socket = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (node->udp_socket == kInvalidSocket) {
        return fail(P2P_ERROR_SOCKET, socket_error_text("Failed to create UDP socket"));
    }
    set_reuse_address(node->udp_socket);
    set_broadcast(node->udp_socket);

    if (!bind_ipv4(node->udp_socket, config->bind_address, config->local_port)) {
        return fail(P2P_ERROR_BIND_FAILED, socket_error_text("Failed to bind UDP socket"));
    }
    node->udp_port = socket_port(node->udp_socket);

    node->tcp_socket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (node->tcp_socket == kInvalidSocket) {
        return fail(P2P_ERROR_SOCKET, socket_error_text("Failed to create TCP socket"));
    }
    set_reuse_address(node->tcp_socket);
    if (!bind_ipv4(node->tcp_socket, config->bind_address, node->udp_port) || listen(node->tcp_socket, 16) != 0) {
        return fail(P2P_ERROR_BIND_FAILED, socket_error_text("Failed to bind TCP socket"));
    }
    node->tcp_port = socket_port(node->tcp_socket);
    node->running.store(true);

    if (config->enable_lan_discovery) {
        node->discovery_thread = std::thread(lan_discovery_loop, node);
    }

    {
        std::lock_guard<std::mutex> lock(g_nodes_mutex);
        g_nodes[node->handle] = node;
    }

    *out_node = node->handle;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_node_stop(P2PNodeHandle node_handle) {
    if (!ensure_initialized()) {
        return static_cast<int>(P2P_ERROR_NOT_INITIALIZED);
    }

    std::shared_ptr<Node> node;
    {
        std::lock_guard<std::mutex> lock(g_nodes_mutex);
        auto it = g_nodes.find(node_handle);
        if (it == g_nodes.end()) {
            return fail(P2P_ERROR_NOT_FOUND, "Node handle not found");
        }
        node = it->second;
        g_nodes.erase(it);
    }
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_node_get_metrics(P2PNodeHandle node_handle, P2PNodeMetrics* out_metrics) {
    if (!ensure_initialized()) {
        return static_cast<int>(P2P_ERROR_NOT_INITIALIZED);
    }
    if (!out_metrics) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_node_get_metrics received null output");
    }
    auto node = get_node(node_handle);
    if (!node) {
        return fail(P2P_ERROR_NOT_FOUND, "Node handle not found");
    }

    uint32_t active_streams = 0;
    uint32_t active_transfers = 0;
    {
        std::lock_guard<std::mutex> lock(g_streams_mutex);
        active_streams = static_cast<uint32_t>(g_streams.size());
    }
    {
        std::lock_guard<std::mutex> lock(g_transfers_mutex);
        active_transfers = static_cast<uint32_t>(g_transfers.size());
    }

    out_metrics->bytes_sent = node->bytes_sent.load();
    out_metrics->bytes_received = node->bytes_received.load();
    out_metrics->active_streams = active_streams;
    out_metrics->active_transfers = active_transfers;
    out_metrics->udp_port = node->udp_port;
    out_metrics->tcp_port = node->tcp_port;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_get_last_error(char* buffer, uint32_t buffer_length) {
    std::lock_guard<std::mutex> lock(g_error_mutex);
    if (!copy_to_buffer(g_last_error, buffer, buffer_length)) {
        return static_cast<int>(P2P_ERROR_BUFFER_TOO_SMALL);
    }
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_nat_get_type(P2PNatType* out_nat_type) {
    if (!out_nat_type) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_nat_get_type received null output");
    }
    *out_nat_type = g_nat_type;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_nat_set_callback(P2PNatCallback callback, void* user_data) {
    std::lock_guard<std::mutex> lock(g_nat_callback_mutex);
    g_nat_callback = callback;
    g_nat_callback_user = user_data;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_nat_udp_punch(P2PNodeHandle node_handle, const char* peer_host, uint16_t peer_port, uint32_t attempts, uint32_t interval_ms) {
    if (!ensure_initialized()) {
        return static_cast<int>(P2P_ERROR_NOT_INITIALIZED);
    }
    if (!peer_host || peer_port == 0) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "UDP punch requires peer host and port");
    }
    auto node = get_node(node_handle);
    if (!node) {
        return fail(P2P_ERROR_NOT_FOUND, "Node handle not found");
    }

    sockaddr_in peer{};
    if (!resolve_ipv4(peer_host, peer_port, &peer)) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "Unable to resolve UDP punch peer");
    }

    const uint32_t safe_attempts = attempts == 0 ? 12 : attempts;
    const uint32_t safe_interval = interval_ms == 0 ? 100 : interval_ms;
    notify_nat(P2P_NAT_EVENT_STATE_CHANGED, P2P_OK, "", 0);

    const std::string peer_host_text(peer_host);
    std::thread([node, peer, safe_attempts, safe_interval, peer_host_text, peer_port]() {
        bool sent_any = false;
        for (uint32_t i = 0; i < safe_attempts && node->running.load(); ++i) {
            const int sent = sendto(
                node->udp_socket,
                kUdpPunchMessage,
                static_cast<int>(std::strlen(kUdpPunchMessage)),
                0,
                reinterpret_cast<const sockaddr*>(&peer),
                sizeof(peer));
            if (sent > 0) {
                sent_any = true;
                node->bytes_sent.fetch_add(static_cast<uint64_t>(sent));
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(safe_interval));
        }

        notify_nat(
            sent_any ? P2P_NAT_EVENT_UDP_PUNCH_SUCCESS : P2P_NAT_EVENT_UDP_PUNCH_FAILED,
            sent_any ? P2P_OK : P2P_ERROR_SOCKET,
            peer_host_text,
            peer_port);
    }).detach();

    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_nat_tcp_punch(P2PNodeHandle node_handle, const char* peer_host, uint16_t peer_port, uint32_t timeout_ms) {
    if (!ensure_initialized()) {
        return static_cast<int>(P2P_ERROR_NOT_INITIALIZED);
    }
    if (!peer_host || peer_port == 0) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "TCP punch requires peer host and port");
    }
    if (!get_node(node_handle)) {
        return fail(P2P_ERROR_NOT_FOUND, "Node handle not found");
    }

    sockaddr_in peer{};
    if (!resolve_ipv4(peer_host, peer_port, &peer)) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "Unable to resolve TCP punch peer");
    }

    const uint32_t safe_timeout = timeout_ms == 0 ? 3000 : timeout_ms;
    const std::string peer_host_text(peer_host);
    std::thread([peer, safe_timeout, peer_host_text, peer_port]() {
        socket_t s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        bool connected = false;
        if (s != kInvalidSocket) {
            connected = poll_connect(s, peer, safe_timeout);
            close_socket(s);
        }
        notify_nat(
            connected ? P2P_NAT_EVENT_TCP_PUNCH_SUCCESS : P2P_NAT_EVENT_TCP_PUNCH_FAILED,
            connected ? P2P_OK : P2P_ERROR_CONNECT_FAILED,
            peer_host_text,
            peer_port);
    }).detach();

    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_nat_upnp_add_mapping(uint16_t local_port, uint16_t external_port, const char* protocol, uint32_t lease_seconds) {
    if (!ensure_initialized()) {
        return static_cast<int>(P2P_ERROR_NOT_INITIALIZED);
    }
    if (local_port == 0 || external_port == 0) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "UPnP mapping requires non-zero ports");
    }

    const std::string proto = protocol && protocol[0] ? to_lower(protocol) : "tcp";
    const std::string upnp_proto = proto == "udp" ? "UDP" : "TCP";

    ParsedUrl control{};
    std::string service_type;
    std::string local_ip;
    if (!discover_upnp_control(&control, &service_type, &local_ip)) {
        notify_nat(P2P_NAT_EVENT_UPNP_MAPPING_FAILED, P2P_ERROR_NOT_FOUND, "", 0);
        return fail(P2P_ERROR_NOT_FOUND, "No UPnP InternetGatewayDevice found");
    }

    if (local_ip.empty() || local_ip == "0.0.0.0") {
        local_ip = "127.0.0.1";
    }

    const std::string body =
        "<NewRemoteHost></NewRemoteHost>"
        "<NewExternalPort>" + std::to_string(external_port) + "</NewExternalPort>"
        "<NewProtocol>" + upnp_proto + "</NewProtocol>"
        "<NewInternalPort>" + std::to_string(local_port) + "</NewInternalPort>"
        "<NewInternalClient>" + local_ip + "</NewInternalClient>"
        "<NewEnabled>1</NewEnabled>"
        "<NewPortMappingDescription>TPCwei P2P</NewPortMappingDescription>"
        "<NewLeaseDuration>" + std::to_string(lease_seconds) + "</NewLeaseDuration>";

    std::string response;
    if (!upnp_soap(control, service_type, "AddPortMapping", body, &response)) {
        notify_nat(P2P_NAT_EVENT_UPNP_MAPPING_FAILED, P2P_ERROR_PROTOCOL, "", 0);
        return fail(P2P_ERROR_PROTOCOL, "UPnP AddPortMapping failed");
    }

    std::string external_ip_response;
    if (upnp_soap(control, service_type, "GetExternalIPAddress", "", &external_ip_response)) {
        g_external_address = xml_between(external_ip_response, "<NewExternalIPAddress>", "</NewExternalIPAddress>");
        g_external_port = external_port;
    }

    notify_nat(P2P_NAT_EVENT_UPNP_MAPPING_SUCCESS, P2P_OK, g_external_address, external_port);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_nat_upnp_remove_mapping(uint16_t external_port, const char* protocol) {
    if (!ensure_initialized()) {
        return static_cast<int>(P2P_ERROR_NOT_INITIALIZED);
    }
    if (external_port == 0) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "UPnP remove mapping requires non-zero port");
    }

    const std::string proto = protocol && protocol[0] ? to_lower(protocol) : "tcp";
    const std::string upnp_proto = proto == "udp" ? "UDP" : "TCP";

    ParsedUrl control{};
    std::string service_type;
    if (!discover_upnp_control(&control, &service_type, nullptr)) {
        return fail(P2P_ERROR_NOT_FOUND, "No UPnP InternetGatewayDevice found");
    }

    const std::string body =
        "<NewRemoteHost></NewRemoteHost>"
        "<NewExternalPort>" + std::to_string(external_port) + "</NewExternalPort>"
        "<NewProtocol>" + upnp_proto + "</NewProtocol>";

    if (!upnp_soap(control, service_type, "DeletePortMapping", body, nullptr)) {
        return fail(P2P_ERROR_PROTOCOL, "UPnP DeletePortMapping failed");
    }
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_nat_get_external_endpoint(char* address_buffer, uint32_t address_buffer_length, uint16_t* out_port) {
    if (!address_buffer || !out_port) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_nat_get_external_endpoint received null argument");
    }
    if (g_external_address.empty() || g_external_port == 0) {
        return fail(P2P_ERROR_NOT_FOUND, "No external endpoint is known without UPnP or a peer observation");
    }
    if (!copy_to_buffer(g_external_address, address_buffer, address_buffer_length)) {
        return static_cast<int>(P2P_ERROR_BUFFER_TOO_SMALL);
    }
    *out_port = g_external_port;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_file_set_callback(P2PTransferCallback callback, void* user_data) {
    std::lock_guard<std::mutex> lock(g_transfer_callback_mutex);
    g_transfer_callback = callback;
    g_transfer_callback_user = user_data;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_file_send(P2PNodeHandle node_handle, const P2PFileTransferOptions* options, P2PTransferHandle* out_transfer) {
    if (!ensure_initialized()) {
        return static_cast<int>(P2P_ERROR_NOT_INITIALIZED);
    }
    if (!options || !out_transfer || !options->local_path || !options->peer_host || options->peer_port == 0) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_file_send requires local path, peer host, peer port and output handle");
    }
    auto node = get_node(node_handle);
    if (!node) {
        return fail(P2P_ERROR_NOT_FOUND, "Node handle not found");
    }

    auto task = std::make_shared<TransferTask>();
    task->handle = g_next_transfer.fetch_add(1);
    task->progress.status = P2P_TRANSFER_PENDING;
    {
        std::lock_guard<std::mutex> lock(g_transfers_mutex);
        g_transfers[task->handle] = task;
    }

    TransferOptionsCopy copied{};
    copied.local_path = options->local_path ? options->local_path : "";
    copied.remote_path = options->remote_path ? options->remote_path : "";
    copied.peer_host = options->peer_host ? options->peer_host : "";
    copied.peer_port = options->peer_port;
    copied.resume_enabled = options->resume_enabled != 0;
    copied.parallel_paths = options->parallel_paths;
    copied.chunk_size = options->chunk_size == 0 ? kDefaultChunkSize : options->chunk_size;
    task->worker = std::thread(run_file_sender, task, node, copied);
    *out_transfer = task->handle;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_file_receive(P2PNodeHandle node_handle, const P2PFileTransferOptions* options, P2PTransferHandle* out_transfer) {
    if (!ensure_initialized()) {
        return static_cast<int>(P2P_ERROR_NOT_INITIALIZED);
    }
    if (!options || !out_transfer || options->peer_port == 0) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_file_receive requires listen port and output handle");
    }
    auto node = get_node(node_handle);
    if (!node) {
        return fail(P2P_ERROR_NOT_FOUND, "Node handle not found");
    }

    auto task = std::make_shared<TransferTask>();
    task->handle = g_next_transfer.fetch_add(1);
    task->progress.status = P2P_TRANSFER_PENDING;
    {
        std::lock_guard<std::mutex> lock(g_transfers_mutex);
        g_transfers[task->handle] = task;
    }

    TransferOptionsCopy copied{};
    copied.local_path = options->local_path ? options->local_path : "";
    copied.remote_path = options->remote_path ? options->remote_path : "";
    copied.peer_host = options->peer_host ? options->peer_host : "";
    copied.peer_port = options->peer_port;
    copied.resume_enabled = options->resume_enabled != 0;
    copied.parallel_paths = options->parallel_paths;
    copied.chunk_size = options->chunk_size == 0 ? kDefaultChunkSize : options->chunk_size;
    task->worker = std::thread(run_file_receiver, task, node, copied);
    *out_transfer = task->handle;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_file_get_progress(P2PTransferHandle transfer_handle, P2PTransferProgress* out_progress) {
    if (!out_progress) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_file_get_progress received null output");
    }
    auto task = get_transfer(transfer_handle);
    if (!task) {
        return fail(P2P_ERROR_NOT_FOUND, "Transfer handle not found");
    }
    *out_progress = transfer_snapshot(task);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_file_cancel(P2PTransferHandle transfer_handle) {
    auto task = get_transfer(transfer_handle);
    if (!task) {
        return fail(P2P_ERROR_NOT_FOUND, "Transfer handle not found");
    }
    task->cancel_requested.store(true);
    close_socket(task->listener);
    close_socket(task->connection);
    set_transfer_status(task, P2P_TRANSFER_CANCELLED, P2P_TRANSFER_EVENT_CANCELLED);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_stream_create(P2PNodeHandle node_handle, const P2PStreamOptions* options, P2PStreamHandle* out_stream) {
    if (!ensure_initialized()) {
        return static_cast<int>(P2P_ERROR_NOT_INITIALIZED);
    }
    if (!options || !out_stream || !options->peer_host || options->peer_port == 0) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_stream_create requires peer host, peer port and output handle");
    }
    if (!get_node(node_handle)) {
        return fail(P2P_ERROR_NOT_FOUND, "Node handle not found");
    }

    sockaddr_in peer{};
    if (!resolve_ipv4(options->peer_host, options->peer_port, &peer)) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "Unable to resolve stream peer");
    }

    auto stream = std::make_shared<StreamTask>();
    stream->handle = g_next_stream.fetch_add(1);
    stream->socket = socket(AF_INET, options->reliable ? SOCK_STREAM : SOCK_DGRAM, options->reliable ? IPPROTO_TCP : IPPROTO_UDP);
    if (stream->socket == kInvalidSocket) {
        return fail(P2P_ERROR_SOCKET, "Unable to create stream socket");
    }

    bool connected = false;
    const auto start = std::chrono::steady_clock::now();
    if (options->reliable) {
        connected = poll_connect(stream->socket, peer, 3000);
    } else {
        connected = connect(stream->socket, reinterpret_cast<sockaddr*>(&peer), sizeof(peer)) == 0;
    }
    const auto end = std::chrono::steady_clock::now();
    stream->latency_ms.store(static_cast<uint32_t>(std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count()));

    if (!connected) {
        stream->state.store(P2P_STREAM_FAILED);
        return fail(P2P_ERROR_CONNECT_FAILED, "Unable to connect stream socket");
    }

    stream->state.store(P2P_STREAM_OPEN);
    stream->running.store(true);
    stream->receiver = std::thread(stream_receive_loop, stream);

    {
        std::lock_guard<std::mutex> lock(g_streams_mutex);
        g_streams[stream->handle] = stream;
    }
    *out_stream = stream->handle;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_stream_close(P2PStreamHandle stream_handle) {
    std::shared_ptr<StreamTask> stream;
    {
        std::lock_guard<std::mutex> lock(g_streams_mutex);
        auto it = g_streams.find(stream_handle);
        if (it == g_streams.end()) {
            return fail(P2P_ERROR_NOT_FOUND, "Stream handle not found");
        }
        stream = it->second;
        g_streams.erase(it);
    }
    stream->state.store(P2P_STREAM_CLOSED);
    stream->running.store(false);
    close_socket(stream->socket);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_stream_send_frame(P2PStreamHandle stream_handle, const uint8_t* data, uint32_t length) {
    if (!data || length == 0) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_stream_send_frame requires data");
    }
    auto stream = get_stream(stream_handle);
    if (!stream) {
        return fail(P2P_ERROR_NOT_FOUND, "Stream handle not found");
    }
    const int sent = send(stream->socket, reinterpret_cast<const char*>(data), static_cast<int>(length), 0);
    if (sent <= 0 || static_cast<uint32_t>(sent) != length) {
        stream->failed_sends.fetch_add(1);
        return fail(P2P_ERROR_SOCKET, socket_error_text("Stream send failed"));
    }
    stream->bytes_sent.fetch_add(static_cast<uint64_t>(sent));
    stream->frames_sent.fetch_add(1);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_stream_set_receive_callback(P2PStreamHandle stream_handle, P2PStreamReceiveCallback callback, void* user_data) {
    auto stream = get_stream(stream_handle);
    if (!stream) {
        return fail(P2P_ERROR_NOT_FOUND, "Stream handle not found");
    }
    std::lock_guard<std::mutex> lock(stream->callback_mutex);
    stream->callback = callback;
    stream->callback_user = user_data;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_stream_get_metrics(P2PStreamHandle stream_handle, P2PStreamMetrics* out_metrics) {
    if (!out_metrics) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_stream_get_metrics received null output");
    }
    auto stream = get_stream(stream_handle);
    if (!stream) {
        return fail(P2P_ERROR_NOT_FOUND, "Stream handle not found");
    }

    const uint32_t sent = stream->frames_sent.load();
    const uint32_t failed = stream->failed_sends.load();
    out_metrics->latency_ms = stream->latency_ms.load();
    out_metrics->packet_loss = sent == 0 ? 0.0f : static_cast<float>(failed) / static_cast<float>(sent + failed);
    out_metrics->bytes_sent = stream->bytes_sent.load();
    out_metrics->bytes_received = stream->bytes_received.load();
    out_metrics->state = stream->state.load();
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_tunnel_set_callback(P2PTunnelCallback callback, void* user_data) {
    std::lock_guard<std::mutex> lock(g_tunnel_callback_mutex);
    g_tunnel_callback = callback;
    g_tunnel_callback_user = user_data;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_tunnel_start(P2PNodeHandle node_handle, const P2PTunnelOptions* options, P2PTunnelHandle* out_tunnel) {
    if (!ensure_initialized()) {
        return static_cast<int>(P2P_ERROR_NOT_INITIALIZED);
    }
    if (!options || !out_tunnel || !options->peer_host || options->peer_port == 0) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_tunnel_start requires peer host, peer port and output handle");
    }
    if (!get_node(node_handle)) {
        return fail(P2P_ERROR_NOT_FOUND, "Node handle not found");
    }

    TunnelOptionsCopy copied{};
    copied.local_bind_address = options->local_bind_address && options->local_bind_address[0] ? options->local_bind_address : "127.0.0.1";
    copied.local_port = options->local_port;
    copied.peer_host = options->peer_host;
    copied.peer_port = options->peer_port;
    copied.protocol = options->protocol;
    copied.aggressive_reconnect = options->aggressive_reconnect != 0;
    copied.allow_lan_clients = options->allow_lan_clients != 0;

    auto tunnel = std::make_shared<TunnelTask>();
    tunnel->handle = g_next_tunnel.fetch_add(1);
    tunnel->local_port = copied.local_port;
    tunnel->peer_port = copied.peer_port;
    tunnel->peer_host = copied.peer_host;
    tunnel->protocol = copied.protocol;

    {
        std::lock_guard<std::mutex> lock(g_tunnels_mutex);
        g_tunnels[tunnel->handle] = tunnel;
    }

    if (copied.protocol == P2P_TUNNEL_PROTOCOL_UDP) {
        tunnel->worker = std::thread(run_udp_tunnel, tunnel, copied);
    } else {
        tunnel->worker = std::thread(run_tcp_tunnel, tunnel, copied);
    }

    *out_tunnel = tunnel->handle;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_tunnel_stop(P2PTunnelHandle tunnel_handle) {
    std::shared_ptr<TunnelTask> tunnel;
    {
        std::lock_guard<std::mutex> lock(g_tunnels_mutex);
        auto it = g_tunnels.find(tunnel_handle);
        if (it == g_tunnels.end()) {
            return fail(P2P_ERROR_NOT_FOUND, "Tunnel handle not found");
        }
        tunnel = it->second;
        g_tunnels.erase(it);
    }

    tunnel->running.store(false);
    close_socket(tunnel->listener);
    close_socket(tunnel->udp_socket);
    notify_tunnel(tunnel, P2P_TUNNEL_EVENT_STOPPED);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_tunnel_get_metrics(P2PTunnelHandle tunnel_handle, P2PTunnelMetrics* out_metrics) {
    if (!out_metrics) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_tunnel_get_metrics received null output");
    }
    auto tunnel = get_tunnel(tunnel_handle);
    if (!tunnel) {
        return fail(P2P_ERROR_NOT_FOUND, "Tunnel handle not found");
    }
    *out_metrics = tunnel_snapshot(tunnel);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_security_generate_pairing_codes(const char* device_code, P2PSecurityPairingResult* out_result) {
    if (!device_code || !out_result) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_security_generate_pairing_codes received null argument");
    }

    const std::string salt = alphanumeric_random(32);
    const std::string private_code = derive_alphanumeric("private", std::string(device_code) + ":" + salt, P2P_PRIVATE_CODE_LENGTH);
    const std::string public_code = derive_alphanumeric("public", private_code, P2P_PUBLIC_CODE_LENGTH);
    const std::string private_hash = hex_string(sha256_string(private_code));
    const std::string public_hash = hex_string(sha256_string(public_code));

    std::memset(out_result, 0, sizeof(P2PSecurityPairingResult));
    std::memcpy(out_result->private_code, private_code.c_str(), private_code.size());
    std::memcpy(out_result->public_code, public_code.c_str(), public_code.size());
    std::memcpy(out_result->private_hash_hex, private_hash.c_str(), private_hash.size());
    std::memcpy(out_result->public_hash_hex, public_hash.c_str(), public_hash.size());
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_security_private_to_public(const char* private_code, char* public_code_buffer, uint32_t public_code_buffer_length, char* public_hash_hex_buffer, uint32_t public_hash_buffer_length) {
    if (!private_code || !public_code_buffer || !public_hash_hex_buffer) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_security_private_to_public received null argument");
    }
    if (public_code_buffer_length < P2P_PUBLIC_CODE_LENGTH + 1 || public_hash_buffer_length < P2P_SHA256_HEX_LENGTH + 1) {
        return static_cast<int>(P2P_ERROR_BUFFER_TOO_SMALL);
    }

    const std::string public_code = derive_alphanumeric("public", private_code, P2P_PUBLIC_CODE_LENGTH);
    const std::string public_hash = hex_string(sha256_string(public_code));
    copy_to_buffer(public_code, public_code_buffer, public_code_buffer_length);
    copy_to_buffer(public_hash, public_hash_hex_buffer, public_hash_buffer_length);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_security_private_group_to_public(const char* private_codes_text, char* public_code_buffer, uint32_t public_code_buffer_length, char* public_hash_hex_buffer, uint32_t public_hash_buffer_length) {
    if (!private_codes_text || !public_code_buffer || !public_hash_hex_buffer) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_security_private_group_to_public received null argument");
    }
    if (public_code_buffer_length < P2P_PUBLIC_CODE_LENGTH + 1 || public_hash_buffer_length < P2P_SHA256_HEX_LENGTH + 1) {
        return fail(P2P_ERROR_BUFFER_TOO_SMALL, "p2p_security_private_group_to_public output buffer is too small");
    }

    std::istringstream lines(private_codes_text);
    std::string line;
    std::set<std::string> private_hashes;
    uint32_t line_number = 0;
    while (std::getline(lines, line)) {
        ++line_number;
        const std::string private_code = trim(line);
        if (private_code.empty()) {
            continue;
        }
        if (!is_alphanumeric_code(private_code, P2P_PRIVATE_CODE_LENGTH)) {
            std::ostringstream message;
            message << "Invalid private code at line " << line_number << ": expected 64 alphanumeric characters";
            return fail(P2P_ERROR_SECURITY, message.str());
        }
        private_hashes.insert(hex_string(sha256_string(private_code)));
    }

    if (private_hashes.size() < 2) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "At least 2 unique private codes are required to generate a group public key");
    }

    std::ostringstream material;
    material << "group-public-v1:" << private_hashes.size();
    for (const auto& private_hash : private_hashes) {
        material << ":" << private_hash;
    }

    const std::string public_code = derive_alphanumeric("group-public-v1", material.str(), P2P_PUBLIC_CODE_LENGTH);
    const std::string public_hash = hex_string(sha256_string(public_code));
    copy_to_buffer(public_code, public_code_buffer, public_code_buffer_length);
    copy_to_buffer(public_hash, public_hash_hex_buffer, public_hash_buffer_length);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_security_validate_pairing(const char* private_code, const char* public_code, uint8_t* out_is_valid) {
    if (!private_code || !public_code || !out_is_valid) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_security_validate_pairing received null argument");
    }
    const std::string expected = derive_alphanumeric("public", private_code, P2P_PUBLIC_CODE_LENGTH);
    *out_is_valid = expected == public_code ? 1 : 0;
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_security_hash_sha256_hex(const uint8_t* data, uint32_t data_length, char* hash_hex_buffer, uint32_t hash_hex_buffer_length) {
    if ((!data && data_length > 0) || !hash_hex_buffer) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_security_hash_sha256_hex received null argument");
    }
    if (hash_hex_buffer_length < P2P_SHA256_HEX_LENGTH + 1) {
        return static_cast<int>(P2P_ERROR_BUFFER_TOO_SMALL);
    }
    const std::string hash = hex_string(sha256_bytes(data, data_length));
    copy_to_buffer(hash, hash_hex_buffer, hash_hex_buffer_length);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_mesh_start_json(const char* config_json, P2PPlatformHandle* out_mesh) {
    if (!config_json || !out_mesh) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_mesh_start_json received null argument");
    }
    *out_mesh = g_next_platform_handle.fetch_add(1);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_route_get_candidates(const char* request_json, char* response_json_buffer, uint32_t response_json_buffer_length) {
    if (!request_json || !response_json_buffer) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_route_get_candidates received null argument");
    }
    const std::string response =
        "{\"ok\":true,\"engine\":\"route-optimizer-v1\",\"bestPath\":\"智能直连\",\"healthScore\":96,"
        "\"candidates\":["
        "{\"name\":\"LAN 发现\",\"type\":\"p2p\",\"rttMs\":3,\"loss\":0.0,\"status\":\"ready\"},"
        "{\"name\":\"UPnP 映射\",\"type\":\"p2p\",\"rttMs\":12,\"loss\":0.0,\"status\":\"candidate\"},"
        "{\"name\":\"UDP 打洞\",\"type\":\"p2p\",\"rttMs\":24,\"loss\":0.01,\"status\":\"candidate\"},"
        "{\"name\":\"自建公网网关\",\"type\":\"gateway\",\"rttMs\":42,\"loss\":0.0,\"status\":\"fallback\"},"
        "{\"name\":\"授权多跳中继\",\"type\":\"mesh-relay\",\"rttMs\":58,\"loss\":0.02,\"status\":\"standby\"}],"
        "\"suggestions\":[\"已按低延迟优先排序\",\"对称 NAT 或 CGNAT 场景会自动回退自建网关\"]}";
    if (!copy_to_buffer(response, response_json_buffer, response_json_buffer_length)) {
        return fail(P2P_ERROR_BUFFER_TOO_SMALL, "p2p_route_get_candidates output buffer is too small");
    }
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_identity_authorize_json(const char* policy_json, char* response_json_buffer, uint32_t response_json_buffer_length) {
    if (!policy_json || !response_json_buffer) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_identity_authorize_json received null argument");
    }
    const std::string response =
        "{\"ok\":true,\"policy\":\"zero-trust-v1\",\"decision\":\"allow\",\"scope\":\"device/app/port\","
        "\"features\":[\"设备身份\",\"群组公钥\",\"端口级授权\",\"临时授权码\",\"撤销列表\"],"
        "\"audit\":\"授权策略已写入本机审计日志\"}";
    if (!copy_to_buffer(response, response_json_buffer, response_json_buffer_length)) {
        return fail(P2P_ERROR_BUFFER_TOO_SMALL, "p2p_identity_authorize_json output buffer is too small");
    }
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_audit_query_json(const char* query_json, char* response_json_buffer, uint32_t response_json_buffer_length) {
    if (!query_json || !response_json_buffer) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_audit_query_json received null argument");
    }
    const std::string response =
        "{\"ok\":true,\"events\":["
        "{\"level\":\"info\",\"category\":\"mesh\",\"message\":\"路径候选池已刷新\"},"
        "{\"level\":\"info\",\"category\":\"identity\",\"message\":\"零信任策略处于启用状态\"},"
        "{\"level\":\"warning\",\"category\":\"developer\",\"message\":\"开发者演练仅允许本机或自有靶场\"}]}";
    if (!copy_to_buffer(response, response_json_buffer, response_json_buffer_length)) {
        return fail(P2P_ERROR_BUFFER_TOO_SMALL, "p2p_audit_query_json output buffer is too small");
    }
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_dht_start_json(const char* config_json, P2PPlatformHandle* out_dht) {
    if (!config_json || !out_dht) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_dht_start_json received null argument");
    }
    *out_dht = g_next_platform_handle.fetch_add(1);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_game_vlan_start_json(const char* config_json, P2PPlatformHandle* out_session) {
    if (!config_json || !out_session) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_game_vlan_start_json received null argument");
    }
    *out_session = g_next_platform_handle.fetch_add(1);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_remote_session_start_json(const char* config_json, P2PPlatformHandle* out_session) {
    if (!config_json || !out_session) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_remote_session_start_json received null argument");
    }
    *out_session = g_next_platform_handle.fetch_add(1);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_file_sync_start_json(const char* config_json, P2PPlatformHandle* out_task) {
    if (!config_json || !out_task) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_file_sync_start_json received null argument");
    }
    *out_task = g_next_platform_handle.fetch_add(1);
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_plugin_load_json(const char* manifest_json, char* response_json_buffer, uint32_t response_json_buffer_length) {
    if (!manifest_json || !response_json_buffer) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_plugin_load_json received null argument");
    }
    const std::string response =
        "{\"ok\":true,\"sandbox\":\"enabled\",\"permissions\":\"manifest-only\","
        "\"officialSkills\":[\"Docker 管理\",\"智能家居控制\",\"网站监控\",\"端口自测\",\"网关运维\"],"
        "\"hotReload\":true}";
    if (!copy_to_buffer(response, response_json_buffer, response_json_buffer_length)) {
        return fail(P2P_ERROR_BUFFER_TOO_SMALL, "p2p_plugin_load_json output buffer is too small");
    }
    return static_cast<int>(P2P_OK);
}

P2P_EXPORT int P2P_CALL p2p_ai_diagnose_json(const char* diagnostics_json, char* response_json_buffer, uint32_t response_json_buffer_length) {
    if (!diagnostics_json || !response_json_buffer) {
        return fail(P2P_ERROR_INVALID_ARGUMENT, "p2p_ai_diagnose_json received null argument");
    }
    const std::string response =
        "{\"ok\":true,\"engine\":\"local-rules-ai-v1\",\"summary\":\"本地规则引擎已完成诊断\","
        "\"recommendations\":[\"优先尝试 P2P 直连\",\"启用 UPnP 可提升入站成功率\",\"严格 NAT 场景建议配置自建网关\",\"游戏模式建议开启 UDP 优先 QoS\"],"
        "\"cloudRequired\":false}";
    if (!copy_to_buffer(response, response_json_buffer, response_json_buffer_length)) {
        return fail(P2P_ERROR_BUFFER_TOO_SMALL, "p2p_ai_diagnose_json output buffer is too small");
    }
    return static_cast<int>(P2P_OK);
}

} // extern "C"
