# Trade Catalog V2

Physical trade items prepared for Daran. Prices are integer Zeks. The serialized `valueCats` field is retained for compatibility; its numeric value is unchanged on existing definitions.

The original working weapon and rag garment prefabs remain in place. Modified weapons and rag garments have separate V1 and V2 prefab assets with distinct GUIDs. New clothing and armor loot prefabs were baked from the imported wardrobe visuals; the wardrobe prefabs were not edited. V2 prefabs have `TradeItem`, a positive price definition, `XRGrabInteractable`, and `Rigidbody`.

These are saleable to Daran when the V2 physical object is offered on his Sell pad. Daran currently stocks only his existing sword, bo staff, and bandage in Main. The full V2 catalogue is not stocked there, and existing world references to unpriced V1 weapons are not promoted. Promotion requires the project owner to review the V2 versions.

## Prices

| Category | Item | Zeks | Definition |
|---|---|---:|---|
| Weapon | Broad Cleaver | 110 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/broad-cleaver.asset` |
| Weapon | Curved Cleaver | 110 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/curved-cleaver.asset` |
| Weapon | Heavy Cleaver | 150 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/heavy-cleaver.asset` |
| Weapon | Hook Sword | 110 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/hook-sword.asset` |
| Weapon | JKR-88 1 | 320 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/jkr-88-1.asset` |
| Weapon | Long Blade | 110 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/long-blade.asset` |
| Weapon | Player practice sword - generated long blade | 80 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/player-practice-sword---generated-long-blade.asset` |
| Weapon | Pole Blade | 150 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/pole-blade.asset` |
| Weapon | Rusted Double Axe | 180 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/rusted-double-axe.asset` |
| Weapon | RustFang | 320 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/rustfang.asset` |
| Weapon | Saw Cleaver | 110 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/saw-cleaver.asset` |
| Weapon | Shield And Saber | 110 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/shield-and-saber.asset` |
| Weapon | Short Saber | 110 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/short-saber.asset` |
| Weapon | Spiked Double Axe | 180 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/spiked-double-axe.asset` |
| Weapon | Spiked Mace | 110 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/spiked-mace.asset` |
| Clothing/armor | armored torso | 110 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/armored-torso.asset` |
| Clothing/armor | bandit armored boot | 90 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-armored-boot.asset` |
| Clothing/armor | bandit armored pants | 110 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-armored-pants.asset` |
| Clothing/armor | bandit armored skirt | 110 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-armored-skirt.asset` |
| Clothing/armor | bandit barred helmet | 75 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-barred-helmet.asset` |
| Clothing/armor | bandit black horn helmet | 85 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-black-horn-helmet.asset` |
| Clothing/armor | bandit crusader helmet | 160 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-crusader-helmet.asset` |
| Clothing/armor | bandit desert glove | 28 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-desert-glove.asset` |
| Clothing/armor | bandit head wrap | 16 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-head-wrap.asset` |
| Clothing/armor | bandit horned hood | 40 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-horned-hood.asset` |
| Clothing/armor | bandit long coat | 70 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-long-coat.asset` |
| Clothing/armor | bandit mercenary chest | 125 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-mercenary-chest.asset` |
| Clothing/armor | bandit rib outfit | 60 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-rib-outfit.asset` |
| Clothing/armor | bandit rib vest | 45 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-rib-vest.asset` |
| Clothing/armor | bandit rust gauntlet | 60 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-rust-gauntlet.asset` |
| Clothing/armor | bandit rust shoulder | 65 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-rust-shoulder.asset` |
| Clothing/armor | bandit samurai helmet | 160 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-samurai-helmet.asset` |
| Clothing/armor | bandit shoulder pad | 45 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-shoulder-pad.asset` |
| Clothing/armor | bandit spiked helmet | 90 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-spiked-helmet.asset` |
| Clothing/armor | bandit spiked hood | 45 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-spiked-hood.asset` |
| Clothing/armor | bandit straw hat a | 14 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-straw-hat-a.asset` |
| Clothing/armor | bandit straw hat b | 14 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bandit-straw-hat-b.asset` |
| Clothing/armor | black guard helmet | 115 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/black-guard-helmet.asset` |
| Clothing/armor | bronze spiked helmet | 105 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/bronze-spiked-helmet.asset` |
| Clothing/armor | desert hood a | 28 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/desert-hood-a.asset` |
| Clothing/armor | desert hood b | 28 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/desert-hood-b.asset` |
| Clothing/armor | leather glove a | 35 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/leather-glove-a.asset` |
| Clothing/armor | leather glove b | 35 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/leather-glove-b.asset` |
| Clothing/armor | paladin armored boot | 160 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/paladin-armored-boot.asset` |
| Clothing/armor | paladin gauntlet | 160 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/paladin-gauntlet.asset` |
| Clothing/armor | paladin medieval chest | 160 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/paladin-medieval-chest.asset` |
| Clothing/armor | paladin metal shoulder | 160 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/paladin-metal-shoulder.asset` |
| Clothing/armor | paladin ragged skirt | 45 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/paladin-ragged-skirt.asset` |
| Clothing/armor | paladin winged helmet | 160 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/paladin-winged-helmet.asset` |
| Clothing/armor | Rag Pants | 9 | `Assets/MeleeCombat/Inventory/Rag Pants.asset` |
| Clothing/armor | Rag Shirt | 8 | `Assets/MeleeCombat/Inventory/Rag Shirt.asset` |
| Clothing/armor | ragged skirt b | 18 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/ragged-skirt-b.asset` |
| Clothing/armor | ragged skirt c | 18 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/ragged-skirt-c.asset` |
| Clothing/armor | scavenger outfit | 22 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/scavenger-outfit.asset` |
| Clothing/armor | scavenger vest | 24 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/scavenger-vest.asset` |
| Clothing/armor | shoulder harness a | 65 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/shoulder-harness-a.asset` |
| Clothing/armor | shoulder harness b | 65 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/shoulder-harness-b.asset` |
| Clothing/armor | spiked helmet a | 90 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/spiked-helmet-a.asset` |
| Clothing/armor | spiked helmet b | 90 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/spiked-helmet-b.asset` |
| Clothing/armor | wrapped boot | 30 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/wrapped-boot.asset` |
| Clothing/armor | wrapped headgear | 35 | `Assets/Zandea/FriendlyNPC/TradeCatalogV2/Definitions/wrapped-headgear.asset` |

## Validation

- 15 new weapon definitions and 46 clothing/armor definitions listed here. Five weapons retained existing valid definitions and prices.
- Static GUID audit: 61 definitions point to physical prefabs whose `TradeItem` points back to the same definition; zero linkage failures.
- Unity reported `Trade Catalog V2: 20 weapons and 46 clothing/armor definitions checked.`
- The Unity trade catalog validator passed on 2026-11-01: 20 weapons, 46 clothing/armor appearances, zero linkage issues.

## Bandit drops

`ZandeaNpcWearables.asset` in `Assets/Resources` maps authored bandit clothing and weapon prefabs to these priced definitions. Rebuild it through **Zandea > Friendly NPC > Build Trade Catalog V2** after adding wearable or weapon definitions. Pulled clothing retains its NPC item identity and receives `TradeItem`; spawned bandit weapons resolve their missing `weaponItem` from this catalog. The existing world prefabs remain separate from the V2 review copies.
