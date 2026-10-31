# Daran: An Honest Day's Work voice integration

The merchant dialogue asset is `Assets/Zandea/FriendlyNPC/Generated/DaranDialogue.asset`; its entry greeting and trade branch remain in place. The hauling mission is authored by `HonestWorkAuthoring` and tracked by `MissionSessions`. `DaranContract` chooses recorded dialogue from that state. Barrel delivery and breakage update mission state before any optional speech. The vendor counter owns payouts and debt collection. `FriendlyNpcInteraction` continues to play all voice, subtitles, facial emotions, and speech mouth envelopes through Daran's existing components.

## Recordings and import

The source is `C:\Users\green\Downloads\Audio\DaranMerchant`. It contained 44 files on 31 October 2026: 26 new `daran_job_*.mp3` mission recordings and 18 older merchant recordings. The request listed `.wav` names, but the actual 26 files are MP3. All 26 named stems were present, with no identical files by SHA-256. The mission recordings are 44.1 kHz mono, 2.69–12.20 seconds. Originals are copied unchanged to `Assets/Audio/Dialogue/Daran/Missions/AnHonestDaysWork/`. Unity imports them compressed in memory as Vorbis at quality 0.8 and bakes a 30 Hz mouth amplitude envelope. Runtime playback is offline and spatial.

Use **Zandea > Missions > Import Daran mission recordings** after changing a source recording. The Editor importer refuses to overwrite a different existing asset; replace an intentional retake in the project only after preserving the old file and its `.meta` GUID. It reports missing recordings by exact stem, updates the existing dialogue asset and voice bank, and checks that the four baseline merchant voice events remain. It never changes the source Downloads directory. The importer requires at least 20 GiB free on D:.

Subtitles use the owner's approved 26-line transcript where it matches the recording. The subtitle text is stored in both dialogue nodes and voice bank takes; ElevenLabs performance tags are omitted. The imported clips were not trimmed, normalized, or re-encoded at source. **Recording mismatch:** `daran_job_debt_reminder.mp3` is a different take from `daran_job_debt_request.mp3`, but both local Whisper tiny and base transcribed the same debt-request sentence from it. Its subtitle therefore matches the audible debt-request wording. Replace that source recording with the approved reminder line when a corrected take is available.

## Event map

Every ID below is the basename of a source MP3 and a node in the existing Daran dialogue asset.

| IDs | Trigger |
| --- | --- |
| `daran_job_offer`, `daran_job_payment`, `daran_job_directions` | Work choice while available; player requests terms or directions. |
| `daran_job_accept`, `daran_job_decline` | Player accepts or declines. Only the accept action activates the contract. |
| `daran_job_no_progress`, `daran_job_one_delivered`, `daran_job_two_delivered`, `daran_job_barrel_broken` | Current mission state or an observed nearby milestone. Each delivery milestone and breakage count is acknowledged once. |
| `daran_job_encouragement`, `daran_job_careful` | Nearby barrel pickup or fast release, with a 60 second cooldown per line. |
| `daran_job_turnin`, `daran_job_incomplete` | Ready to turn in; unfinished turn-in attempt. |
| `daran_job_perfect`, `daran_job_two_intact`, `daran_job_one_intact`, `daran_job_all_broken` | Terminal results 3/0, 2/1, 1/2, 0/3 intact/broken. Net Zeks: +30, +10, −10, −30. |
| `daran_job_payout`, `daran_job_payment_complete` | Physical payout placed; zero balance or payout actually collected. |
| `daran_job_debt_request`, `daran_job_insufficient_funds`, `daran_job_debt_reminder`, `daran_job_debt_paid` | Negative result; failed payment; return with debt; successful payment using personal purse on counter payment spot. |
| `daran_job_abandon`, `daran_job_completed_return`, `daran_job_goodbye` | Abandonment, settled return, or mission farewell. |

The outcome node completes before the payout or debt node starts. Audio completion and existing fallback timing drive dialogue advancement. A missing clip leaves subtitles and mission state functional. Spontaneous reactions require the player and event to be within 7–9 m of Daran and do not interrupt an active conversation or another spoken line. Missed reactions are represented by current mission state when the player returns; they are not queued as a backlog.

Payout uses the existing vendor counter's unique `contract-<missionId>` pouch. Repeated settlement cannot create a second pouch. Debt payment requires the personal purse on the counter payment spot and clears debt only after `TryContractDebtPayment` succeeds. Abandonment retains the full penalty for barrels already broken and forfeits delivery earnings. Mission and merchant balances survive POI unload within one play session; no cross-launch save system exists yet.

## Validation

With Main open, use **Zandea > Missions > Validate Daran mission in Main** for an Editor check of all 26 dialogue/clip/bank references, preservation of baseline merchant voice, branching, all four terminal outcomes, incomplete turn-in, abandonment debt, and the Main Pueblo scene references. The command will not switch away from another open scene. `DaranMissionTests` also contains focused NUnit tests; in this project's predefined Editor assembly they may not be discovered by Unity's Test Runner. Headset checks remain necessary for audibility, mouth motion, subtitle placement, conversational timing, and physical purse placement. No headset result is implied by Editor validation.

On 31 October 2026, Unity batch import completed with 26 clip mappings and baked envelopes. The live project's scripts compiled in the Editor. A read-only serialized check passed for the 26 source-identical clips, their GUIDs, subtitles, envelopes, dialogue branches, baseline merchant voices, outcome transitions, and all three Main contract-barrel identities. The Unity Test Runner wrote no result file. A separate isolated scene-validation attempt was interrupted when its temporary project copy disappeared during compilation; the Main menu validator therefore has not yet been run. No headset validation has been performed.
