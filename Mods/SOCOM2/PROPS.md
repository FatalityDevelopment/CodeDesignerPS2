# SOCOM II props: vehicles and map items at runtime

Place vehicles and other models into a running SOCOM II match. Props come from asset packs on this PC
(`F:/SOCOM Packs`), are loaded into the game over the CodeDesigner bridge, and stand on the ground in front of you.
They are scenery only: nothing drives, collides or shoots, and only you see them (nothing is sent over the network).

You need to be in a round (not the lobby or the end-of-round screen) with the bridge connected.

## Placing a prop

```
python SOCOM2/bots/socom_bots.py prop <pack> [model]
```

Loads the pack if it isn't loaded yet, then places its model 12 units in front of you. The model is lifted so its
lowest point sits on the ground. Run it again to place another copy; a pack only loads once per map.

Example: `prop mcv117` places the SOCOM: Combined Assault Bradley.

## Removing props

```
python SOCOM2/bots/socom_bots.py despawn        # the newest prop
python SOCOM2/bots/socom_bots.py despawn all    # every prop placed on this map
```

Only props placed with `prop` are tracked. Loading a new map clears everything anyway.

## Limits

- **Texture memory.** Map props share one small texture pool: about 0xBA blocks free on a fresh map. Each pack's
  need is in the table below; `prop` refuses to load a pack that doesn't fit (it would draw black or red/green
  garbage), and the fix is to reload the map. Small vehicles fit two or three to a map.
- **Heap.** Each loaded pack also uses main memory; `prop` refuses when it runs low.
- **Look.** Converted vehicles are lit flat (no baked shading), use 32x32 textures, and have no rotor blur or glass
  transparency.

## Vehicle packs (from SOCOM: Combined Assault)

`F:/SOCOM Packs/SOCOMCA/static/<pack>`, catalogued in `vehicles.json`. Confirmed in game: Humvee, Bradley,
Little Bird, CRRC.

| Pack | Vehicle | Triangles | Texture blocks |
|---|---|---|---|
| mcv100 | Utility van | 1906 | 0x33 |
| mcv101 | Multi-turret Humvee | 3045 | 0x45 |
| mcv102 | Technical SUV | 2851 | 0x36 |
| mcv103 | SOC-R boat | 2442 | 0x25 |
| mcv104 | Humvee .50 cal | 1954 | 0x39 |
| mcv105 | Cargo truck | 2223 | 0x50 |
| mcv106 | Ural truck | 2844 | 0x44 |
| mcv107 | CRRC (inflatable boat) | 669 | 0x18 |
| mcv108 | Technical boat | 2365 | 0x33 |
| mcv109 | VBL | 2477 | 0x1f |
| mcv110 | Pave Hawk | 3517 | 0x24 |
| mcv111 | BMP | 4529 | 0x27 |
| mcv112 | Recon Shadow | 2318 | 0x17 |
| mcv113 | ALSV | 2851 | 0x2e |
| mcv114 | Scout | 2289 | 0x46 |
| mcv115 | T-72 | 2264 | 0x28 |
| mcv116 | TOW Humvee | 2057 | 0x39 |
| mcv117 | Bradley (desert) | 3236 | 0x2c |
| mcv118 | Pickup truck | 2355 | 0x36 |
| mcv119 | ZSU AA gun | 2002 | 0x36 |
| mcv120 | Hind | 1893 | 0x16 |
| mcv121 | Shadow RSTV | 2773 | 0x1f |
| mcv122 | Ka-50 | 2415 | 0x12 |
| mcv123 | Little Bird | 1585 | 0x17 |
| mcv124 | Patrol boat | 5350 | 0x35 |

SOCOM II's own vehicles, weapons, gear and map props are ripped to `F:/SOCOM Packs/SOCOM2/<category>` (each folder
has a `catalog.json`); `prop` works with those packs too.

## Converting and testing

Rebuild every vehicle pack (needs the SOCOM: Combined Assault ISO on F:):

```
cd SOCOMCA
python convert_vehicles.py
```

One model at a time, with options (texture size cap, brightness, nodes to leave out):

```
python ca2s2static.py --zdb VEH_HUMV.ZDB --resource veh_humvee_50cal.rdr --model 3 --kind prop --out mcv0001 --max-size 32 --bright 0xA0
```

Every build is checked before it goes near the game (missing textures, palette types, 4-bit textures, texture
memory). To try new builds of the same model without reloading the map:

```
python SOCOM2/bots/socom_bots.py testprop <pack> <model>
```

`testprop` reuses the texture memory of the previous test, so earlier test props turn to garbage - ignore them.
The format notes are in `SOCOMCA/README.md`.
