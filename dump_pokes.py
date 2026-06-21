import struct, collections
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

b = streams[(10075, 44250)]   # server -> client
# decode RT messages
i=0; msgs=[]
while i+3<=len(b):
    t=b[i]; ln=b[i+1]|(b[i+2]<<8); enc=t&0x80; rt=t&0x7F; i+=3
    hdr=i
    if enc: i+=4
    if i+ln>len(b): break
    payload=bytes(b[i:i+ln]); i+=ln
    msgs.append((rt,bool(enc),ln,payload))

pokes=[(ln,p) for (rt,enc,ln,p) in msgs if rt==0x1E and not enc]
print(f"total msgs={len(msgs)}  plaintext MEMORY_POKE count={len(pokes)}")
print("size histogram:", collections.Counter(ln for ln,_ in pokes))

# Try to interpret poke payload. Print first few pokes' leading bytes interpreted a few ways.
for idx,(ln,p) in enumerate(pokes[:6]):
    a_le = struct.unpack_from('<I',p,0)[0]
    sz_le= struct.unpack_from('<I',p,4)[0] if ln>=8 else None
    print(f"\npoke[{idx}] len={ln}  addr_le=0x{a_le:08X} size_le={sz_le}")
    print("  head:", p[:24].hex())

# Reconstruct: assume [addr:4][size:4][data]. Check addresses are sequential & contiguous.
print("\n--- assuming [addr:4][size:4][data] ---")
addrs=[]
for ln,p in pokes:
    if ln>=8:
        a=struct.unpack_from('<I',p,0)[0]; s=struct.unpack_from('<I',p,4)[0]
        addrs.append((a,s,ln-8))
for a,s,dlen in addrs[:8]:
    print(f"  addr=0x{a:08X} size={s} datalen={dlen}")
if addrs:
    lo=min(a for a,_,_ in addrs); hi=max(a+ (dlen) for a,_,dlen in addrs)
    print(f"  addr range 0x{lo:08X}..0x{hi:08X}  span={hi-lo} bytes  npokes={len(addrs)}")
    # contiguity
    s2=sorted(addrs)
    gaps=0
    for j in range(1,len(s2)):
        prev_end=s2[j-1][0]+s2[j-1][2]
        if s2[j][0]!=prev_end: gaps+=1
    print(f"  noncontiguous steps={gaps}")

# Dump every poke payload in wire order: [len:4 LE][payload] so the server can replay verbatim.
total_data=sum(ln for ln,_ in pokes)
with open(r'C:\Users\tholu\Desktop\socom2_lobby_pokes.bin','wb') as f:
    for ln,p in pokes:
        f.write(struct.pack('<I',ln)); f.write(p)
print(f"\nwrote {len(pokes)} pokes ({total_data} payload bytes) -> socom2_lobby_pokes.bin")
# Also: what's the very first non-poke server push after login (the 94/34 SERVER_APPs)?
print("\nfirst 14 server messages (type/enc/len):")
for rt,enc,ln,p in msgs[:14]:
    print(f"  rt=0x{rt:02X} {'ENC' if enc else 'pln'} len={ln}")
