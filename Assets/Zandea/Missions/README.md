# An Honest Day's Work â€” implementation status

Daran is authored in Main’s Pueblobinito/Pueblo settlement. **Zandea > Missions > Place Daran in Main Pueblo** places his existing prefab and counter under the streamed `Layout Preview`, and his existing sword, bo staff, and bandage stock under the streamed layout, along with three instances of the existing barrel prefab and a separate delivery trigger. The stall is near the west ramada; the storage barrels are about 30 m away. The command is idempotent by object ID and saves `Assets/Main.unity`. The separate Daran voice test scene is preserved.

The session state is held by `MissionSessions`. It is reset on a new play session. There is no disk save adapter. A streamed scene that destroys and recreates its barrel objects still needs a rehydration strategy; the current authored setup covers Main Pueblo. The merchant has separate session state for balance and outstanding payouts. Legacy serialized fields retain their original numeric values and names for compatibility.

Author another transport contract by creating a `TransportMissionDefinition`, setting its stable mission and giver IDs, count, reward and breakage amount, attaching a `DaranContract` to the intended NPC, and assigning the existing physical barrel instances with unique `ContractBarrel.objectId` values. Put a `ContractDeliveryZone` on an authorable trigger beside the recipient. The first mission uses a per-delivery reward and per-destroyed-barrel penalty. The zone requires release and a stabilization delay.

Add `CarryWeight` to other XR grabbable objects and set effective weight. `StrengthAndEncumbrance` on the player exposes capacity and XP rates. `JunkerSprint` composes its speed with that component. To add prices, continue setting the existing integer `valueCats` field; it is retained as a serialized legacy field and displays through `CurrencyDefinition` as Zeks.

Voice clips to regenerate through the existing offline/editor ElevenLabs workflow: `insufficient_funds_001` originally referenced clip GUID `b630bec17a2977f4580e9cdf5a1ae962`. `merchant_lacks_funds_001` and any other clips containing the old currency name require a full asset audit before release. The affected bank clip references are disabled so updated subtitles do not play mismatching audio. New contract nodes have no generated audio and use subtitle fallback.

## Validation and limitations

Unity 6000.2.8f1 compiled the scripts, applied the placement to Main, and returned `OK validate` for scene references. Main contains Daran, the counter, three stock items, three assigned barrels, and the delivery zone. Play Mode mission scenarios and Quest 2 headset checks were not completed. Do not treat the mission as ship ready until the acceptance scenarios are exercised. Outstanding: debt dialogue and repeat settlement polish, scene streaming rehydration, disk persistence, mission feedback UI, voice replacement, and full VR validation.
