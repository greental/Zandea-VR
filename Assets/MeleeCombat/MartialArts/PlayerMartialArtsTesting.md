# Player martial arts in the VR arena

Open `MartialArtsVRArena.unity` and enter Play mode with an XR headset. The player starts behind the two unarmed bandits.

## Controls

- Grip alone: normal direct grab and floor pickup.
- Grip and trigger together on an empty hand: martial stance. Keep both held to keep the fist clenched. Release either to return to ordinary hand interactions.
- Move a clenched hand deliberately into a bandit: punch. A punch requires at least 0.19 m of travel and 1.55 m/s; a landed punch requires recovery and a brief still hand before another hit.
- Place a clenched fist or forearm in the path of an incoming strike: block. Move it toward the attacker during contact for a parry.
- Left primary button: low front kick. Right primary button: crescent kick used as the lateral kick. These actions are configurable on `PlayerMartialKick` and do not use the movement sticks.

## Headset checks still needed

Check grip-first and simultaneous grip/trigger presses beside grabbable items, both hands punching independently, one hand holding a weapon while the other punches, release and pickup transitions, fist/forearm contact placement, and kick foot alignment against moving bandits. Tune fist radius, speed, travel, kick input bindings, and the selected imported kick clips from headset observation. The crescent clip is a lateral substitute because the imported fighter pack does not include a clip named roundhouse or front kick.
