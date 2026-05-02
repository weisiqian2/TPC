# TPC

> 中文说明在前，English guide follows below.
>
> Chinese first, English below.

## 中文说明

TPC 是一个面向 Windows 的远程局域网和朋友联机工具。它的目标很直接：让新手用尽量少的按钮完成“创建房间、发给朋友、朋友加入、开始联机”，同时给高级用户保留公网 IPv4、UPnP、自建节点、网关、DHT、文件任务和开发者工具。

当前产品原则：

- 完全免费，不做订阅、授权收费或付费共享。
- 不依赖官方公共服务器；跨网络失败时优先提示公网 IPv4、端口转发或自建节点。
- 默认新手模式，复杂参数收进设置里的高级模式。
- 用户可见的插件市场和安全页面已移除；连接所需的密钥、公钥和房间凭据仍由软件内部生成。
- Windows 是当前完整发布目标；Linux/macOS 目前以 native core、gateway、meshd 构建说明为主。

### 当前状态

已经接入并验证的能力：

- WinUI 3 桌面主界面，默认 6 个入口：`首页 / 设备 / 文件 / 桌面 / 游戏 / 设置`。
- 首页新手流程：`一键启动内核`、`创建房间`、`加入房间`、连接列表。
- 加入页精简为：`房间码`、`房间密码`、`完整连接信息（高级可选）`、`粘贴`、`加入`。
- 每次加入都会生成独立连接记录，一个人可以加入多条连接，不覆盖旧连接。
- 每条连接可启动、停止、复制、查看详情、删除本机记录。
- Minecraft 远程局域网优先适配：
  - Java 版默认 `TCP 25565`。
  - 基岩版默认 `UDP 19132`。
- 公网 IPv4 直连适配：
  - 可自动检测本机网卡公网 IPv4。
  - 可手动填写公网 IPv4。
  - 可尝试 UPnP 自动端口映射。
  - 不能确认公网入口时，会明确提示检查 Windows 防火墙、路由器端口转发或自建节点。
- 自建网关 `tpcwei_gateway.exe`：用于 TCP/UDP 映射和公网兜底。
- 后台 Agent `TPC.Agent.exe`：用于 Profile 保存、启动、停止、metrics 和诊断。
- Rust sidecar `tpcwei_meshd.exe`：用于本机 mesh/DHT 能力，默认不内置公共 Bootstrap 节点。
- Inno Setup 安装包：`F:\.TPCwei\publish\TPCSetup.exe`。
- 软件图标：`design/favicon.ico` 已用于 exe、窗口、托盘、快捷方式和安装包。

需要说明的限制：

- 纯直连无法保证 100% 成功。双方都在 CGNAT、对称 NAT、校园网或运营商封锁入站端口时，必须使用可达的公网 IPv4、端口转发、自建节点或自建网关。
- 如果公网 IPv4 在路由器/光猫上，而不是电脑网卡上，软件不能绕过路由器限制，需要 UPnP 成功或手动端口转发。
- 自研完整虚拟网卡局域网仍是后续方向；当前 Minecraft 优先走专用 TCP/UDP 远程局域网流程。
- `tpcwei_meshd` 默认不连接公共网络，Bootstrap 节点需要用户自己配置。

### 新手怎么和朋友联机

#### 房主

1. 打开 TPC。
2. 点击 `一键启动内核`。
3. 点击 `创建房间`。
4. 选择用途，Minecraft 默认即可。
5. 设置房间名和房间密码。
6. 创建后，把房间码和房间密码发给朋友。
7. 如果你有公网 IPv4：
   - Java 版 Minecraft 请放行 `TCP 25565`。
   - 基岩版 Minecraft 请放行 `UDP 19132`。
   - Windows 防火墙也要允许 TPC 和 Minecraft。

#### 朋友

1. 打开 TPC。
2. 点击 `一键启动内核`。
3. 点击 `加入房间`。
4. 输入房主发来的房间码和房间密码。
5. 点击 `加入`。
6. 在连接列表里启动这条连接。
7. 打开 Minecraft，多人游戏里优先看是否自动出现远程局域网；如果没有出现，按界面提示使用本地连接地址。

### 公网 IPv4 怎么用

如果你的电脑本身拥有公网 IPv4，流程最简单：

1. 打开 `设置`。
2. 进入 `网络`。
3. 保持 `优先公网 IPv4 直连` 开启。
4. 点击 `自动检测公网 IPv4`。
5. 创建房间时，TPC 会优先把公网 IPv4 和端口写入房间信息。

如果公网 IPv4 在路由器上：

1. 在设置里手动填写公网 IPv4。
2. 在路由器里转发端口：
   - Minecraft Java：`TCP 25565` 转发到房主电脑。
   - Minecraft Bedrock：`UDP 19132` 转发到房主电脑。
3. Windows 防火墙放行对应程序和端口。
4. 再创建房间并分享给朋友。

如果仍然连不上，界面会提示下一步：

- 检查 Windows 防火墙。
- 检查路由器端口转发。
- 启动自建节点或自建网关。

### 高级模式

默认情况下，新手不会看到 NAT、打洞、网关令牌、DHT、Bootstrap、抓包等复杂参数。

需要时可打开：

1. 进入 `设置`。
2. 找到 `高级模式`。
3. 打开后，连接页、网络设置和开发者入口会显示更多选项。

高级模式包含：

- DHT Bootstrap 节点配置。
- 自建节点状态。
- 离线消息容量和保留时间。
- 网关、自建节点、FRP 导入和调试操作。
- 开发者工具箱。

### 安装包

当前 Windows 安装包路径：

```text
F:\.TPCwei\publish\TPCSetup.exe
```

安装包特性：

- 需要管理员权限。
- 支持安装到自定义路径。
- 可创建桌面快捷方式。
- 可选择安装后台 Agent 为 Windows Service。
- 安装包、快捷方式、程序窗口和托盘图标使用 `design/favicon.ico`。
- 如果没有虚拟网卡驱动，安装不会失败；Minecraft 专用远程局域网仍可使用。

### 从源码构建

#### Windows

推荐环境：

- Windows 10 19041+ 或 Windows 11。
- Visual Studio Build Tools，启用“使用 C++ 的桌面开发”。
- .NET 9 SDK。
- Rust stable + Cargo。
- Inno Setup 6。

构建 native core 和网关：

```powershell
cmake -S F:\.TPCwei -B F:\.TPCwei\build
cmake --build F:\.TPCwei\build --config Release
```

构建 Rust sidecar：

```powershell
cargo build --manifest-path F:\.TPCwei\src\tpcwei_meshd\Cargo.toml --release
```

构建 WinUI 和 Agent：

```powershell
dotnet build F:\.TPCwei\src\TPCwei.Agent\TPCwei.Agent.csproj -c Release -r win-x64 --no-restore
dotnet build F:\.TPCwei\src\TPCwei.WinUI\TPCwei.WinUI.csproj -c Release -r win-x64 --no-restore
```

发布 WinUI：

```powershell
dotnet publish F:\.TPCwei\src\TPCwei.WinUI\TPCwei.WinUI.csproj -c Release -r win-x64 -o F:\.TPCwei\publish\winui-win-x64 --self-contained true
```

生成安装包：

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" F:\.TPCwei\installer\tpcwei.iss
```

发布目录应包含：

```text
TPC.exe
TPC.Agent.exe
p2p_core.dll
tpcwei_gateway.exe
tpcwei_meshd.exe
favicon.ico
```

#### Linux/macOS

当前 Linux/macOS 主要用于 native core、gateway、meshd，不提供本轮完整桌面安装包。

```bash
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build
cargo build --manifest-path src/tpcwei_meshd/Cargo.toml --release
```

### 自建网关

自建网关适合这些情况：

- 双方都在复杂 NAT 或 CGNAT 后面。
- 路由器不能做端口转发。
- 校园网、公司网或运营商限制入站连接。
- 需要更稳定的公网兜底。

启动示例：

```powershell
.\publish\winui-win-x64\tpcwei_gateway.exe --bind 0.0.0.0 --control-port 7000 --admin-port 7400 --token <请改成强令牌>
```

网关检查：

```powershell
Invoke-WebRequest http://127.0.0.1:7400/health
Invoke-WebRequest http://127.0.0.1:7400/api/status
Invoke-WebRequest http://127.0.0.1:7400/metrics
```

### Agent JSON-RPC

Agent 负责保存和运行连接规则。常用方法：

```text
profile.list
profile.save
profile.delete
profile.start
profile.stop
metrics.snapshot
diagnostics.export
network.publicIpv4.detect
connect.plan
connect.start
connect.status
gateway.status
mesh.start
mesh.stop
mesh.status
mesh.bootstrap.set
mesh.peers.list
mesh.route.find
mesh.message.send
mesh.message.sync
developer.*
```

### 项目结构

```text
.
|-- CMakeLists.txt
|-- cmake/
|-- design/
|   `-- favicon.ico
|-- installer/
|   `-- tpcwei.iss
|-- native/
|   |-- include/
|   `-- src/
|-- publish/
|   |-- TPCSetup.exe
|   `-- winui-win-x64/
`-- src/
    |-- TPCwei.Agent/
    |-- TPCwei.App/
    |-- TPCwei.WinUI/
    `-- tpcwei_meshd/
```

### 常见问题

#### 为什么朋友还是连接不上？

最常见原因：

- 房主没有公网 IPv4。
- 公网 IPv4 在路由器上，但没有做端口转发。
- Windows 防火墙挡住了 TPC 或 Minecraft。
- 运营商、校园网或公司网封锁入站连接。
- 双方都在 CGNAT 后面。

解决顺序：

1. 同一 Wi-Fi 下先测试。
2. 确认房主是否有公网 IPv4。
3. 放行 Windows 防火墙。
4. 转发 Minecraft 端口。
5. 使用自建节点或自建网关兜底。

#### 纯直连能 100% 成功吗？

不能。没有任何软件能在所有 NAT、CGNAT 和防火墙环境下保证纯直连 100% 成功。

接近 100% 的做法是：

- 能直连就直连。
- 不能直连就使用用户自己的公网 IPv4、端口转发、自建节点或自建网关。

#### 为什么没有官方服务器？

TPC 的原则是完全免费和自建网络，不把用户流量默认导向官方中转。

#### 删除连接会删除文件吗？

不会。删除连接只删除本机保存的 Profile 记录，不删除项目文件，也不删除朋友电脑上的配置。

#### 为什么安全页面没有了？

为了让新手界面更简单，用户可见的安全页面已经移除。连接所需的密钥、公钥、房间凭据仍由软件内部处理。

#### 插件市场在哪里？

插件市场已从产品界面移除，不作为当前版本的用户可见功能。

---

## English Guide

TPC is a Windows-first remote LAN and friend-connection tool. The main goal is simple: let a beginner create a room, share it with a friend, let the friend join, and start playing with as few buttons as possible. Advanced users can still use public IPv4, UPnP, self-hosted nodes, gateways, DHT, file tasks, and developer tools.

Product principles:

- Free to use. No subscription, license fee, or paid team sharing.
- No official public relay by default. When cross-network direct connection fails, the app guides users toward public IPv4, port forwarding, self-hosted nodes, or a self-hosted gateway.
- Beginner mode by default. Complex options live behind Advanced Mode in Settings.
- The visible Plugin Marketplace and Security page have been removed. Keys, public identities, and room credentials still exist internally because connections need them.
- Windows is the complete release target for now. Linux/macOS currently focus on native core, gateway, and meshd builds.

### Current Status

Implemented and verified:

- WinUI 3 desktop UI with 6 default entries: `Home / Devices / Files / Desktop / Game / Settings`.
- Beginner home flow: `Start Core`, `Create Room`, `Join Room`, and connection list.
- Simplified join page: `Room Code`, `Room Password`, `Full Connection Info (advanced optional)`, `Paste`, `Join`.
- Joining multiple rooms creates independent connection records. Existing connections are not overwritten.
- Each connection can be started, stopped, copied, inspected, or deleted locally.
- Minecraft remote LAN first:
  - Java Edition defaults to `TCP 25565`.
  - Bedrock Edition defaults to `UDP 19132`.
- Public IPv4 direct-connect support:
  - Detects public IPv4 from local network adapters.
  - Allows manual public IPv4 input.
  - Can try UPnP port mapping.
  - If a public entry cannot be confirmed, the UI asks the user to check Windows Firewall, router port forwarding, or a self-hosted node.
- Self-hosted gateway `tpcwei_gateway.exe` for TCP/UDP mapping and public fallback.
- Background Agent `TPC.Agent.exe` for profile save/start/stop, metrics, and diagnostics.
- Rust sidecar `tpcwei_meshd.exe` for local mesh/DHT features. It does not include public Bootstrap nodes by default.
- Inno Setup installer: `F:\.TPCwei\publish\TPCSetup.exe`.
- App icon: `design/favicon.ico` is used by the exe, window, tray icon, shortcuts, and installer.

Important limitations:

- Pure direct connection cannot be 100% guaranteed. If both users are behind CGNAT, symmetric NAT, campus networks, or carrier firewalls, a reachable public IPv4, port forwarding, self-hosted node, or gateway is required.
- If the public IPv4 is on the router instead of the PC network adapter, TPC cannot bypass the router. UPnP or manual port forwarding is required.
- A full virtual network adapter LAN is still future work. The current version prioritizes Minecraft-specific TCP/UDP remote LAN.
- `tpcwei_meshd` does not join any public network by default. Users must configure their own Bootstrap nodes.

### How To Connect With A Friend

#### Host

1. Open TPCwei.
2. Click `Start Core`.
3. Click `Create Room`.
4. Choose a purpose. Minecraft defaults are fine.
5. Set a room name and room password.
6. Share the room code and room password with your friend.
7. If you have public IPv4:
   - Minecraft Java: allow `TCP 25565`.
   - Minecraft Bedrock: allow `UDP 19132`.
   - Also allow TPC and Minecraft in Windows Firewall.

#### Friend

1. Open TPCwei.
2. Click `Start Core`.
3. Click `Join Room`.
4. Enter the room code and room password.
5. Click `Join`.
6. Start the connection from the connection list.
7. Open Minecraft. The remote LAN room should appear first when possible. If it does not appear, use the local address shown by TPCwei.

### Public IPv4

If the PC itself has a public IPv4 address:

1. Open `Settings`.
2. Go to `Network`.
3. Keep `Prefer Public IPv4 Direct` enabled.
4. Click `Detect Public IPv4`.
5. When creating a room, TPC will write the public IPv4 and port into the room info.

If the public IPv4 is on the router:

1. Enter the public IPv4 manually in Settings.
2. Forward the port on the router:
   - Minecraft Java: forward `TCP 25565` to the host PC.
   - Minecraft Bedrock: forward `UDP 19132` to the host PC.
3. Allow the app and port in Windows Firewall.
4. Create the room and share it.

If connection still fails, the UI will guide you to:

- Check Windows Firewall.
- Check router port forwarding.
- Start a self-hosted node or gateway.

### Advanced Mode

Beginners do not see NAT, hole punching, gateway token, DHT, Bootstrap, packet capture, or similar advanced options.

To enable them:

1. Open `Settings`.
2. Find `Advanced Mode`.
3. Turn it on.

Advanced Mode includes:

- DHT Bootstrap node configuration.
- Self-hosted node status.
- Offline message retention and capacity.
- Gateway, self-hosted node, FRP import, and debug actions.
- Developer toolbox.

### Installer

Current Windows installer:

```text
F:\.TPCwei\publish\TPCSetup.exe
```

Installer behavior:

- Requires administrator privileges.
- Supports custom installation path.
- Can create a desktop shortcut.
- Can install the background Agent as a Windows Service.
- Uses `design/favicon.ico` for installer, shortcuts, window, tray, and exe icon.
- Missing virtual network adapter files do not block installation. Minecraft-specific remote LAN still works.

### Build From Source

#### Windows

Recommended environment:

- Windows 10 19041+ or Windows 11.
- Visual Studio Build Tools with Desktop development with C++.
- .NET 9 SDK.
- Rust stable + Cargo.
- Inno Setup 6.

Build native core and gateway:

```powershell
cmake -S F:\.TPCwei -B F:\.TPCwei\build
cmake --build F:\.TPCwei\build --config Release
```

Build Rust sidecar:

```powershell
cargo build --manifest-path F:\.TPCwei\src\tpcwei_meshd\Cargo.toml --release
```

Build WinUI and Agent:

```powershell
dotnet build F:\.TPCwei\src\TPCwei.Agent\TPCwei.Agent.csproj -c Release -r win-x64 --no-restore
dotnet build F:\.TPCwei\src\TPCwei.WinUI\TPCwei.WinUI.csproj -c Release -r win-x64 --no-restore
```

Publish WinUI:

```powershell
dotnet publish F:\.TPCwei\src\TPCwei.WinUI\TPCwei.WinUI.csproj -c Release -r win-x64 -o F:\.TPCwei\publish\winui-win-x64 --self-contained true
```

Build installer:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" F:\.TPCwei\installer\tpcwei.iss
```

Expected publish files:

```text
TPC.exe
TPC.Agent.exe
p2p_core.dll
tpcwei_gateway.exe
tpcwei_meshd.exe
favicon.ico
```

#### Linux/macOS

Linux/macOS currently focus on native core, gateway, and meshd. They are not part of the full desktop installer release yet.

```bash
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build
cargo build --manifest-path src/tpcwei_meshd/Cargo.toml --release
```

### Self-Hosted Gateway

A self-hosted gateway is useful when:

- Both users are behind strict NAT or CGNAT.
- The router cannot forward ports.
- Campus, office, or carrier networks block inbound traffic.
- A stable public fallback path is needed.

Start example:

```powershell
.\publish\winui-win-x64\tpcwei_gateway.exe --bind 0.0.0.0 --control-port 7000 --admin-port 7400 --token <change-this-token>
```

Gateway checks:

```powershell
Invoke-WebRequest http://127.0.0.1:7400/health
Invoke-WebRequest http://127.0.0.1:7400/api/status
Invoke-WebRequest http://127.0.0.1:7400/metrics
```

### Agent JSON-RPC

The Agent saves and runs connection profiles. Common methods:

```text
profile.list
profile.save
profile.delete
profile.start
profile.stop
metrics.snapshot
diagnostics.export
network.publicIpv4.detect
connect.plan
connect.start
connect.status
gateway.status
mesh.start
mesh.stop
mesh.status
mesh.bootstrap.set
mesh.peers.list
mesh.route.find
mesh.message.send
mesh.message.sync
developer.*
```

### Repository Layout

```text
.
|-- CMakeLists.txt
|-- cmake/
|-- design/
|   `-- favicon.ico
|-- installer/
|   `-- tpcwei.iss
|-- native/
|   |-- include/
|   `-- src/
|-- publish/
|   |-- TPCSetup.exe
|   `-- winui-win-x64/
`-- src/
    |-- TPCwei.Agent/
    |-- TPCwei.App/
    |-- TPCwei.WinUI/
    `-- tpcwei_meshd/
```

### FAQ

#### Why can my friend still not connect?

Common causes:

- The host does not have a public IPv4 address.
- The public IPv4 is on the router, but port forwarding is not configured.
- Windows Firewall blocks TPC or Minecraft.
- The ISP, campus network, or office network blocks inbound connections.
- Both users are behind CGNAT.

Recommended order:

1. Test on the same Wi-Fi first.
2. Confirm whether the host has public IPv4.
3. Allow Windows Firewall.
4. Forward the Minecraft port.
5. Use a self-hosted node or gateway.

#### Can pure direct connection be 100% successful?

No. No software can guarantee pure direct connection in every NAT, CGNAT, and firewall environment.

The practical near-100% approach is:

- Direct when possible.
- Otherwise use the user's own public IPv4, port forwarding, self-hosted node, or gateway.

#### Why is there no official server?

TPC is designed around free use and self-hosted networking. User traffic is not routed through an official relay by default.

#### Does deleting a connection delete files?

No. It only deletes the local Profile record. It does not delete project files or your friend's configuration.

#### Why is the Security page gone?

The visible Security page was removed to keep the beginner UI simple. Keys, public identities, and room credentials are still handled internally.

#### Where is the Plugin Marketplace?

The Plugin Marketplace has been removed from the product UI and is not a user-visible feature in this version.
