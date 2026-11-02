# Daran Main integration check — 2026-10-31

The established `VR player - Man Creator.prefab` now contains the V2 palm
notebook and `BackpackUtilityAttachments` with the existing external personal
purse. The V1 and V2 prefabs remain available. Main's active player remains the
working V2 instance.

The Pueblo settlement Daran uses the shared mission dialogue and voice assets.
His scene instance references the active player's notebook and the hauling
contract. The contract references three instances of the existing breakable barrel
prefab. Each has a distinct `daran-honest-work-barrel-N` ID, the mission definition,
and `CarryWeight` set to 20.

Unity's `DaranMissionValidator.ValidateOpenMain` passed. It checked all 26 mission
clips, the work dialogue branches, four financial outcomes, abandonment debt, and
Main scene references. This is editor validation. Barrel transport, settlement,
payout pickup, streaming, and Quest 2 behavior have not been playtested in that editor run.

## Pueblo Play Mode repair — 2026-10-31

The Main scene now uses the friendly Pueblo configuration (`residentBandits: 0`).
Inactive authored bandits stay inactive, and random bandit encounter points use
the settlement wall exclusion with a 20 m margin. Pueblo structures are offset
15 cm below the terrain stamp anchor. `PuebloMissionTestStart` places the active
XR origin 55 m from the settlement on the first surface transition for testing.

The active Pueblo Daran has the existing trade counter, handshake, notebook, and
hauling contract wired together. Three stable-ID instances of the existing
breakable barrel stream with Pueblo. The old Daran scene object remains inactive
for rollback. The wave palm-facing test now applies a configurable 270-degree
yaw correction. Accepting the job keeps Daran's “get moving” acknowledgement.

`PuebloMainPlayValidation` passed in Unity Play Mode on 2026-10-31. It verified
friendly settlement loading, the nearby player start, terrain and structures,
three physical contract barrels, work acceptance, the mounted personal purse,
merchant buy/sell accounting, one physical payout and collection, and Pueblo
unload/reload with the same Daran. Log: `Temp/ZandeaPueblo.main-validation.txt`.
The editor reported no headset attached, so palm alignment, locomotion comfort,
barrel hauling, and the full mission settlement still need Quest 2 playthrough.

## Merchant interaction repair — 2026-11-01

Either hand can complete a single sideways wave reversal to open Daran's
conversation. An accepted job changes the notebook work choice to “About that
job you gave me...” while the mission state still selects Daran's progress line.
Choosing “Let's settle up” now commits the outcome immediately, before the
spoken result can be interrupted. Debt payment accepts the released personal
purse physically on the payment spot even if the trigger callback arrives late.

V1 purse prefabs were preserved and V2 purse prefabs use the authored money-bag
model. Main's V2 player and active Pueblo counter reference V2. Both pouch
types display their current Zeks amount; payout release beside the personal
purse merges once. The bandit currency definition keeps its legacy asset
identity but displays Zeks and spawns the V2 pouch. The former scrap-metal
loot roll now offers a separate 1–3 Zek pouch. Main's Bo Staff stock was
scaled from a measured 1.83 m to about 1.5 m as a scene override.

Picking up a weapon cannot immediately injure Daran. A first deliberate
strike warns “What are you doing?” without damage; another deliberate strike
within five seconds can damage him. This preserves an intentional attack path.

`PuebloMainPlayValidation` passed in headless Unity Play Mode on 2026-11-01.
It checked the active scene wiring, V2 pouch model and visible balances,
merchant purchase/sale, a 30-Zek mission payout, repeat-payment protection,
physical pouch merge, a 10-Zek contract debt paid from the personal purse,
and Pueblo unload/reload. Report: `Temp/ZandeaPueblo.main-validation.txt`.
OpenXR reported no headset, so either-hand wave feel, money-bag scale and
placement, weapon contact, and natural physical payment remain VR checks.

## Live-session follow-up — 2026-11-01

The player confirmed three barrels were delivered and Daran calculated 30
Zeks, but no accessible pouch was seen. The mission handshake was routed only
to the ordinary trade commit. The active V2 backpack was also mounted far
behind the shoulder with a narrow grab shell; the money-bag FBX appeared white.

The V2 backpack mount is now closer to the player and its grab shell is
larger. Mounted trigger objects are included in both direct-hand queries.
V2 pouches explicitly use the authored `money_bag_00` material. Daran's
counter now builds a supported payout tray on the player's side, marks the
payment spot with the amount due, and marks an outstanding pouch for pickup.
The handshake can settle a ready contract or re-present its existing stable-ID
payout; it does not create another payment. `PuebloMainPlayValidation` passed
after the follow-up on 2026-11-01. It verified the wider V2 backpack grab
shell, both direct-hand trigger settings, the V2 purse material reference,
marked payout tray, mission handshake, one 30-Zek payout and pouch merge,
the payment marker, a 10-Zek purse debit, and Pueblo reload. Report:
`Temp/ZandeaPueblo.main-validation.txt`. Physical reach, material appearance,
and payout pickup in Quest 2 still need a headset check.

## Follow-up — 2026-11-01

The V2 backpack mount was raised by 0.20 m. Daran's hand pose now follows the
armed handshake even after dialogue closes. Either controller can initiate a
sideways wave without a strict palm-angle gate; a free hand and deliberate
direction change are still required. Bandit outfit reuse reactivates inactive
visuals, and inventory refreshes once after streaming activation. Authored
bandit outfits and spawned weapons now resolve priced item definitions from the
runtime catalog, and pulled clothing gets a `TradeItem` on the physical drop.
The Bo Staff item definition's prefab reference was repaired. The trade catalog
validator passed with 20 weapons and 46 clothing/armor appearances and no
linkage issues. These changes still need an in-headset interaction check.

## Live handshake follow-up — 2026-11-02

During a Quest 2 session, the Editor log recorded three delivered barrels and
`DaranContract.Settle()` completing a 30-Zek payout. The player could not grip
Daran's hand. `HandshakeTarget` used a separate hardcoded `gripPressed` action,
while the controllers grab through the XR direct interactor's configured select
input. The handshake now reads that interactor input, accepts either assigned
hand within 0.42 m of the offered hand, and temporarily reserves the free hand
against nearby stock grabs. The current Play session keeps its old loaded code;
Unity must reload scripts before the interaction can be verified in headset.

The player ended Play Mode and the updated scripts compiled. The 2026-11-02
`Temp/ZandeaPueblo.main-validation.txt` run passed the Main payout, repeated
settlement, physical pouch merge, debt, merchant trade, and Pueblo streaming
checks. `HandshakeTarget` now uses the XR direct interactor's select input and
lets an uncollected pouch be presented again. Asking Daran about the job also
re-presents an existing payout. Normal merchant pickup and market calls are
suppressed while the hauling contract is active or its payout/debt remains;
after leaving Daran's merchant area, market calls wait at least 30 seconds.
Quest 2 physical grip and spoken-line timing remain manual checks.
