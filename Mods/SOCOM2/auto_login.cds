// SOCOM II (SCUS-97275, r0004) - hard-coded online username / password
//
// For when memory-card profile saving doesn't work: the login reads the account name and password
// from the script variables PLAYERNAMEVAR / PLAYERPASSWORD (table 0x00444890, looked up with
// 0x003A5AD0, which returns a variant: +0 type byte, 3 = string; +4 char*). The connect function
// 0x00276040 passes them to the Medius login setup 0x0031A470, and 0x0027EEA0 checks both are
// non-empty before an automatic login.
//
// This mod points those four lookups at two stubs that return fixed string variants, so the
// login always uses the strings below. The game's own variables are left alone.
//
// EDIT THE TWO STRINGS BELOW (max 31 characters each), then compile and apply the code.
// Apply it continuously (pnach / cheat device), like the other r0004 mods.

// Fixed variants: { type = 3 (string), char* }
address $000D0400
name_var:
    hexcode $00000003
    hexcode $000D0440
pass_var:
    hexcode $00000003
    hexcode $000D0460

address $000D0410
name_lookup:
    lui   $v0, $000D
    jr    $ra
    addiu $v0, $v0, $0400
pass_lookup:
    lui   $v0, $000D
    jr    $ra
    addiu $v0, $v0, $0408

// Account name (32-byte buffer)
address $000D0440
    string "USERNAME"

// Password (32-byte buffer)
address $000D0460
    string "PASSWORD"

// 0x00276040 (login connect): PLAYERNAMEVAR / PLAYERPASSWORD lookups
address $002760B4
    jal   :name_lookup
address $002760D4
    jal   :pass_lookup

// 0x0027EEA0 (have-credentials check): same two lookups
address $0027EEC0
    jal   :name_lookup
address $0027EEE0
    jal   :pass_lookup
