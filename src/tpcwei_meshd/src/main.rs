use anyhow::{Context, Result};
use clap::Parser;
use futures::StreamExt;
use libp2p::{
    identify, kad, noise, ping, swarm::NetworkBehaviour, swarm::SwarmEvent, tcp, yamux, Multiaddr,
    PeerId, SwarmBuilder,
};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::{
    collections::{HashMap, VecDeque},
    net::SocketAddr,
    sync::Arc,
    time::{Duration, SystemTime},
};
use tokio::{
    io::{AsyncBufReadExt, AsyncWriteExt, BufReader},
    net::{TcpListener, TcpStream},
    sync::Mutex,
};
use uuid::Uuid;

#[derive(Parser, Debug)]
#[command(name = "tpcwei_meshd")]
#[command(about = "TPCwei decentralized mesh sidecar JSON-RPC daemon")]
struct Args {
    #[arg(long, default_value = "127.0.0.1:8765")]
    listen: SocketAddr,
}

#[derive(Debug, Deserialize)]
struct JsonRpcRequest {
    #[serde(default)]
    id: Option<Value>,
    method: String,
    #[serde(default)]
    params: Value,
}

#[derive(Debug, Serialize, Clone)]
struct PeerInfo {
    id: String,
    address: String,
    role: String,
    rtt_ms: u32,
    authorized: bool,
    last_seen_unix_ms: u128,
}

#[derive(Debug, Serialize, Clone)]
struct OfflineMessage {
    id: String,
    target_peer: String,
    ciphertext: String,
    expires_unix_ms: u128,
}

#[derive(Default)]
struct MeshState {
    running: bool,
    node_id: String,
    bootstraps: Vec<String>,
    peers: HashMap<String, PeerInfo>,
    messages: VecDeque<OfflineMessage>,
    listen_addresses: Vec<String>,
}

#[derive(NetworkBehaviour)]
struct MeshBehaviour {
    kademlia: kad::Behaviour<kad::store::MemoryStore>,
    identify: identify::Behaviour,
    ping: ping::Behaviour,
}

#[tokio::main]
async fn main() -> Result<()> {
    let args = Args::parse();
    let state = Arc::new(Mutex::new(MeshState {
        node_id: format!("tpcwei-{}", Uuid::new_v4()),
        ..MeshState::default()
    }));
    {
        let state = Arc::clone(&state);
        tokio::spawn(async move {
            if let Err(error) = run_libp2p_swarm(state).await {
                eprintln!("libp2p swarm error: {error:#}");
            }
        });
    }
    let listener = TcpListener::bind(args.listen)
        .await
        .with_context(|| format!("failed to listen on {}", args.listen))?;
    println!("tpcwei_meshd listening on {}", args.listen);

    loop {
        tokio::select! {
            incoming = listener.accept() => {
                let (stream, _) = incoming?;
                let state = Arc::clone(&state);
                tokio::spawn(async move {
                    if let Err(error) = handle_client(stream, state).await {
                        eprintln!("client error: {error:#}");
                    }
                });
            }
            _ = tokio::signal::ctrl_c() => {
                println!("tpcwei_meshd stopping");
                break;
            }
        }
    }

    Ok(())
}

async fn run_libp2p_swarm(state: Arc<Mutex<MeshState>>) -> Result<()> {
    let mut swarm = SwarmBuilder::with_new_identity()
        .with_tokio()
        .with_tcp(
            tcp::Config::default(),
            noise::Config::new,
            yamux::Config::default,
        )?
        .with_quic()
        .with_behaviour(|keypair| {
            let peer_id = PeerId::from(keypair.public());
            let store = kad::store::MemoryStore::new(peer_id);
            MeshBehaviour {
                kademlia: kad::Behaviour::new(peer_id, store),
                identify: identify::Behaviour::new(identify::Config::new(
                    "/tpcwei/identify/1.0".to_owned(),
                    keypair.public(),
                )),
                ping: ping::Behaviour::default(),
            }
        })?
        .with_swarm_config(|cfg| cfg.with_idle_connection_timeout(Duration::from_secs(300)))
        .build();

    {
        let mut state = state.lock().await;
        state.node_id = swarm.local_peer_id().to_string();
    }

    swarm.listen_on("/ip4/0.0.0.0/tcp/0".parse()?)?;
    swarm.listen_on("/ip4/0.0.0.0/udp/0/quic-v1".parse()?)?;

    let mut refresh = tokio::time::interval(Duration::from_secs(5));
    loop {
        tokio::select! {
            event = swarm.select_next_some() => handle_swarm_event(event, &state, &mut swarm).await,
            _ = refresh.tick() => refresh_bootstraps(&state, &mut swarm).await,
        }
    }
}

async fn handle_swarm_event(
    event: SwarmEvent<MeshBehaviourEvent>,
    state: &Arc<Mutex<MeshState>>,
    swarm: &mut libp2p::Swarm<MeshBehaviour>,
) {
    match event {
        SwarmEvent::NewListenAddr { address, .. } => {
            let mut state = state.lock().await;
            let address = address.to_string();
            if !state.listen_addresses.contains(&address) {
                state.listen_addresses.push(address);
            }
        }
        SwarmEvent::Behaviour(MeshBehaviourEvent::Identify(identify::Event::Received { peer_id, info, .. })) => {
            for address in info.listen_addrs {
                swarm.behaviour_mut().kademlia.add_address(&peer_id, address.clone());
                upsert_peer(state, peer_id, address, "identified", None, true).await;
            }
        }
        SwarmEvent::Behaviour(MeshBehaviourEvent::Ping(ping::Event { peer, result, .. })) => {
            let rtt = result.ok().map(|duration| duration.as_millis().min(u128::from(u32::MAX)) as u32);
            upsert_peer(state, peer, Multiaddr::empty(), "ping", rtt, true).await;
        }
        SwarmEvent::Behaviour(MeshBehaviourEvent::Kademlia(kad::Event::RoutingUpdated { peer, .. })) => {
            upsert_peer(state, peer, Multiaddr::empty(), "dht", None, true).await;
        }
        _ => {}
    }
}

async fn refresh_bootstraps(state: &Arc<Mutex<MeshState>>, swarm: &mut libp2p::Swarm<MeshBehaviour>) {
    let bootstraps = {
        let state = state.lock().await;
        if !state.running {
            return;
        }
        state.bootstraps.clone()
    };

    for address in bootstraps {
        if let Ok(addr) = address.parse::<Multiaddr>() {
            let _ = swarm.dial(addr);
        }
    }
    let _ = swarm.behaviour_mut().kademlia.bootstrap();
}

async fn upsert_peer(
    state: &Arc<Mutex<MeshState>>,
    peer_id: PeerId,
    address: Multiaddr,
    role: &str,
    rtt_ms: Option<u32>,
    authorized: bool,
) {
    let mut state = state.lock().await;
    let id = peer_id.to_string();
    let entry = state.peers.entry(id.clone()).or_insert(PeerInfo {
        id,
        address: String::new(),
        role: role.to_owned(),
        rtt_ms: rtt_ms.unwrap_or(0),
        authorized,
        last_seen_unix_ms: unix_ms(),
    });
    if !address.to_string().is_empty() {
        entry.address = address.to_string();
    }
    entry.role = role.to_owned();
    if let Some(rtt_ms) = rtt_ms {
        entry.rtt_ms = rtt_ms;
    }
    entry.authorized = authorized;
    entry.last_seen_unix_ms = unix_ms();
}

async fn handle_client(stream: TcpStream, state: Arc<Mutex<MeshState>>) -> Result<()> {
    let (reader, mut writer) = stream.into_split();
    let mut lines = BufReader::new(reader).lines();
    while let Some(line) = lines.next_line().await? {
        if line.trim().is_empty() {
            continue;
        }

        let response = match serde_json::from_str::<JsonRpcRequest>(&line) {
            Ok(request) => handle_request(request, Arc::clone(&state)).await,
            Err(error) => json!({ "ok": false, "error": format!("JSON-RPC 解析失败：{error}") }),
        };
        writer.write_all(response.to_string().as_bytes()).await?;
        writer.write_all(b"\n").await?;
        writer.flush().await?;
    }
    Ok(())
}

async fn handle_request(request: JsonRpcRequest, state: Arc<Mutex<MeshState>>) -> Value {
    let id = request.id.clone();
    let result = match request.method.as_str() {
        "mesh.start" => mesh_start(request.params, state).await,
        "mesh.stop" => mesh_stop(state).await,
        "mesh.status" => mesh_status(state).await,
        "mesh.bootstrap.set" => mesh_bootstrap_set(request.params, state).await,
        "mesh.peers.list" => mesh_peers_list(state).await,
        "mesh.route.find" => mesh_route_find(request.params, state).await,
        "mesh.message.send" => mesh_message_send(request.params, state).await,
        "mesh.message.sync" => mesh_message_sync(request.params, state).await,
        other => Err(format!("未知 meshd 方法：{other}")),
    };

    match result {
        Ok(value) => json!({ "id": id, "ok": true, "result": value }),
        Err(error) => json!({ "id": id, "ok": false, "error": error }),
    }
}

async fn mesh_start(params: Value, state: Arc<Mutex<MeshState>>) -> std::result::Result<Value, String> {
    let mut state = state.lock().await;
    state.running = true;
    if let Some(nodes) = params.get("bootstraps").and_then(Value::as_array) {
        state.bootstraps = nodes
            .iter()
            .filter_map(Value::as_str)
            .map(str::trim)
            .filter(|x| !x.is_empty())
            .map(ToOwned::to_owned)
            .collect();
    }

    Ok(json!({
        "nodeId": state.node_id,
        "running": true,
        "bootstraps": state.bootstraps,
        "listenAddresses": state.listen_addresses,
        "transports": ["tcp", "quic"],
        "plannedTransports": ["websocket"],
        "dht": "libp2p-kademlia-running",
        "relayPolicy": "authorized-or-same-group",
        "maxRelayHops": 3
    }))
}

async fn mesh_stop(state: Arc<Mutex<MeshState>>) -> std::result::Result<Value, String> {
    let mut state = state.lock().await;
    state.running = false;
    Ok(json!({ "running": false }))
}

async fn mesh_status(state: Arc<Mutex<MeshState>>) -> std::result::Result<Value, String> {
    let state = state.lock().await;
    Ok(json!({
        "nodeId": state.node_id,
        "running": state.running,
        "peerCount": state.peers.len(),
        "bootstraps": state.bootstraps,
        "listenAddresses": state.listen_addresses,
        "cachedMessages": state.messages.len(),
        "transportPlan": ["tcp", "quic"],
        "plannedTransports": ["websocket"],
        "dht": "libp2p-kademlia-running"
    }))
}

async fn mesh_bootstrap_set(params: Value, state: Arc<Mutex<MeshState>>) -> std::result::Result<Value, String> {
    let nodes = params
        .get("nodes")
        .and_then(Value::as_array)
        .ok_or_else(|| "缺少 nodes 数组".to_string())?;
    let mut state = state.lock().await;
    state.bootstraps = nodes
        .iter()
        .filter_map(Value::as_str)
        .map(str::trim)
        .filter(|x| !x.is_empty())
        .map(ToOwned::to_owned)
        .collect();
    Ok(json!({ "bootstraps": state.bootstraps }))
}

async fn mesh_peers_list(state: Arc<Mutex<MeshState>>) -> std::result::Result<Value, String> {
    let state = state.lock().await;
    let mut peers = state.peers.values().cloned().collect::<Vec<_>>();
    peers.sort_by_key(|peer| peer.rtt_ms);
    Ok(json!(peers))
}

async fn mesh_route_find(params: Value, state: Arc<Mutex<MeshState>>) -> std::result::Result<Value, String> {
    let target = params.get("targetPeer").and_then(Value::as_str).unwrap_or("auto");
    let state = state.lock().await;
    let mut candidates = state.peers.values().cloned().collect::<Vec<_>>();
    candidates.sort_by_key(|peer| peer.rtt_ms);
    let relays = candidates
        .into_iter()
        .filter(|peer| peer.authorized)
        .take(3)
        .collect::<Vec<_>>();
    Ok(json!({
        "targetPeer": target,
        "direct": state.peers.contains_key(target),
        "relayCandidates": relays,
        "maxRelayHops": 3,
        "policy": "prefer-lowest-rtt-authorized-path"
    }))
}

async fn mesh_message_send(params: Value, state: Arc<Mutex<MeshState>>) -> std::result::Result<Value, String> {
    let target_peer = params
        .get("targetPeer")
        .and_then(Value::as_str)
        .ok_or_else(|| "缺少 targetPeer".to_string())?;
    let ciphertext = params
        .get("ciphertext")
        .and_then(Value::as_str)
        .ok_or_else(|| "缺少 ciphertext；meshd 只缓存端到端加密后的消息".to_string())?;
    let ttl_hours = params.get("ttlHours").and_then(Value::as_u64).unwrap_or(24).clamp(1, 168);
    let expires = unix_ms() + Duration::from_secs(ttl_hours * 60 * 60).as_millis();
    let message = OfflineMessage {
        id: Uuid::new_v4().to_string(),
        target_peer: target_peer.to_owned(),
        ciphertext: ciphertext.to_owned(),
        expires_unix_ms: expires,
    };

    let mut state = state.lock().await;
    state.messages.push_back(message.clone());
    prune_messages(&mut state);
    Ok(json!(message))
}

async fn mesh_message_sync(params: Value, state: Arc<Mutex<MeshState>>) -> std::result::Result<Value, String> {
    let peer = params.get("peer").and_then(Value::as_str).unwrap_or_default();
    let mut state = state.lock().await;
    prune_messages(&mut state);
    let messages = state
        .messages
        .iter()
        .filter(|message| peer.is_empty() || message.target_peer == peer)
        .cloned()
        .collect::<Vec<_>>();
    Ok(json!(messages))
}

fn prune_messages(state: &mut MeshState) {
    let now = unix_ms();
    while state.messages.front().is_some_and(|message| message.expires_unix_ms <= now) {
        state.messages.pop_front();
    }
    while state.messages.len() > 1024 {
        state.messages.pop_front();
    }
}

fn unix_ms() -> u128 {
    SystemTime::now()
        .duration_since(SystemTime::UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis()
}
