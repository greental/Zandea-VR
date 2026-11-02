# Opening combat progression and balance

## Implementation state

Slices 2–5 are implemented in the existing `DamagePacket` → health receiver pipeline. Slice 6 has automated scene/profile, formula, and save/load checks in an isolated Unity project. In-headset balance remains to be measured.

## Starting profiles

| Character | Strength | Toughness | Endurance | Martial Arts | Staff proficiency |
|---|---:|---:|---:|---:|---:|
| Kael | 8 | 10 | 10 | 5 | 5 |
| Ordinary slaver | 22 | 25 | 25 | 16 | 25 |
| Slaver leader | 30 | 32 | 30 | 24 | 34 |

The opening director applies these profile assets to its existing player and three V2 slaver instances. It configures the existing staff strike damage to 11.5 and the slavers' unarmed trauma multiplier to 1.5. These fields are editable on the director. The shared slaver prefab and other NPCs retain their existing settings.

## Design calculation before runtime checks

- A maximum-speed starting Kael punch is capped at 16 base damage before attributes. His Strength and Martial Arts reduce that by a combined factor of about 0.834; an ordinary slaver's Toughness increases incoming damage by 1.125. One such punch therefore does about 15 health damage and 22.5 trauma with the opening multiplier. Two top-speed torso punches produce about 45 trauma, below the ordinary slaver's approximately 52.5 trauma threshold. Typical punches do less. An unhelmeted head can still trigger the existing probabilistic short knockout.
- An ordinary slaver's 11.5 base staff strike is reduced to about 10.3 by Strength and staff proficiency, then increased to about 12.3 against Kael's low Toughness. Three clean nonlethal strikes yield about 37 trauma, above Kael's approximately 36 trauma threshold. Blocks, parries and glancing hits reduce or avoid that damage.
- A one-time fake-surrender punch against the current captor receives a configurable 1.8 multiplier. A strong surprise punch followed by a strong ordinary punch can exceed the captor's trauma threshold, while weaker or blocked strikes may not. The first strike is marked as a surprise in recent-hit diagnostics.
- XP is awarded from resolved meaningful hits. The default total award cap is 25 XP per 120-second window across attributes. The next point costs `30 + 6 × current attribute value` XP. This prevents a few punches from leveling Kael into an experienced fighter.

The isolated Unity validation measured these configured formulas: `OPENING_BALANCE_CALC` reported punch `15.02`, ordinary-slaver trauma threshold `52.50`, staff `12.32`, and Kael trauma threshold `36.00` in both scenes. These are **formula results**, not physical headset strikes.

## Unity automated results

- `OPENING_PROGRESSION_VALIDATION_PASS`: both scenes have the intended profiles and exactly one attribute/progression component per opening character after repeated setup; the unrelated unified NPC prefab retains its original profile. A temporary Kael-style save/load round trip restored Strength, XP, and punch-attempt statistics.
- `OPENING_MAIN_SMOKE ... nav=True,True,True`: all three opening slavers reached NavMesh at their saved encounter positions after the opening root and navigation center were corrected.
- `OPENING_CAPTURED_TITLE_RECOVERY_PASS`: the title route completed, blackout faded back, the player recovered, and the capture group resumed.
- `OPENING_MAIN_POST_KO_CAPTURE_PASS state=Binding`: the Main-scene slavers reached the player and began the existing binding action after knockout/title handoff.

## Checks still needed

1. Exercise real unarmed and Bo Staff collisions in headset and record actual health/trauma damage, block/parry state, XP, and knockout count. The formula test does not replace physical strike testing.
2. Confirm one punch attempt and one landed hit per swing, one staff hit per attack, bounded recent history, and a single knockout transition under headset motion.
3. Confirm the stereo blackout and welcome title in both eyes. Batch mode compiled the instanced shader but has no headset display.
4. Recheck surrender, escort, disarm, weapon physics, gore, replay, and recovery interactively after the latest changes.

## Manual headset checks

Play one slaver and three slaver scenarios; torso and head punches; fake surrender and follow-up; three clean staff hits; blocking/dodging; increased attributes; knockout/recovery; and a reload. Record measured damage, trauma, XP and number of clean hits to knockout in this report. VR hand responsiveness and physical balance can only be confirmed in headset.

## Current limits

- Combat has no stamina resource. The progression component tracks Endurance from meaningful combat and encounter survival, while the debug Inspector reports stamina as unavailable.
- No body-region enum exists in the damage packet. Head location is used by NPC knockout chance, but recent-hit records omit region rather than inventing one.
- The current damage pipeline has no general armor resistance. Head protection affects the existing short-knockout chance; geometric armor coverage is outside this milestone.
- Only Kael has an automatic persistent identity. Ordinary spawned NPC progression is disabled by default; their combat totals are available during the session.
