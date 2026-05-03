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
#  include <signal.h>
#  include <sys/select.h>
#  include <sys/socket.h>
#  include <unistd.h>
#endif

#include <atomic>
#include <chrono>
#include <cctype>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <fstream>
#include <iostream>
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

struct Options {
    std::string bind_address = "0.0.0.0";
    uint16_t control_port = 7000;
    std::string admin_bind = "127.0.0.1";
    uint16_t admin_port = 7400;
    std::string token = "change-me";
    std::string config_path;
    std::string log_path;
    bool install_service = false;
    bool uninstall_service = false;
};

struct Session;

struct Tunnel {
    uint64_t id = 0;
    std::weak_ptr<Session> session;
    std::string protocol;
    std::string name;
    uint16_t public_port = 0;
    std::atomic_bool running{false};
    socket_t listener = kInvalidSocket;
    std::thread listener_thread;
    std::atomic_uint64_t bytes_up{0};
    std::atomic_uint64_t bytes_down{0};

    ~Tunnel();
};

struct Session {
    socket_t control = kInvalidSocket;
    std::string room;
    std::string public_code;
    std::atomic_bool running{false};
    std::mutex write_mutex;
    std::mutex tunnel_mutex;
    std::map<uint64_t, std::shared_ptr<Tunnel>> tunnels;

    ~Session();
};

struct PendingConnection {
    socket_t public_socket = kInvalidSocket;
    std::shared_ptr<Tunnel> tunnel;
    sockaddr_in udp_remote{};
    std::vector<char> first_packet;
    bool udp = false;
    std::chrono::steady_clock::time_point created = std::chrono::steady_clock::now();
};

std::atomic_bool g_running{true};
std::atomic_uint64_t g_next_connection{1};
std::atomic_uint64_t g_total_public_connections{0};
std::atomic_uint64_t g_total_data_connections{0};
std::mutex g_tunnels_mutex;
std::map<uint64_t, std::shared_ptr<Tunnel>> g_tunnels;
std::mutex g_pending_mutex;
std::map<std::string, PendingConnection> g_pending;
std::mutex g_log_mutex;
std::ofstream g_log_file;

void log_line(const std::string& message) {
    const auto now = std::chrono::system_clock::to_time_t(std::chrono::system_clock::now());
    std::tm tm{};
#if defined(_WIN32)
    localtime_s(&tm, &now);
#else
    localtime_r(&now, &tm);
#endif
    char time_buffer[32]{};
    std::strftime(time_buffer, sizeof(time_buffer), "%Y-%m-%d %H:%M:%S", &tm);

    std::lock_guard<std::mutex> lock(g_log_mutex);
    std::cout << "[" << time_buffer << "] " << message << std::endl;
    if (g_log_file.is_open()) {
        g_log_file << "[" << time_buffer << "] " << message << std::endl;
        g_log_file.flush();
    }
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

void set_reuse_address(socket_t s) {
    int yes = 1;
    setsockopt(s, SOL_SOCKET, SO_REUSEADDR, reinterpret_cast<const char*>(&yes), sizeof(yes));
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

bool bind_ipv4(socket_t s, const std::string& bind_address, uint16_t port) {
    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_port = htons(port);
    if (inet_pton(AF_INET, bind_address.c_str(), &addr.sin_addr) != 1) {
        return false;
    }
    return bind(s, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) == 0;
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

std::string json_escape(const std::string& value) {
    std::string result;
    result.reserve(value.size() + 8);
    for (char ch : value) {
        switch (ch) {
        case '\\': result += "\\\\"; break;
        case '"': result += "\\\""; break;
        case '\n': result += "\\n"; break;
        case '\r': result += "\\r"; break;
        case '\t': result += "\\t"; break;
        default: result.push_back(ch); break;
        }
    }
    return result;
}

std::string key_for(uint64_t tunnel_id, const std::string& connection_id) {
    return std::to_string(tunnel_id) + ":" + connection_id;
}

bool write_control(const std::shared_ptr<Session>& session, const std::string& line) {
    if (!session || !session->running.load()) {
        return false;
    }
    std::lock_guard<std::mutex> lock(session->write_mutex);
    return send_text(session->control, line);
}

void pipe_socket(socket_t source, socket_t target, std::atomic_bool& running, std::atomic_uint64_t& counter) {
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

void bridge_tcp(std::shared_ptr<Tunnel> tunnel, socket_t public_socket, socket_t data_socket) {
    std::atomic_bool bridge_running{true};
    std::thread up([&]() {
        pipe_socket(data_socket, public_socket, bridge_running, tunnel->bytes_up);
    });
    std::thread down([&]() {
        pipe_socket(public_socket, data_socket, bridge_running, tunnel->bytes_down);
    });
    if (up.joinable()) {
        up.join();
    }
    if (down.joinable()) {
        down.join();
    }
    close_socket(public_socket);
    close_socket(data_socket);
}

void bridge_udp_packet(std::shared_ptr<Tunnel> tunnel, socket_t udp_socket, const sockaddr_in& remote, const std::vector<char>& packet, socket_t data_socket) {
    if (!send_frame(data_socket, packet.data(), static_cast<uint32_t>(packet.size()))) {
        close_socket(data_socket);
        return;
    }

    set_receive_timeout(data_socket, 2000);
    std::vector<char> response;
    if (read_frame(data_socket, &response) && !response.empty()) {
        sendto(udp_socket, response.data(), static_cast<int>(response.size()), 0, reinterpret_cast<const sockaddr*>(&remote), sizeof(remote));
        tunnel->bytes_down.fetch_add(static_cast<uint64_t>(response.size()));
    }

    close_socket(data_socket);
}

std::shared_ptr<Tunnel> find_tunnel(uint64_t tunnel_id) {
    std::lock_guard<std::mutex> lock(g_tunnels_mutex);
    auto it = g_tunnels.find(tunnel_id);
    return it == g_tunnels.end() ? nullptr : it->second;
}

std::string gateway_status_json() {
    uint64_t bytes_up = 0;
    uint64_t bytes_down = 0;
    uint32_t running_tunnels = 0;
    {
        std::lock_guard<std::mutex> lock(g_tunnels_mutex);
        for (const auto& pair : g_tunnels) {
            if (pair.second) {
                bytes_up += pair.second->bytes_up.load();
                bytes_down += pair.second->bytes_down.load();
                running_tunnels += pair.second->running.load() ? 1 : 0;
            }
        }
    }

    std::ostringstream json;
    json << "{"
         << "\"name\":\"TPCwei Gateway\","
         << "\"version\":\"2\","
         << "\"running\":true,"
         << "\"runningTunnels\":" << running_tunnels << ","
         << "\"publicConnections\":" << g_total_public_connections.load() << ","
         << "\"dataConnections\":" << g_total_data_connections.load() << ","
         << "\"bytesUp\":" << bytes_up << ","
         << "\"bytesDown\":" << bytes_down
         << "}";
    return json.str();
}

std::string gateway_rules_json() {
    std::ostringstream json;
    json << "[";
    bool first = true;
    {
        std::lock_guard<std::mutex> lock(g_tunnels_mutex);
        for (const auto& pair : g_tunnels) {
            const auto& tunnel = pair.second;
            if (!tunnel) {
                continue;
            }
            if (!first) {
                json << ",";
            }
            first = false;
            json << "{"
                 << "\"id\":" << tunnel->id << ","
                 << "\"name\":\"" << json_escape(tunnel->name) << "\","
                 << "\"protocol\":\"" << json_escape(tunnel->protocol) << "\","
                 << "\"publicPort\":" << tunnel->public_port << ","
                 << "\"running\":" << (tunnel->running.load() ? "true" : "false") << ","
                 << "\"bytesUp\":" << tunnel->bytes_up.load() << ","
                 << "\"bytesDown\":" << tunnel->bytes_down.load()
                 << "}";
        }
    }
    json << "]";
    return json.str();
}

std::string gateway_metrics_text() {
    uint64_t bytes_up = 0;
    uint64_t bytes_down = 0;
    uint32_t tunnel_count = 0;
    {
        std::lock_guard<std::mutex> lock(g_tunnels_mutex);
        tunnel_count = static_cast<uint32_t>(g_tunnels.size());
        for (const auto& pair : g_tunnels) {
            if (pair.second) {
                bytes_up += pair.second->bytes_up.load();
                bytes_down += pair.second->bytes_down.load();
            }
        }
    }

    std::ostringstream metrics;
    metrics << "# HELP tpcwei_gateway_tunnels Current registered tunnels\n"
            << "# TYPE tpcwei_gateway_tunnels gauge\n"
            << "tpcwei_gateway_tunnels " << tunnel_count << "\n"
            << "# HELP tpcwei_gateway_public_connections Total public side connections\n"
            << "# TYPE tpcwei_gateway_public_connections counter\n"
            << "tpcwei_gateway_public_connections " << g_total_public_connections.load() << "\n"
            << "# HELP tpcwei_gateway_data_connections Total data channels\n"
            << "# TYPE tpcwei_gateway_data_connections counter\n"
            << "tpcwei_gateway_data_connections " << g_total_data_connections.load() << "\n"
            << "# HELP tpcwei_gateway_bytes_up Bytes from client/local service to public side\n"
            << "# TYPE tpcwei_gateway_bytes_up counter\n"
            << "tpcwei_gateway_bytes_up " << bytes_up << "\n"
            << "# HELP tpcwei_gateway_bytes_down Bytes from public side to client/local service\n"
            << "# TYPE tpcwei_gateway_bytes_down counter\n"
            << "tpcwei_gateway_bytes_down " << bytes_down << "\n";
    return metrics.str();
}

void send_http(socket_t client, int status, const std::string& content_type, const std::string& body) {
    std::ostringstream response;
    response << "HTTP/1.1 " << status << (status == 200 ? " OK" : " Error") << "\r\n"
             << "Content-Type: " << content_type << "; charset=utf-8\r\n"
             << "Content-Length: " << body.size() << "\r\n"
             << "Connection: close\r\n\r\n"
             << body;
    send_text(client, response.str());
}

void handle_admin_connection(socket_t client) {
    std::string request_line;
    if (!read_line(client, &request_line)) {
        close_socket(client);
        return;
    }

    std::string header;
    while (read_line(client, &header) && !header.empty()) {
    }

    auto words = split_words(request_line);
    const std::string path = words.size() >= 2 ? words[1] : "/";
    if (path == "/metrics") {
        send_http(client, 200, "text/plain", gateway_metrics_text());
    } else if (path == "/api/status" || path == "/") {
        send_http(client, 200, "application/json", gateway_status_json());
    } else if (path == "/api/rules") {
        send_http(client, 200, "application/json", gateway_rules_json());
    } else if (path == "/health") {
        send_http(client, 200, "text/plain", "ok\n");
    } else {
        send_http(client, 404, "application/json", "{\"error\":\"not_found\"}");
    }
    close_socket(client);
}

void admin_loop(Options options) {
    socket_t listener = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listener == kInvalidSocket) {
        log_line("[gateway] create admin listener failed");
        return;
    }

    set_reuse_address(listener);
    if (!bind_ipv4(listener, options.admin_bind, options.admin_port) || listen(listener, 32) != 0) {
        log_line("[gateway] admin bind failed on " + options.admin_bind + ":" + std::to_string(options.admin_port));
        close_socket(listener);
        return;
    }

    log_line("[gateway] admin API listening on http://" + options.admin_bind + ":" + std::to_string(options.admin_port));
    while (g_running.load()) {
        sockaddr_in remote{};
#if defined(_WIN32)
        int remote_len = sizeof(remote);
#else
        socklen_t remote_len = sizeof(remote);
#endif
        socket_t accepted = accept(listener, reinterpret_cast<sockaddr*>(&remote), &remote_len);
        if (accepted == kInvalidSocket) {
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
            continue;
        }
        std::thread(handle_admin_connection, accepted).detach();
    }

    close_socket(listener);
}

void handle_data_connection(socket_t data_socket, const std::vector<std::string>& words, const std::string& expected_token) {
    if (words.size() < 4 || words[1] != expected_token) {
        send_text(data_socket, "ERR auth\n");
        close_socket(data_socket);
        return;
    }

    g_total_data_connections.fetch_add(1);
    const auto tunnel_id = static_cast<uint64_t>(std::strtoull(words[2].c_str(), nullptr, 10));
    const std::string key = key_for(tunnel_id, words[3]);
    PendingConnection pending;
    {
        std::lock_guard<std::mutex> lock(g_pending_mutex);
        auto it = g_pending.find(key);
        if (it == g_pending.end()) {
            send_text(data_socket, "ERR no_pending\n");
            close_socket(data_socket);
            return;
        }
        pending = it->second;
        g_pending.erase(it);
    }

    if (!pending.tunnel || pending.public_socket == kInvalidSocket) {
        close_socket(data_socket);
        return;
    }

    if (pending.udp) {
        std::thread(bridge_udp_packet, pending.tunnel, pending.public_socket, pending.udp_remote, pending.first_packet, data_socket).detach();
        return;
    }

    std::cout << "[gateway] paired TCP connection tunnel=" << tunnel_id << " conn=" << words[3] << std::endl;
    std::thread(bridge_tcp, pending.tunnel, pending.public_socket, data_socket).detach();
}

void tcp_public_listener(std::shared_ptr<Tunnel> tunnel) {
    while (g_running.load() && tunnel->running.load()) {
        sockaddr_in remote{};
#if defined(_WIN32)
        int remote_len = sizeof(remote);
#else
        socklen_t remote_len = sizeof(remote);
#endif
        socket_t accepted = accept(tunnel->listener, reinterpret_cast<sockaddr*>(&remote), &remote_len);
        if (accepted == kInvalidSocket) {
            if (!tunnel->running.load()) {
                break;
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
            continue;
        }

        auto session = tunnel->session.lock();
        if (!session || !session->running.load()) {
            close_socket(accepted);
            continue;
        }

        g_total_public_connections.fetch_add(1);
        const std::string connection_id = std::to_string(g_next_connection.fetch_add(1));
        {
            PendingConnection pending{};
            pending.public_socket = accepted;
            pending.tunnel = tunnel;
            pending.created = std::chrono::steady_clock::now();
            std::lock_guard<std::mutex> lock(g_pending_mutex);
            g_pending[key_for(tunnel->id, connection_id)] = std::move(pending);
        }

        if (!write_control(session, "OPEN " + std::to_string(tunnel->id) + " " + connection_id + "\n")) {
            std::lock_guard<std::mutex> lock(g_pending_mutex);
            auto it = g_pending.find(key_for(tunnel->id, connection_id));
            if (it != g_pending.end()) {
                close_socket(it->second.public_socket);
                g_pending.erase(it);
            }
        }
    }
}

void udp_public_listener(std::shared_ptr<Tunnel> tunnel) {
    std::vector<char> buffer(64 * 1024);
    while (g_running.load() && tunnel->running.load()) {
        sockaddr_in remote{};
#if defined(_WIN32)
        int remote_len = sizeof(remote);
#else
        socklen_t remote_len = sizeof(remote);
#endif
        const int received = recvfrom(tunnel->listener, buffer.data(), static_cast<int>(buffer.size()), 0, reinterpret_cast<sockaddr*>(&remote), &remote_len);
        if (received <= 0) {
            if (!tunnel->running.load()) {
                break;
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
            continue;
        }

        auto session = tunnel->session.lock();
        if (!session || !session->running.load()) {
            continue;
        }

        g_total_public_connections.fetch_add(1);
        const std::string connection_id = std::to_string(g_next_connection.fetch_add(1));
        PendingConnection pending{};
        pending.public_socket = tunnel->listener;
        pending.tunnel = tunnel;
        pending.udp_remote = remote;
        pending.first_packet.assign(buffer.begin(), buffer.begin() + received);
        pending.udp = true;
        pending.created = std::chrono::steady_clock::now();
        {
            std::lock_guard<std::mutex> lock(g_pending_mutex);
            g_pending[key_for(tunnel->id, connection_id)] = std::move(pending);
        }

        tunnel->bytes_up.fetch_add(static_cast<uint64_t>(received));
        if (!write_control(session, "OPEN " + std::to_string(tunnel->id) + " " + connection_id + "\n")) {
            std::lock_guard<std::mutex> lock(g_pending_mutex);
            g_pending.erase(key_for(tunnel->id, connection_id));
        }
    }
}

bool start_tunnel_listener(const std::shared_ptr<Tunnel>& tunnel, const Options& options, std::string* error) {
    const bool udp = tunnel->protocol == "UDP";
    socket_t listener = socket(AF_INET, udp ? SOCK_DGRAM : SOCK_STREAM, udp ? IPPROTO_UDP : IPPROTO_TCP);
    if (listener == kInvalidSocket) {
        if (error) {
            *error = "create listener failed";
        }
        return false;
    }

    set_reuse_address(listener);
    if (!bind_ipv4(listener, options.bind_address, tunnel->public_port) || (!udp && listen(listener, 128) != 0)) {
        close_socket(listener);
        if (error) {
            *error = "bind public port failed";
        }
        return false;
    }

    tunnel->listener = listener;
    tunnel->running.store(true);
    tunnel->listener_thread = udp
        ? std::thread(udp_public_listener, tunnel)
        : std::thread(tcp_public_listener, tunnel);
    return true;
}

void register_tunnel(const std::shared_ptr<Session>& session, const Options& options, const std::vector<std::string>& words) {
    if (words.size() < 7 || words[1] != options.token) {
        write_control(session, "ERR bad_tunnel_request\n");
        return;
    }

    auto tunnel = std::make_shared<Tunnel>();
    tunnel->id = static_cast<uint64_t>(std::strtoull(words[2].c_str(), nullptr, 10));
    tunnel->protocol = words[3];
    tunnel->public_port = static_cast<uint16_t>(std::strtoul(words[4].c_str(), nullptr, 10));
    tunnel->name = words.size() >= 8 ? words[7] : "tunnel";
    tunnel->session = session;

    std::string error;
    if (!start_tunnel_listener(tunnel, options, &error)) {
        write_control(session, "ERR TUNNEL " + std::to_string(tunnel->id) + " " + error + "\n");
        return;
    }

    {
        std::lock_guard<std::mutex> lock(session->tunnel_mutex);
        session->tunnels[tunnel->id] = tunnel;
    }
    {
        std::lock_guard<std::mutex> lock(g_tunnels_mutex);
        g_tunnels[tunnel->id] = tunnel;
    }

    write_control(session, "OK TUNNEL " + std::to_string(tunnel->id) + " " + tunnel->protocol + " " + std::to_string(tunnel->public_port) + "\n");
    log_line("[gateway] tunnel registered id=" + std::to_string(tunnel->id) +
        " protocol=" + tunnel->protocol +
        " public_port=" + std::to_string(tunnel->public_port));
}

void stop_tunnel(const std::shared_ptr<Session>& session, const std::vector<std::string>& words, const Options& options) {
    if (words.size() < 3 || words[1] != options.token) {
        write_control(session, "ERR bad_stop_request\n");
        return;
    }

    const auto tunnel_id = static_cast<uint64_t>(std::strtoull(words[2].c_str(), nullptr, 10));
    std::shared_ptr<Tunnel> tunnel;
    {
        std::lock_guard<std::mutex> lock(session->tunnel_mutex);
        auto it = session->tunnels.find(tunnel_id);
        if (it != session->tunnels.end()) {
            tunnel = it->second;
            session->tunnels.erase(it);
        }
    }
    {
        std::lock_guard<std::mutex> lock(g_tunnels_mutex);
        g_tunnels.erase(tunnel_id);
    }
    if (tunnel) {
        tunnel->running.store(false);
        shutdown_socket(tunnel->listener);
        close_socket(tunnel->listener);
    }
    write_control(session, "OK STOP " + std::to_string(tunnel_id) + "\n");
}

void handle_session(socket_t control, const std::vector<std::string>& hello, Options options) {
    if (hello.size() < 2 || hello[1] != options.token) {
        send_text(control, "ERR auth\n");
        close_socket(control);
        return;
    }

    if (hello[0] == "HELLO2") {
        if (hello.size() < 6) {
            send_text(control, "ERR bad_hello2\n");
            close_socket(control);
            return;
        }
        const auto timestamp = static_cast<int64_t>(std::strtoll(hello[2].c_str(), nullptr, 10));
        const auto now = static_cast<int64_t>(std::time(nullptr));
        if (timestamp <= 0 || std::llabs(now - timestamp) > 300) {
            send_text(control, "ERR stale_hello2\n");
            close_socket(control);
            return;
        }
    }

    auto session = std::make_shared<Session>();
    session->control = control;
    session->room = hello[0] == "HELLO2"
        ? (hello.size() >= 5 ? hello[4] : "-")
        : (hello.size() >= 3 ? hello[2] : "-");
    session->public_code = hello[0] == "HELLO2"
        ? (hello.size() >= 6 ? hello[5] : "-")
        : (hello.size() >= 4 ? hello[3] : "-");
    session->running.store(true);
    write_control(session, hello[0] == "HELLO2" ? "OK HELLO2 TPCweiGateway/2\n" : "OK HELLO TPCweiGateway/1\n");
    log_line("[gateway] client joined room=" + session->room);

    std::string line;
    while (g_running.load() && session->running.load() && read_line(control, &line)) {
        auto words = split_words(line);
        if (words.empty()) {
            continue;
        }
        if (words[0] == "TUNNEL") {
            register_tunnel(session, options, words);
        } else if (words[0] == "STOP") {
            stop_tunnel(session, words, options);
        } else if (words[0] == "PONG") {
            continue;
        } else {
            write_control(session, "ERR unknown_command\n");
        }
    }

    session->running.store(false);
    log_line("[gateway] client disconnected room=" + session->room);
}

void handle_control_connection(socket_t control, Options options) {
    std::string first_line;
    if (!read_line(control, &first_line)) {
        close_socket(control);
        return;
    }

    auto words = split_words(first_line);
    if (words.empty()) {
        close_socket(control);
        return;
    }

    if (words[0] == "HELLO" || words[0] == "HELLO2") {
        handle_session(control, words, options);
    } else if (words[0] == "DATA") {
        handle_data_connection(control, words, options.token);
    } else {
        send_text(control, "ERR unknown_first_command\n");
        close_socket(control);
    }
}

Tunnel::~Tunnel() {
    running.store(false);
    shutdown_socket(listener);
    close_socket(listener);
    listener = kInvalidSocket;
    if (listener_thread.joinable()) {
        listener_thread.join();
    }
}

Session::~Session() {
    running.store(false);
    shutdown_socket(control);
    close_socket(control);
    control = kInvalidSocket;
}

bool text_value(const std::string& text, const std::string& key, std::string* out) {
    const std::string quoted_key = "\"" + key + "\"";
    size_t pos = text.find(quoted_key);
    if (pos == std::string::npos) {
        pos = text.find(key);
    }
    if (pos == std::string::npos) {
        return false;
    }
    pos = text.find_first_of(":=", pos + key.size());
    if (pos == std::string::npos) {
        return false;
    }
    ++pos;
    while (pos < text.size() && std::isspace(static_cast<unsigned char>(text[pos]))) {
        ++pos;
    }
    if (pos < text.size() && text[pos] == '"') {
        const size_t end = text.find('"', pos + 1);
        if (end == std::string::npos) {
            return false;
        }
        if (out) {
            *out = text.substr(pos + 1, end - pos - 1);
        }
        return true;
    }
    const size_t end = text.find_first_of(",\r\n}", pos);
    if (out) {
        *out = text.substr(pos, end == std::string::npos ? std::string::npos : end - pos);
        while (!out->empty() && std::isspace(static_cast<unsigned char>(out->back()))) {
            out->pop_back();
        }
    }
    return true;
}

void apply_config_file(Options* options) {
    if (!options || options->config_path.empty()) {
        return;
    }
    std::ifstream file(options->config_path);
    if (!file) {
        return;
    }
    std::stringstream buffer;
    buffer << file.rdbuf();
    const std::string text = buffer.str();
    std::string value;
    if (text_value(text, "bind", &value) || text_value(text, "bindAddress", &value)) {
        options->bind_address = value;
    }
    if (text_value(text, "adminBind", &value)) {
        options->admin_bind = value;
    }
    if (text_value(text, "token", &value)) {
        options->token = value;
    }
    if (text_value(text, "logPath", &value)) {
        options->log_path = value;
    }
    if (text_value(text, "controlPort", &value)) {
        options->control_port = static_cast<uint16_t>(std::strtoul(value.c_str(), nullptr, 10));
    }
    if (text_value(text, "adminPort", &value)) {
        options->admin_port = static_cast<uint16_t>(std::strtoul(value.c_str(), nullptr, 10));
    }
}

Options parse_args(int argc, char** argv) {
    Options options;
    for (int i = 1; i < argc; ++i) {
        const std::string arg = argv[i];
        if (arg == "--config" && i + 1 < argc) {
            options.config_path = argv[++i];
        }
    }
    apply_config_file(&options);
    for (int i = 1; i < argc; ++i) {
        const std::string arg = argv[i];
        auto next = [&]() -> std::string {
            return i + 1 < argc ? argv[++i] : "";
        };
        if (arg == "--bind") {
            options.bind_address = next();
        } else if (arg == "--control-port") {
            options.control_port = static_cast<uint16_t>(std::strtoul(next().c_str(), nullptr, 10));
        } else if (arg == "--admin-bind") {
            options.admin_bind = next();
        } else if (arg == "--admin-port") {
            options.admin_port = static_cast<uint16_t>(std::strtoul(next().c_str(), nullptr, 10));
        } else if (arg == "--token") {
            options.token = next();
        } else if (arg == "--log") {
            options.log_path = next();
        } else if (arg == "--install-service") {
            options.install_service = true;
        } else if (arg == "--uninstall-service") {
            options.uninstall_service = true;
        } else if (arg == "--help" || arg == "-h") {
            std::cout << "Usage: tpcwei_gateway --bind 0.0.0.0 --control-port 7000 --admin-port 7400 --token <token>\n";
            std::exit(0);
        }
    }
    return options;
}

#if !defined(_WIN32)
void handle_signal(int) {
    g_running.store(false);
}
#endif

} // namespace

int main(int argc, char** argv) {
    const Options options = parse_args(argc, argv);

    if (options.install_service) {
#if defined(_WIN32)
        std::cout << "请以管理员身份执行：sc create TPCweiGateway binPath= \""
                  << argv[0] << " --config " << (options.config_path.empty() ? "gateway.json" : options.config_path)
                  << "\" start= auto\n";
#else
        std::cout << "请使用 README 中的 systemd 部署命令安装服务。\n";
#endif
        return 0;
    }

    if (options.uninstall_service) {
#if defined(_WIN32)
        std::cout << "请以管理员身份执行：sc stop TPCweiGateway & sc delete TPCweiGateway\n";
#else
        std::cout << "请执行：sudo systemctl disable --now tpcwei-gateway && sudo rm /etc/systemd/system/tpcwei-gateway.service\n";
#endif
        return 0;
    }

    if (!options.log_path.empty()) {
        std::lock_guard<std::mutex> lock(g_log_mutex);
        g_log_file.open(options.log_path, std::ios::app);
    }

#if defined(_WIN32)
    WSADATA data{};
    if (WSAStartup(MAKEWORD(2, 2), &data) != 0) {
        std::cerr << "[gateway] WSAStartup failed\n";
        return 1;
    }
#else
    signal(SIGPIPE, SIG_IGN);
    signal(SIGINT, handle_signal);
    signal(SIGTERM, handle_signal);
#endif

    socket_t listener = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listener == kInvalidSocket) {
        std::cerr << "[gateway] create control listener failed\n";
        return 1;
    }

    set_reuse_address(listener);
    if (!bind_ipv4(listener, options.bind_address, options.control_port) || listen(listener, 128) != 0) {
        log_line("[gateway] bind failed on " + options.bind_address + ":" + std::to_string(options.control_port));
        close_socket(listener);
        return 1;
    }

    log_line("[gateway] TPCwei gateway listening on " + options.bind_address + ":" + std::to_string(options.control_port));
    log_line("[gateway] token is required; change the default token before exposing to the internet");
    std::thread admin_thread(admin_loop, options);

    while (g_running.load()) {
        sockaddr_in remote{};
#if defined(_WIN32)
        int remote_len = sizeof(remote);
#else
        socklen_t remote_len = sizeof(remote);
#endif
        socket_t accepted = accept(listener, reinterpret_cast<sockaddr*>(&remote), &remote_len);
        if (accepted == kInvalidSocket) {
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
            continue;
        }
        std::thread(handle_control_connection, accepted, options).detach();
    }

    close_socket(listener);
    if (admin_thread.joinable()) {
        admin_thread.detach();
    }
#if defined(_WIN32)
    WSACleanup();
#endif
    return 0;
}
