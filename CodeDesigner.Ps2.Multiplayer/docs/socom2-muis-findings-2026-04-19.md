# SOCOM II MUIS -> MAS Findings (2026-04-19)

## Summary
The current MUIS->MAS failure is most consistent with an early parser reject in `UniverseStatusParser` caused by non-zero status at payload offset `0x18`.

## Verified
- `UniverseStatusParser` (`0x0031AE90`) is reached.
- Parser takes early failure path to `0x0031B218` when `CheckStatusCode` fails.
- `gUniverseConnectFlag` (`0x00453150`) is `1` after the failure path.
- `gUniverseCount` (`0x00453124`) remains `2`.
- New universe slot remained zero in sampled memory (`0x00770E00`).

## Corrected Prior Assumption
The second registry lookup at `0x0031B080` does not directly skip `UniverseListAdd`; null return there skips enrichment copy and control continues into universe add setup.

## Registry Notes
- Lookup backing pointer chain observed:
  - `0x001CC750` -> `0x001CC460`
  - `+0x5C` => `0x001CC4BC` (null in this run)
- `0x00421AC0` is a struct-like block containing `MP_MEDIUS_ERROR`, not a plain key string.

## Next Verification
1. Capture exact decrypted MUIS `RT_MSG_SERVER_APP` payload bytes around `0x10..0x20`.
2. Confirm `payload[0x18..0x1B] == 00 00 00 00` at PS2 parse time.
3. If not zero, validate `SERVER_APP` framing/encryption handling in MUIS send path.
4. Re-test for:
   - `gUniverseConnectFlag == 0`
   - `gUniverseCount` increment
   - new universe entry host/port matching configured MAS target.

## Active Debug Hooks Used
- Breakpoint `0x0031AE90` (`UniverseStatusParser`)
- Breakpoint `0x003FFC20` (LobbyExt dispatch wrapper area)
