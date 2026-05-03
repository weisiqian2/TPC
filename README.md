# TPC

<p align="center">
  <img src="design/favicon.ico" width="88" height="88" alt="TPC icon" />
</p>

<p align="center">
  <strong>Remote LAN for friends, Minecraft, files, and desktop access.</strong><br />
  <strong>面向朋友联机、Minecraft、文件传输和远程桌面的远程局域网工具。</strong>
</p>

<p align="center">
  <a href="#中文说明">中文</a> · <a href="#english-guide">English</a>
</p>

---

## 中文说明

我是一名12岁小学生，这是我做的第一个项目，请大家多多支持。如果有问题请在issues或我的邮箱跟我讲，我会尽快修复（联系邮箱：wcgwsqll@gmail.com）

TPC 是一个 Windows 优先的远程局域网和朋友联机工具。它的目标很简单：新手只需要启动内核、创建房间、把房间码发给朋友，朋友输入房间码和密码后就能开始连接。高级用户仍然可以打开高级模式，配置公网 IPv4、端口转发、自建网关、DHT、自建节点和开发者工具。

TPC 的原则是：完全免费、不内置官方公共中继、不把用户流量默认交给第三方服务器。跨网络连接失败时，软件会优先尝试直连；直连不成功时，会引导用户使用公网 IPv4、路由器端口转发或自己的自建节点/网关兜底。

> 说明：纯直连不可能在所有网络环境下 100% 成功。双方都在 CGNAT、对称 NAT、校园网、公司网或运营商封锁入站端口时，必须使用公网入口、端口转发、自建节点或自建网关。

### 主要功能

- 新手首页：`一键启动内核`、`创建房间`、`加入房间`、连接列表。
- 简化导航：`首页 / 设备 / 文件 / 桌面 / 游戏 / 设置`。
- 多连接支持：同一个人可以加入多条连接，每条连接独立保存，不覆盖旧记录。
- 连接操作：启动、停止、复制给朋友、查看详情、删除本机记录。
- Minecraft 远程局域网：
  - Java 版默认使用 `TCP 25565`。
  - Bedrock 基岩版默认使用 `UDP 19132`。
- 公网 IPv4 直连：
  - 自动检测本机网卡公网 IPv4。
  - 支持手动填写公网 IPv4。
  - 支持 UPnP 自动端口映射尝试。
  - 失败时给出防火墙、端口转发、自建节点等下一步建议。
- 自建网关：用于复杂 NAT、CGNAT、校园网、公司网等场景的稳定兜底。
- 后台 Agent：负责 Profile 保存、启动、停止、metrics 和诊断。
- 设备页：显示已连接设备列表，而不是抽象拓扑图。
- 高级模式：隐藏复杂参数，新手默认不用看到 NAT、打洞、DHT、抓包等内容。
- 图标：`design/favicon.ico` 已用于程序窗口、托盘、快捷方式和安装包。

### 当前状态

当前 Windows 版本已经可以构建、发布并生成 Inno Setup 安装包。发布目录应包含这些核心文件：

```text
TPC.exe
TPC.Agent.exe
p2p_core.dll
tpcwei_gateway.exe
tpcwei_meshd.exe
favicon.ico
```

其中：

- `TPC.exe` 是 WinUI 主程序。
- `TPC.Agent.exe` 是后台 Agent。
- `p2p_core.dll` 是 native core。
- `tpcwei_gateway.exe` 是自建网关组件。
- `tpcwei_meshd.exe` 是 mesh/DHT sidecar。

> `tpcwei_gateway.exe` 和 `tpcwei_meshd.exe` 目前保留兼容文件名，避免破坏已有启动链路。用户可见的软件名和安装包名已经是 TPC。

### 快速开始

#### 房主

1. 打开 TPC。
2. 点击 `一键启动内核`。
3. 点击 `创建房间`。
4. 用途选择 Minecraft，普通玩家保持默认即可。
5. 设置房间名和房间密码。
6. 创建成功后，把房间码和房间密码发给朋友。
7. 如果你有公网 IPv4，请确认：
   - Minecraft Java 放行 `TCP 25565`。
   - Minecraft Bedrock 放行 `UDP 19132`。
   - Windows 防火墙允许 TPC 和 Minecraft 通信。

#### 朋友

1. 打开 TPC。
2. 点击 `一键启动内核`。
3. 点击 `加入房间`。
4. 输入房主发来的房间码和房间密码。
5. 点击 `加入`。
6. 在连接列表里启动这条连接。
7. 打开 Minecraft，多人游戏中优先查看是否出现远程局域网房间；如果没有出现，按 TPC 界面提示使用本地连接地址。

### 公网 IPv4 怎么用

如果房主电脑本身拥有公网 IPv4：

1. 打开 `设置`。
2. 进入 `网络`。
3. 保持 `优先公网 IPv4 直连` 开启。
4. 点击 `自动检测公网 IPv4`。
5. 创建房间时，TPC 会优先把公网 IPv4 和端口写入连接信息。

如果公网 IPv4 在路由器或光猫上，而不是电脑网卡上：

1. 在 TPC 设置里手动填写公网 IPv4。
2. 在路由器里做端口转发：
   - Minecraft Java：把 `TCP 25565` 转发到房主电脑。
   - Minecraft Bedrock：把 `UDP 19132` 转发到房主电脑。
3. Windows 防火墙放行对应程序和端口。
4. 再创建房间并分享给朋友。

如果仍然连不上，按这个顺序排查：

1. 同一 Wi-Fi 下先测试。
2. 确认房主是否真的有公网 IPv4。
3. 检查 Windows 防火墙。
4. 检查路由器端口转发。
5. 使用自建节点或自建网关兜底。

### 高级模式

TPC 默认是新手模式。新手不会看到 NAT、打洞、网关令牌、DHT、Bootstrap、抓包等复杂参数。

打开方式：

1. 进入 `设置`。
2. 找到 `高级模式`。
3. 点击 `打开高级模式`。

高级模式会显示：

- DHT Bootstrap 节点配置。
- 自建节点状态。
- 离线消息容量和保留时间。
- 网关、自建节点、FRP 导入和调试操作。
- 开发者工具箱。

### 安装包

普通用户建议从 GitHub Releases 下载最新的 `TPCSetup.exe`。

开发者本地构建时，Windows 安装包由 Inno Setup 生成，默认输出到：

```text
publish/TPCSetup.exe
```

安装包特性：

- 请求管理员权限。
- 支持自定义安装路径。
- 可创建桌面快捷方式。
- 可选择安装后台 Agent 为 Windows Service。
- 使用 `design/favicon.ico` 作为安装包、快捷方式、窗口和托盘图标。
- 如果没有虚拟网卡驱动，安装不会失败；Minecraft 专用远程局域网模式仍可用。

### 从源码构建

#### Windows

推荐环境：

- Windows 10 19041+ 或 Windows 11。
- Visual Studio Build Tools，并安装“使用 C++ 的桌面开发”。
- .NET 9 SDK。
- Rust stable + Cargo。
- Inno Setup 6。

构建 native core 和网关：

```powershell
cmake -S . -B build
cmake --build build --config Release
```

构建 Rust sidecar：

```powershell
cargo build --manifest-path src/tpcwei_meshd/Cargo.toml --release
```

构建 Agent 和 WinUI：

```powershell
dotnet build src/TPCwei.Agent/TPCwei.Agent.csproj -c Release -r win-x64 --no-restore
dotnet build src/TPCwei.WinUI/TPCwei.WinUI.csproj -c Release -r win-x64 --no-restore
```

发布 WinUI：

```powershell
dotnet publish src/TPCwei.WinUI/TPCwei.WinUI.csproj -c Release -r win-x64 -o publish/winui-win-x64 --self-contained true
```

生成安装包：

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/tpcwei.iss
```

#### Linux / macOS

当前 Linux/macOS 主要用于 native core、gateway 和 meshd 构建，不提供完整桌面安装包。

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
- 需要一个稳定的公网兜底入口。

启动示例：

```powershell
./publish/winui-win-x64/tpcwei_gateway.exe --bind 0.0.0.0 --control-port 7000 --admin-port 7400 --token <change-this-token>
```

健康检查：

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

#### 纯直连能 100% 成功吗？

不能。没有任何软件能在所有 NAT、CGNAT、防火墙和运营商网络限制下保证纯直连 100% 成功。

更可靠的方案是：能直连就直连；直连失败时，使用公网 IPv4、端口转发、自建节点或自建网关。

#### 为什么朋友连接不上？

常见原因：

- 房主没有公网 IPv4。
- 公网 IPv4 在路由器上，但没有做端口转发。
- Windows 防火墙挡住了 TPC 或 Minecraft。
- 运营商、校园网或公司网封锁入站连接。
- 双方都在 CGNAT 后面。

#### 删除连接会删除文件吗？

不会。删除连接只删除本机保存的 Profile 记录，不删除项目文件，也不删除朋友电脑上的配置。

#### 为什么没有官方服务器？

TPC 的原则是完全免费和自建网络，不把用户流量默认导向官方中转。

#### 安全页面为什么没有了？

为了让新手界面更简单，用户可见的安全页面已经移除。连接所需的密钥、公钥和房间凭据仍由软件内部处理。


### 许可证

当前仓库还没有声明许可证。发布到 GitHub 前，请根据你的分发目标补充 `LICENSE` 文件。

---

## English Guide

I am a 12-year-old primary school student. This is my first project. Please give me your support. If you have any questions, feel free to contact me via issues or my email (contact email: wcgwsqll@gmail.com). I will fix them as soon as possible.

TPC is a Windows-first remote LAN tool for friends, Minecraft, file transfer, and desktop access. The goal is simple: beginners should be able to start the core, create a room, share a room code, and let a friend join without learning NAT, hole punching, gateways, or routing details. Advanced users can enable Advanced Mode for public IPv4, port forwarding, self-hosted gateways, DHT, self-hosted nodes, and developer tools.

TPC is free to use, does not include an official public relay by default, and does not route user traffic through a third-party server by default. When cross-network direct connection fails, the app guides users toward public IPv4, router port forwarding, or their own self-hosted node/gateway.

> Note: pure direct connection cannot be guaranteed in every network. If both users are behind CGNAT, symmetric NAT, campus networks, office networks, or carrier firewalls, a reachable public endpoint, port forwarding, self-hosted node, or gateway is required.

### Features

- Beginner home screen: `Start Core`, `Create Room`, `Join Room`, and connection list.
- Simple navigation: `Home / Devices / Files / Desktop / Game / Settings`.
- Multiple connections: one user can join multiple rooms without overwriting previous records.
- Connection actions: start, stop, copy for friend, view details, and delete local record.
- Minecraft remote LAN:
  - Java Edition defaults to `TCP 25565`.
  - Bedrock Edition defaults to `UDP 19132`.
- Public IPv4 direct connect:
  - Detects public IPv4 on local network adapters.
  - Supports manual public IPv4 input.
  - Can try UPnP port mapping.
  - Shows clear next steps when firewall, router forwarding, or a self-hosted node is needed.
- Self-hosted gateway for strict NAT, CGNAT, campus networks, office networks, and stable fallback paths.
- Background Agent for profile save/start/stop, metrics, and diagnostics.
- Devices page shows connected devices instead of an abstract topology diagram.
- Advanced Mode keeps complex NAT, DHT, gateway, packet capture, and debugging options hidden by default.
- `design/favicon.ico` is used for the app window, tray icon, shortcuts, and installer.

### Current Status

The Windows version can be built, published, and packaged with Inno Setup. A published folder should include:

```text
TPC.exe
TPC.Agent.exe
p2p_core.dll
tpcwei_gateway.exe
tpcwei_meshd.exe
favicon.ico
```

File roles:

- `TPC.exe`: WinUI desktop app.
- `TPC.Agent.exe`: background Agent.
- `p2p_core.dll`: native core.
- `tpcwei_gateway.exe`: self-hosted gateway component.
- `tpcwei_meshd.exe`: mesh/DHT sidecar.

> `tpcwei_gateway.exe` and `tpcwei_meshd.exe` currently keep compatibility names to avoid breaking existing startup paths. The visible product name and installer name are TPC.

### Quick Start

#### Host

1. Open TPC.
2. Click `Start Core`.
3. Click `Create Room`.
4. Choose Minecraft. Defaults are fine for most users.
5. Set a room name and room password.
6. Share the room code and room password with your friend.
7. If you have public IPv4, make sure:
   - Minecraft Java allows `TCP 25565`.
   - Minecraft Bedrock allows `UDP 19132`.
   - Windows Firewall allows TPC and Minecraft.

#### Friend

1. Open TPC.
2. Click `Start Core`.
3. Click `Join Room`.
4. Enter the room code and room password.
5. Click `Join`.
6. Start the connection from the connection list.
7. Open Minecraft. The remote LAN room should appear when possible. If it does not, use the local connection address shown by TPC.

### Public IPv4

If the host PC itself has a public IPv4 address:

1. Open `设置`.
2. Go to `Network`.
3. Keep `Prefer Public IPv4 Direct` enabled.
4. Click `Detect Public IPv4`.
5. When creating a room, TPC will write the public IPv4 and port into the connection info.

If the public IPv4 is on the router or modem instead of the PC:

1. Enter the public IPv4 manually in TPC settings.
2. Forward the port on the router:
   - Minecraft Java: forward `TCP 25565` to the host PC.
   - Minecraft Bedrock: forward `UDP 19132` to the host PC.
3. Allow the app and port in Windows Firewall.
4. Create the room and share it with your friend.

If connection still fails, check in this order:

1. Test on the same Wi-Fi first.
2. Confirm whether the host really has public IPv4.
3. Check Windows Firewall.
4. Check router port forwarding.
5. Use a self-hosted node or gateway.

### Advanced Mode

TPC starts in beginner mode. Beginners do not see NAT, hole punching, gateway tokens, DHT, Bootstrap nodes, packet capture, or similar options.

To enable Advanced Mode:

1. Open `设置`.
2. Find `Advanced Mode`.
3. Click `Enable Advanced Mode`.

Advanced Mode includes:

- DHT Bootstrap node configuration.
- Self-hosted node status.
- Offline message retention and capacity.
- Gateway, self-hosted node, FRP import, and debug actions.
- Developer toolbox.

### Installer

End users should download the latest `TPCSetup.exe` from GitHub Releases.

For local developer builds, the Windows installer is generated by Inno Setup:

```text
publish/TPCSetup.exe
```

Installer behavior:

- Requests administrator privileges.
- Supports custom installation path.
- Can create a desktop shortcut.
- Can install the background Agent as a Windows Service.
- Uses `design/favicon.ico` for installer, shortcuts, window, tray, and exe icon.
- Missing virtual network adapter files do not block installation. Minecraft-specific remote LAN mode still works.

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
cmake -S . -B build
cmake --build build --config Release
```

Build Rust sidecar:

```powershell
cargo build --manifest-path src/tpcwei_meshd/Cargo.toml --release
```

Build Agent and WinUI:

```powershell
dotnet build src/TPCwei.Agent/TPCwei.Agent.csproj -c Release -r win-x64 --no-restore
dotnet build src/TPCwei.WinUI/TPCwei.WinUI.csproj -c Release -r win-x64 --no-restore
```

Publish WinUI:

```powershell
dotnet publish src/TPCwei.WinUI/TPCwei.WinUI.csproj -c Release -r win-x64 -o publish/winui-win-x64 --self-contained true
```

Build installer:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/tpcwei.iss
```

#### Linux / macOS

Linux/macOS currently focus on native core, gateway, and meshd builds. They are not part of the complete desktop installer release yet.

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
./publish/winui-win-x64/tpcwei_gateway.exe --bind 0.0.0.0 --control-port 7000 --admin-port 7400 --token <change-this-token>
```

Health checks:

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

#### Can pure direct connection be 100% successful?

No. No software can guarantee pure direct connection in every NAT, CGNAT, firewall, and carrier network environment.

The practical approach is: direct when possible; otherwise use the user's own public IPv4, port forwarding, self-hosted node, or gateway.

#### Why can my friend still not connect?

Common causes:

- The host does not have a public IPv4 address.
- The public IPv4 is on the router, but port forwarding is not configured.
- Windows Firewall blocks TPC or Minecraft.
- The ISP, campus network, or office network blocks inbound connections.
- Both users are behind CGNAT.

#### Does deleting a connection delete files?

No. It only deletes the local Profile record. It does not delete project files or your friend's configuration.

#### Why is there no official server?

TPC is designed around free use and self-hosted networking. User traffic is not routed through an official relay by default.

#### Where is the Plugin Marketplace?

The Plugin Marketplace has been removed from the product UI and is not a user-visible feature in this version.

### License

This repository does not currently declare a license. Before publishing to GitHub, add a `LICENSE` file that matches your distribution plan.
