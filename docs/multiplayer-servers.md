[← Back to Documentation Index](README.md)

# Multiplayer Servers

CodeDesigner includes a self-contained PS2 online server emulator that reproduces the full **Sony Medius** online infrastructure used by SOCOM II and other PS2 titles. It is built in `CodeDesigner.Ps2.Multiplayer`.

---

## Overview

PS2 online games use a layered protocol stack:

```
Game (PS2)
  │
  ├─ DNAS authentication  → DnsServer (DNS intercept) + DnasServer (HTTPS)
  │
  ├─ MUIS (Universe Info) → MuisServer :10075 TCP
  │     returns MAS address
  ├─ MAS (Authentication) → MasServer :10071 TCP
  │     login / account creation → returns MLS address
  ├─ MLS (Lobby)          → MlsServer :10073 TCP
  │     channels, game rooms, chat → creates DME world
  │
  ├─ DME (In-game relay)  → DmeServer :10070 TCP
  │     real-time packet relay between players
  │
  └─ NAT (Punchthrough)   → NatServer :10069 UDP
        echoes client external IP:port for P2P setup
```

All servers run as a single .NET hosted service. They start concurrently and share an in-memory state store.

---

## Running the server

```powershell
cd CodeDesigner.Ps2.Multiplayer
dotnet run
```

Edit `config.json` before starting to set your LAN/WAN IP:

```json
{
  "Online": {
    "BindAddress": "0.0.0.0",
    "ServerIp": "192.168.1.100"
  }
}
```

`ServerIp` is the IP address the server announces to clients. Set it to your machine's LAN IP so PS2 consoles on the same network can reach the Medius and DME servers.

---

## Configuration reference

All settings live under the `"Online"` key in `config.json`.

### Network

| Setting | Default | Description |
|---|---|---|
| `BindAddress` | `"0.0.0.0"` | IP to bind all listeners (use `"0.0.0.0"` for all interfaces) |
| `ServerIp` | `"127.0.0.1"` | Announced IP returned to clients — set to your LAN IP |

### Medius ports (TCP)

| Setting | Default | Description |
|---|---|---|
| `MuisPort` | `10075` | Universe Information Server — first contact |
| `MasPort` | `10071` | Authentication Server — login / account creation |
| `MlsPort` | `10073` | Lobby Server — channels, game rooms, chat |
| `DmePort` | `10070` | In-game relay server |

### NAT (UDP)

| Setting | Default | Description |
|---|---|---|
| `NatPort` | `10069` | NAT punchthrough helper |

### DNAS

| Setting | Default | Description |
|---|---|---|
| `DnasPort` | `10060` | Plain-HTTP debug fallback (for testing without SSL) |
| `DnasHttpsPort` | `443` | Real DNAS HTTPS listener (requires elevated privileges) |
| `DnsDnasPort` | `53` | DNS intercept for `*.dnas.playstation.org` (requires elevated privileges) |
| `DnasRegion` | `"us-gw"` | Response file region subfolder. Valid: `us-gw`, `eu-gw`, `gai-gw` |
| `DnasCertPath` | `""` | TLS certificate path (defaults to `Certificates/dnas.crt`) |
| `DnasKeyPath` | `""` | TLS private key path (defaults to `Certificates/dnas.key`) |

### Game

| Setting | Default | Description |
|---|---|---|
| `MaxClients` | `1000` | Maximum concurrent connections per server |
| `AllowedAppIds` | `[10683, 10684]` | Accepted game AppIds (`10683` = SOCOM II NTSC-U, `10684` = PAL) |
| `AutoCreateAccounts` | `true` | Auto-create accounts on first login (no separate registration step) |
| `ClientTimeoutSeconds` | `60` | Client idle timeout before disconnection |

---

## Server details

### MUIS — Universe Information Server (port 10075)

The very first server the game contacts. It handles the SCERT handshake, then responds to `GetUniverse` / `GetUniverse_ExtraInfo` requests with the `ServerIp:MasPort` address so the game knows where to authenticate.

### MAS — Authentication Server (port 10071)

Handles account login and registration. Clients connect after MUIS. Flow:

1. **SessionBegin** — assigns a session key
2. **AccountLogin** / **AnonymousLogin** / **TicketLogin** — authenticates the account
3. **AccountRegistration** — creates a new account (when `AutoCreateAccounts` is false)
4. Server returns the MLS IP:port for the client to connect to next

All account state is held in memory (`InMemoryStorage`). Accounts do not persist across restarts.

### MLS — Lobby Server (port 10073)

Clients arrive here after authenticating on MAS. The lobby server handles:

- Channel listing and joining
- Game room creation, listing, and joining
- In-game chat
- Buddy list operations
- Announcements

When a player creates or joins a game, MLS creates a DME world and returns its world ID to the client.

### DME — In-game Relay Server (port 10070)

After MLS creates a DME world, all players in that game connect here. DME relays real-time packets between clients:

- `RT_MSG_CLIENT_APP_BROADCAST` — relay to all players in the world
- `RT_MSG_CLIENT_APP_SINGLE` — relay to a specific player
- `RT_MSG_CLIENT_APP_LIST` — relay to a list of players

Worlds are created by MLS and cleaned up automatically when all players disconnect.

### NAT — Punchthrough Helper (port 10069, UDP)

PS2 games send a 4-byte UDP datagram to this server before establishing peer-to-peer connections. The server echoes back the client's external IP and port (6 bytes: 4 IP + 2 port big-endian), allowing the game to advertise its external endpoint to other players.

A last-byte value of `0xD4` is a Medius keepalive sentinel and is silently discarded.

---

## DNAS — Dynamic Network Authentication System

DNAS is Sony's game authentication service. The PS2 contacts DNAS over HTTPS before connecting to Medius. Without a valid DNAS response, most SCE-published games refuse to go online.

### DNS intercept

The built-in `DnsServer` listens on UDP port 53 and intercepts A-record queries for:
- `*.dnas.playstation.org`
- `*.pdonline.scea.com`

These are answered with `ServerIp`. All other queries are forwarded to `8.8.8.8`.

**Requires elevated privileges** (administrator on Windows, root or `iptables` redirect on Linux) to bind port 53.

### HTTPS endpoint

`DnasServer` listens on port 443 (configurable via `DnasHttpsPort`) using BouncyCastle TLS. It is used instead of the system TLS stack because:

- Modern Windows Schannel blocks RC4 and SSLv3 at the kernel level
- The PS2 DNAS client only offers `TLS_RSA_WITH_RC4_128_SHA`, `TLS_RSA_WITH_RC4_128_MD5`, and `TLS_RSA_WITH_3DES_EDE_CBC_SHA`
- BouncyCastle implements TLS in userspace and can negotiate these suites

The server forces **TLS 1.0** because the PS2 sends an SSLv2-format ClientHello but uses TLS 1.0 record semantics.

A plain-HTTP debug fallback is also available on `DnasPort` (10060) for testing without SSL.

### Request routing

| Request path | Handler | Description |
|---|---|---|
| Path contains `"connect"` | ConnectHandler | Main authentication request, response encrypted with per-request Triple-DES keys |
| Path contains `"others"` | OthersHandler | Secondary requests, response returned as-is |

### Response encryption

`connect` responses are encrypted using a custom Triple-DES CBC scheme (`DnasCrypto`), ported from the open-source [DNASrep](https://github.com/FogNo23/DNASrep) project:

- **Pass A** (dynamic keys): derived from SHA1 of slices of the client request body, applied at offset `0xC8` for `0x20` bytes
- **Pass B** (envelope keys): hardcoded constants, applied at offset `0x28` for `0x120` bytes

Each 8-byte block is processed as: XOR with previous ciphertext → DES-ECB encrypt key1 → DES-ECB decrypt key2 → DES-ECB encrypt key3.

### DNAS certificate

The server needs an SSL certificate that the PS2 DNAS client will accept. Place:
- `Certificates/dnas.crt` — DER or PEM certificate
- `Certificates/dnas.key` — PEM private key

The same certificate used by DNASrep or Horizon Private Server works. Custom paths can be set in `config.json` via `DnasCertPath` and `DnasKeyPath`.

### Response files

Pre-built DNAS response blobs are stored under `Data/<DnasRegion>/`. The server selects a file using:

```
hex(gameID) + "_" + hex(qrytype[0..4])
```

These files can be extracted from a real DNAS capture or taken from existing open-source DNAS server projects.

---

## Transport protocol — SCERT

All Medius TCP servers (MUIS, MAS, MLS, DME) use the **SCERT** (SCE Real-Time) transport layer.

### Packet format

```
[type:1][length:2][payload:length]
```

### Handshake (old Medius ≤ 108, typical for 2002–2004 PS2 games)

```
CLIENT_HELLO (0x24)              → SERVER_HELLO (0x25)
CLIENT_CRYPTKEY_PUBLIC (0x12)    → SERVER_CRYPTKEY_PEER (0x13)
CLIENT_CONNECT_TCP (0x00)        → SERVER_CRYPTKEY_GAME (0x14)
                                   SERVER_CONNECT_ACCEPT_TCP (0x07)
                                   SERVER_CONNECT_COMPLETE (0x1A)
CLIENT_CONNECT_READY_TCP (0x21)  → SERVER_CONNECT_COMPLETE (0x1A)
<Medius application messages follow>
```

### Payload encryption

After the handshake, all application-layer payloads are encrypted with **PS2 RC4** — a slight variation of standard RC4 with a custom key schedule. The session key is exchanged in `CLIENT_CRYPTKEY_PUBLIC` / `SERVER_CRYPTKEY_PEER`.

### Message classes

Medius application payloads begin with a one-byte class identifier:

| Class | Value | Description |
|---|---|---|
| `MessageClassDME` | 0 | DME in-game relay messages |
| `MessageClassLobby` | 1 | Standard Medius lobby messages |
| `MessageClassApplication` | 2 | Game-specific application messages |
| `MessageClassLobbyReport` | 3 | Lobby report messages |
| `MessageClassLobbyExt` | 4 | Extended lobby messages (used by MUIS) |
| `MessageClassLobbyAuth` | 5 | Authentication messages |

---

## Connecting a PS2

### Network setup

1. Route PS2 DNS to the CodeDesigner server IP (either configure the PS2 network settings manually or use the built-in DNS intercept on port 53).
2. Ensure ports `10069–10075` TCP and `10069` UDP are reachable from the PS2.
3. Set `ServerIp` in `config.json` to your server's LAN IP.

### PS2 network settings

Configure the PS2 network adapter to use:
- **Primary DNS**: `<ServerIp>` (so the DNS intercept captures DNAS lookups)
- **Secondary DNS**: any (e.g. `8.8.8.8`)

### Running the server

```powershell
# Start from the Multiplayer project directory
cd CodeDesigner.Ps2.Multiplayer

# Windows — requires elevated prompt for ports 53 and 443
dotnet run

# Or publish and run the executable directly
dotnet publish -c Release -o publish
.\publish\CodeDesigner.Ps2.Multiplayer.exe
```

---

## Supported games

The server is tested with:

| Game | Region | AppId |
|---|---|---|
| SOCOM: U.S. Navy SEALs II | NTSC-U | 10683 |
| SOCOM: U.S. Navy SEALs II | PAL | 10684 |

Other SCE Medius-based PS2 games should work — add their AppIds to `AllowedAppIds` in `config.json`.

---

## In-memory storage

All game state (accounts, channels, game rooms, sessions) is kept in memory. There is no database backend for the multiplayer server. State is lost on restart. This is intentional for a LAN/private-use server.

---

## See also

- [DNASrep](https://github.com/FogNo23/DNASrep) — reference DNAS server implementation
- [Horizon Private Server](https://github.com/Horizon-Private-Server) — full PS2 online private server infrastructure
- [Socket Communication](socket-communication.md) — CodeDesigner's internal JSON socket protocol
