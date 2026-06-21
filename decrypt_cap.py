import struct, collections

# ---- PS2 Medius RC4 (port of PS2_RC4.cs) ----
def setkey(key, h):
    s=[(255-i)&0xFF for i in range(256)]
    # hash scramble
    li=0; ci=0; idi=0
    while True:
        v1=h[idi]; idi=(idi+1)&3
        temp=s[ci]; v1=v1+li; li=(temp+v1)&0xFF
        s[ci]=s[li]; s[li]=temp
        ci=(ci+5)&0xFF
        if ci==0: break
    # key schedule
    ki=0; li=0; ci=0
    while True:
        kb=key[ki]+li; ki=(ki+1)&0x3F
        cb=s[ci]; cv=cb&0xFF
        cb=cb+kb; li=cb&0xFF
        t0=s[li]; s[ci]=t0; s[li]=cv
        ci=(ci+3)&0xFF
        if ci==0: break
    return s

def hash_valid(h):
    return not (h[0]==0 and h[1]==0 and h[2]==0 and (h[3]&0x1F)==0)

def decrypt(key, h, data):
    if not hash_valid(h): return bytes(data)  # plaintext passthrough
    s=setkey(key,h); x=0; y=0
    out=bytearray(len(data))
    for i in range(len(data)):
        y=(y+5)&0xFF
        v0=s[y]; a2=v0&0xFF
        v0=v0+x; x=v0&0xFF
        v0=s[x]; s[y]=v0&0xFF; s[x]=a2
        a0=data[i]
        idx=(v0+a2)&0xFF
        a0^=s[idx]&0xFF
        out[i]=a0&0xFF
        x=(s[a0&0xFF]+x)&0xFF
    return bytes(out)

def encrypt(key, h, data):
    if not hash_valid(h): return bytes(data)
    s=setkey(key,h); x=0; y=0
    out=bytearray(len(data))
    for i in range(len(data)):
        x=(x+5)&0xFF
        y=(y+s[x])&0xFF
        s[x],s[y]=s[y],s[x]
        out[i]=data[i]^(s[(s[x]+s[y])&0xFF])
        y=(s[data[i]]+y)&0xFF
    return bytes(out)

KEY=bytes(64)  # all-zero session key
# Self-test: encrypt 0xA5 (hash 4fb8cf6a) with zero key -> should be captured 0x83 if key is zeros
import sys
_e=encrypt(KEY,bytes.fromhex('4fb8cf6a'),bytes([0xA5]))
_d=decrypt(KEY,bytes.fromhex('4fb8cf6a'),_e)
print(f"SELFTEST zero-key: enc(0xA5)={_e.hex()} (captured=83)  roundtrip dec={_d.hex()} (expect a5)",file=sys.stderr)

LOBBY={0x03:"SessionBegin",0x04:"SessionBeginResp",0x07:"AccountLogin",0x08:"AccountLoginResp",
0x31:"PlayerInfo",0x32:"PlayerInfoResp",0x49:"UpdateUserState",0x4E:"SetGameListFilter",0x4F:"SetGameListFilterResp",
0x86:"VersionServer",0x87:"VersionServerResp",0xA3:"SetLocalizationParams",0xA4:"SetLocalizationParamsResp",
0xA5:"FileCreate",0xA6:"FileCreateResp",0xA7:"FileUpload",0xA8:"FileUploadResp",0xA9:"FileUploadServerReq",
0xAA:"FileClose",0xAB:"FileCloseResp",0xAC:"FileDownload",0xAD:"FileDownloadResp",0xAE:"FileDownloadStream",
0xAF:"FileDownloadStreamResp",0xB2:"FileListFiles",0xB3:"FileListFilesResp",0x11:"AccountUpdateStats",
0x12:"AccountUpdateStatsResp",0xCE:"UpdateLadderStats",0xCF:"UpdateLadderStatsResp"}

d=open(r'F:\Desktop\packets\universe-community-screen.pcap','rb').read()
off=24; streams=collections.OrderedDict()
while off+16<=len(d):
    ts,tu,incl,orig=struct.unpack_from('<IIII',d,off); off+=16
    pkt=d[off:off+incl]; off+=incl
    if len(pkt)<34 or pkt[12:14]!=b'\x08\x00': continue
    ip=pkt[14:]; ihl=(ip[0]&0xF)*4
    if ip[9]!=6: continue
    tcp=ip[ihl:]; sp,dp=struct.unpack_from('>HH',tcp,0); doff=(tcp[12]>>4)*4
    pl=tcp[doff:]
    if pl: streams.setdefault((sp,dp),bytearray()).__iadd__(pl)

PEER=bytearray()
RTN={0x12:"CLIENT_CRYPTKEY_PUBLIC",0x13:"SERVER_CRYPTKEY_PEER",0x14:"SERVER_CRYPTKEY_GAME",
0x07:"SERVER_CONNECT_ACCEPT_TCP",0x1A:"SERVER_CONNECT_COMPLETE",0x0A:"SERVER_APP",0x0B:"CLIENT_APP_TOSERVER",
0x00:"CLIENT_CONNECT_TCP",0x05:"CLIENT_ECHO",0x1E:"SERVER_MEMORY_POKE"}

for (sp,dp),b in streams.items():
    if sp!=10075: continue
    print("RAW S->C first 80:", bytes(b[:80]).hex())
    i=0; cnt=0
    while i+3<=len(b) and cnt<12:
        t=b[i]; ln=b[i+1]|(b[i+2]<<8); enc=t&0x80; rt=t&0x7F; i+=3
        h=b[i:i+4] if enc else b'\x00'*4
        if enc: i+=4
        if i+ln>len(b): break
        payload=bytes(b[i:i+ln]); i+=ln
        extra=""
        if rt==0x13:
            PEER[:]=payload
            extra=" PEER_payload="+payload.hex()
        if rt==0x05:  # CLIENT_ECHO — known plaintext 0xA5
            extra=(" echo_raw="+payload.hex()
                   +" decZero="+decrypt(KEY,h,payload).hex()
                   +" decPeer="+decrypt(bytes(PEER),h,payload).hex()
                   +" decPeerRev="+decrypt(bytes(PEER)[::-1],h,payload).hex()+" (expect a5)")
        if rt==0x0A: extra=" decPeer[:8]="+decrypt(bytes(PEER),h,payload)[:8].hex()
        print(f"  {RTN.get(rt,hex(rt)):26s} enc={bool(enc)} len={ln} hash={h.hex()}{extra}")
        cnt+=1
    break
