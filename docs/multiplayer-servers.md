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
| `DnasHttpsPort` | `443` | DNAS gateway (requires elevated privileges). `0` disables DNAS |
| `DnsDnasPort` | `53` | DNS intercept for `*.dnas.playstation.org` (requires elevated privileges) |
| `ExternalDnasServer` | `""` | Use another DNAS server (IP or hostname) instead of the built-in one. See [Using an external DNAS server](#using-an-external-dnas-server) |
| `DnasRegion` | `"us-gw"` | Picks the region certificate (`us-gw` → `cert-us`, `eu-gw` → `cert-eu`, `gai-gw` → `cert-jp`) and the packet region searched first |
| `DnasCertPath` / `DnasKeyPath` | `""` | Gateway certificate/key (PEM). Default: `Certificates/dnas.crt` + `dnas.key` if present, else the `DnasRegion` cert |
| `DnasCaPath` | `""` | Issuer certificate sent with the chain (default `Certificates/ca-cert.pem`) |
| `DnasRsaDirectory` | `""` | DNAS ticket root (defaults to `<DataDirectory>/dnas/RSA`) |
| `DnasCaptureDirectory` | `""` | DNAS packet captures (defaults to `<DataDirectory>/dnas/PacketLogs`) |
| `DnasRepositoryUrl` | `"Data"` | Captured DNAS packets each title's RSA ticket is taken from: a local folder laid out as `<region>/packets/<lookup>_<operation>` (relative to the binary), or an HTTPS DNASrep mirror |

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

#### SOCOM II briefing rooms — the three lists and how the client refreshes them

The r0004 client exposes three script natives to its menus, `RefreshRoomList`, `RefreshGameList` and
`RefreshRoomPlayerList`, and the server answers the requests each of them sends. The layouts below were taken
from the client's own response callbacks (EE RAM dump `CodeDesigner.Ps2.Bridge/Resources/Dumps/r0004.bin`);
every reply echoes the request's 21-byte MessageID and carries the status at offset 0x18.

| Screen / list | Request → reply | What the client does with the reply |
|---|---|---|
| BRIEFING room list | `ChannelList_ExtraInfo0` (0xEC) → one 0x70-byte `ChannelList_ExtraInfoResponse` (0xED) per room | Merges rooms by name, commits when a row has EndOfList set. Player counts come from online presence. |
| Lobby (game) list | `GameList_ExtraInfo0` (0xEA) → one 0x190-byte `GameList_ExtraInfoResponse0` (0xEB) per game | Marks every known game stale, merges rows by game name, prunes stale games at EndOfList. The server always sends the room's complete list (default rooms + player games). |
| Filtered refresh | `ClearGameListFilter0` (0x50) → `ClearGameListFilterResponse` (0x51), then `SetGameListFilter0` (0x4E) → (0x4F), then the game list | A chain: each callback sends the next request, so **every link must be answered** or the list never refreshes. Filters are acknowledged but not applied. |
| Players in the room | `GetLobbyPlayerNames_ExtraInfo` (0xDC, world = the room) → one 208-byte `GetLobbyPlayerNames_ExtraInfoResponse` (0xDD) per player | Polled at most every 10 s and only when no request is in flight. The client zeroes every known player's status before asking, merges rows by AccountID (name @0x20, OnlineState @0x40: status, lobby/game world, lobby/game name) and erases players still at status 0 at EndOfList. The reply therefore lists **every** player in the room with status 2 (in lobby) or 3 (in game). |
| Players in a game | `GameWorldPlayerList` (0x2F) → 328-byte rows (0x30) | Stats @0x40, EndOfList @0x144. |
| Join by name | `FindWorldByName` (0x37; v108 layout: Name[64] @0x26, WorldType @0x68 — 1 = room, 0 = game) → `FindWorldByNameResponse` (0x38, 0x9C bytes, MediusWorldID @0x44) | Resolves a room or game name to its world id before JoinChannel / JoinGame. |

Other client requests answered so they cannot time out: `AddToBuddyListConfirmation` (LobbyExt 0x05 — the
add-friend-by-name flow, auto-accepted and made mutual, answered with status 2 *RequestAccepted*), `AddPlayerToClan`
(0x5E, leader adds a player and the message is posted on the clan board), `GetTotalUsers` / `GetTotalGames` /
`GetTotalChannels`, `SessionEnd` and `SetLocalizationParams`.

#### Spectators

`CreateGame` carries both a game password and a spectator password; its `MaxPlayers` already includes the
spectator seats (SOCOM II sends players + spectators, with `GameStats[4]` = max spectators — retail default rooms
are 16 + 8 = 24). A spectator sends `JoinGame` with JoinType `MediusJoinAsSpectator` and the **spectator** password
in the password field, so the server checks it against the spectator password (empty = open) and reserves an
ordinary slot in the same DME world, flagged as a spectator in the server's counts and logs. The game itself tells
the other players who is spectating through its own DME messages.

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

### HTTPS endpoint — DnaSDK

`DnasServer` serves DNAS on port 443 (`DnasHttpsPort`) with the code in `CodeDesigner.Ps2.Multiplayer/Dnas/`, which
comes from [DnaSDK](https://github.com/FatalityDevelopment/DnaSDK). `Dnas/README.md` documents the protocol in full.
In short:

- **Transport:** BouncyCastle TLS in userspace. It accepts the PS2's SSLv2-format ClientHello, keeps the original
  hello bytes in the handshake hash, and negotiates TLS 1.0 / RSA / 3DES. One HTTP/1.0 request per connection.
- **Protocol:** a live six-message exchange (C1/S1, C2/S2, C3/S3). Every session gets fresh challenges, a new session
  ID and new request-bound encryption. DnaSDK verifies the console's C2 against the challenge it actually issued.
  Sessions persist across the client's separate TLS connections for 10 idle minutes. Nothing is replayed.
- **Tickets:** the only archived material is the title's original 128-byte RSA-signed ticket, which the retail
  client checks with its built-in public key. On a title's first login DnaSDK finds the captured S1 for the
  console's C1 lookup ID in `Data/<region>/packets/<lookup>_<operation>` (the same packet files the old
  capture-replay server used; `DnasRegion` first, then the other regions). It takes the ticket from offset `0x48`
  and caches it at `<DnasRsaDirectory>/Signatures/<SERIAL>.BIN`.
- **Captures:** every DNAS request and response body is written to `<DnasCaptureDirectory>`
  (`Session<N>_Incoming_<i>.bin`, `Session<N>.log`, `Connections.log`).

### Using an external DNAS server

Set `ExternalDnasServer` to another DNAS server's IP or hostname, for example a friend's public one. In a container,
use `Online__ExternalDnasServer`, `EXTERNAL_DNAS` with docker compose, or `-ExternalDnasServer` with the Azure script.

- The built-in DNAS gateway does not start, so nothing listens on 443. The packets and certificates go unused.
- The DNS responder answers `*.dnas.playstation.org` with the external server's address. A hostname is re-resolved
  at most once a minute, and the last good address is reused if a lookup fails.
- Medius names (`*.pdonline.scea.com`) still resolve to `ServerIp`, so consoles authenticate with the external
  DNAS server and then play on this one.
- The console still needs this server as its primary DNS, because that's how it finds both servers.

### DNAS certificate

Same lookup order as before: `Certificates/dnas.crt` + `dnas.key` (PEM) if present, otherwise the community
certificate for `DnasRegion` (`cert-us.pem`/`cert-us-key.pem`, `cert-eu…`, `cert-jp…`). `ca-cert.pem` is always
sent as the issuer, because the PS2 rejects a chain that has only the leaf certificate. Override with
`DnasCertPath`, `DnasKeyPath` and `DnasCaPath`.

### Code layout and updating from DnaSDK

DnaSDK's source is part of the multiplayer project, not a separate project. Everything is in the
`CodeDesigner.Ps2.Multiplayer.Dnas` namespace:

| `Dnas/` folder | Upstream DnaSDK folder | Contents |
|---|---|---|
| `Client/` | `DNAS.Client/` | Client-side proof model, used by the server's C1 validation |
| `Cryptography/` | `DNAS.Cryptography/` | DES/3DES, RSA, native proof algorithms |
| `Server/` | `DNAS.Server/` | Packet formats, sessions, response producers, ticket store |
| `Transport/` | `TCP.Server/` | TLS listener, HTTP framing, packet journal |
| `Tls/` | `TCP.TLS/` | Legacy TLS 1.0 server, SSLv2-hello adapter, certificate loading |
| `Moduli/` | `Assets/Moduli/` | Optional RSA public moduli, copied to `Assets/Moduli` in the output |

To bring in a newer DnaSDK, copy its files into the matching folders and keep these differences:

- Namespaces `DNAS`, `TCP.Server` and `TLS` all become `CodeDesigner.Ps2.Multiplayer.Dnas`. The `using DNAS;`,
  `using TCP.Server;` and `using TLS;` lines are dropped. In `Client/ClientSetup.cs`, `DNAS.Identity` becomes
  `global::CodeDesigner.Ps2.Multiplayer.Dnas.Identity`.
- Upstream's `Program.cs` and `TCP.Server/ServerOptions.cs` (its standalone command-line tool), `Dockerfile`,
  and `TCP.TLS/Certificates/` aren't copied. Certificates come from `Certificates/`.
- `Server/DnasRepRepository.cs` reads a local packet folder as well as HTTPS, and searches a preferred region first
  (`DnasServerConfiguration.PreferredRegion`, passed through `TicketStore`).

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

## Debug API (localhost)

Set `"DebugApiPort": 10090` under `Online` to get a plain-HTTP API on `127.0.0.1` for testing with a single console
(`Mods/server/socom_server.py` wraps it):

| Endpoint | Purpose |
|---|---|
| `/clients` | Lobby connections: connection id, account, endpoint |
| `/rooms` | Briefing rooms with the players in each (status, game), the games listed there, and the community screen |
| `/lobby/join?name=N[&room=0x44 or US EAST]` | Put a *virtual player* in a briefing room: it appears in that room's player list, player count and `FindPlayer` on a real console (`socom_server.py lobbyjoin N [ROOM]`) |
| `/lobby/leave?name=N` | Remove a virtual player |
| `/chat?text=..[&name=BOT]` | Lobby chat line from any name to everyone in the caller's room |
| `/peek`, `/poke` | Read / write a connected console's EE memory over the lobby connection |
| `/worlds`, `/fake/join`, `/fake/leave`, `/fake/inbox`, `/raw`, `/app` | DME worlds and fake in-game clients |

## In-memory storage

All game state (accounts, channels, game rooms, sessions) is kept in memory. There is no database backend for the multiplayer server. State is lost on restart. This is intentional for a LAN/private-use server.

---

## See also

- [DnaSDK](https://github.com/FatalityDevelopment/DnaSDK) — upstream of the DNAS code in `Dnas/`
- [DNASrep](https://github.com/FogNo23/DNASrep) — archive of original DNAS captures (ticket source)
- [Horizon Private Server](https://github.com/Horizon-Private-Server) — full PS2 online private server infrastructure
- [Socket Communication](socket-communication.md) — CodeDesigner's internal JSON socket protocol
