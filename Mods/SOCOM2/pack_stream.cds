// SOCOM II (SCUS-97275, r0004) - mount an asset archive that lives on the HOST (bridge), not on disc or memory card
//
// The archive layer (mount ctx 0x004A4DD0) reads a mounted archive through ONE stream object, ctx+0x78, and only
// calls three of its methods: +0x1C mode flags, +0x24 seek(offset, whence) (absolute entry offset+pos), +0x28
// read(buf, n). The entry table is ctx+0x6C for the CDB layout (0x110 header, 0x70-byte entries, entry +0x54 bit 0
// clear = stored raw: no inflate, no SHA-1 check). So an archive can be served from anywhere by swapping in a stream
// whose read asks the host for the bytes:
//
//   read(buf, n): mailbox {+4 buf, +8 pos, +0xC n}, set +0 = 1, spin until the host (socom_bots.py loadpack mem)
//                 has written the bytes straight into buf and cleared +0. No staging copy in EE RAM (the heap has no
//                 room for one). Gives up after ~2^30 polls (returns 0 bytes -> the load fails, no hang).
//
// Emulator / bridge only - on a console use the memory-card copy (the game's own loader mounts
// mc0:/BASCUS-97275<NAME>/<NAME>.CDB when RUN/<NAME>.ZDB is not on the disc).
//
// CODE and DATA live on different 4 KB pages: the host writes the mailbox from another thread while the EE spins
// in ms_read, and a write into a page holding recompiled code makes PCSX2 throw that code away under the running
// block (first version, mailbox at 0x000D6100: the emulator crashed in the spin loop).
//
//   code page 0x000D6000:
//   0x000D6000 mem_mount(ctx)    ctx+0x6C = header (0x000D7800, written by the host), ctx+0x78 = our stream
//   0x000D6040 mem_unmount(ctx)  take our pointers back out of ctx, then ZDB_Reset(ctx) (frees the rest)
//   data page 0x000D7000:
//   0x000D7000 mailbox           +0 pending, +4 buf, +8 pos, +0xC n
//   0x000D7040 stream object     +0 vtable, +4 pos, +8 archive size (host)
//   0x000D7080 vtable            +0x1C flags, +0x24 seek, +0x28 read, everything else returns 0
//   0x000D7800 archive header + entry table (host)

address $000D6000
mem_mount:
    lui   $t0, $000D
    addiu $t1, $t0, $7800
    sw    $t1, $006C($a0)                 // CDB entry table
    addiu $t1, $t0, $7040
    sw    $t1, $0078($a0)                 // stream
    sw    $zero, $0004($t1)               // pos = 0
    sw    $zero, $0068($a0)               // no ZDB table
    sw    $zero, $0070($a0)
    sw    $zero, $0074($a0)
    sw    $zero, $007C($a0)               // no memory-card stream (its popup)
    sw    $zero, $0080($a0)
    sw    $zero, $0084($a0)
    jr    $ra
    addiu $v0, $zero, 1

address $000D6040
mem_unmount:
    lui   $t0, $000D
    addiu $t1, $t0, $7800
    lw    $t2, $006C($a0)
    bne   $t2, $t1, :mu_stream
    nop
    sw    $zero, $006C($a0)
    sw    $zero, $0074($a0)
mu_stream:
    addiu $t1, $t0, $7040
    lw    $t2, $0078($a0)
    bne   $t2, $t1, :mu_reset
    nop
    sw    $zero, $0078($a0)
mu_reset:
    j     $003C8B80                       // ZDB_Reset(ctx): the inflate buffers etc. are the game's own
    nop

address $000D60C0
ms_zero:                                  // every method we do not implement
    jr    $ra
    daddu $v0, $zero, $zero

ms_flags:                                 // +0x1C: opened for reading (no memory-card bit 8)
    jr    $ra
    addiu $v0, $zero, 1

ms_seek:                                  // +0x24 (a1 offset, a2 whence 0 set / 1 cur / 2 end)
    lw    $v0, $0004($a0)
    addiu $t0, $zero, 1
    beq   $a2, $t0, :ms_seek_set
    addu  $v0, $v0, $a1
    lw    $v0, $0008($a0)
    addiu $t0, $zero, 2
    beq   $a2, $t0, :ms_seek_set
    addu  $v0, $v0, $a1
    daddu $v0, $a1, $zero
ms_seek_set:
    jr    $ra
    sw    $v0, $0004($a0)

address $000D6180
ms_read:                                  // +0x28 (a1 buf, a2 n) -> n
    lui   $t0, $000D
    addiu $t0, $t0, $7000
    sw    $a1, $0004($t0)
    lw    $t1, $0004($a0)
    sw    $t1, $0008($t0)
    sw    $a2, $000C($t0)
    addiu $t1, $zero, 1
    sw    $t1, $0000($t0)                 // ask the host
    lui   $t2, $4000                      // poll budget (~2^30)
mr_wait:
    lw    $t1, $0000($t0)
    beq   $t1, $zero, :mr_done
    addiu $t2, $t2, -1
    bne   $t2, $zero, :mr_wait
    nop
    sw    $zero, $0000($t0)               // host gone: fail the read
    jr    $ra
    daddu $v0, $zero, $zero
mr_done:
    lw    $t1, $0004($a0)
    addu  $t1, $t1, $a2
    sw    $t1, $0004($a0)
    jr    $ra
    daddu $v0, $a2, $zero

address $000D7040
pack_stream:
    hexcode $000D7080
    hexcode $00000000
    hexcode $00000000
    hexcode $00000000

address $000D7080
pack_stream_vtable:
    hexcode $00000000
    hexcode $00000000
    hexcode $000D60C0
    hexcode $000D60C0
    hexcode $000D60C0
    hexcode $000D60C0
    hexcode $000D60C0
    hexcode $000D60C8
    hexcode $000D60C0
    hexcode $000D60D0
    hexcode $000D6180
    hexcode $000D60C0
    hexcode $000D60C0
    hexcode $000D60C0
    hexcode $000D60C0
    hexcode $000D60C0
    hexcode $000D60C0
    hexcode $000D60C0
    hexcode $000D60C0
    hexcode $000D60C0
