using System;
using System.Linq;
using JunkerBody;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.AI;
using VRKoombar.WeaponsStaging.Contracts;
using Zandea.Opening.Capture;
using Zandea.POI;

namespace Zandea.Opening.Editor
{
    [InitializeOnLoad]
    public static class OpeningPlayValidation
    {
        const string Mode = "Zandea.Opening.Validation.Mode";
        static int cycle;
        static double next;
        static double wallDeadline;
        static float gameDeadline;
        static PlayerKnockout knockout;
        static VRBodyController body;
        static ContinuousMoveProvider move;
        static bool originalMove;
        static int started, unconscious, recovering, recovered;
        static float initialHealth;
        static bool initialized;
        static ScavengerNPC.ScavengerPlayerHealth playerHealth;
        static OpeningCaptureGroup capture;
        static OpeningBindingAction binder;
        static PlayerWristRestraints restraints;
        static int deaths;
        static OpeningPrisonerEscort escort;
        static bool escortMode;
        static int arrivals;
        static PlayerSurrenderGesture gesture;
        static int confirms, cancellations;
        static float gestureStageAt;
        static OpeningDirector openingDirector;
        static int resolutions, completions;
        static bool victoryMode, captureMode, titleSeen;

        static OpeningPlayValidation() => EditorApplication.update += Tick;

        public static void RunKnockoutCycles() => Run("knockout");
        public static void RunEncounterSmoke() => Run("encounter");
        public static void RunNonlethalSmoke() => Run("nonlethal");
        public static void RunCaptureSmoke() => Run("capture");
        public static void RunEscortSmoke() => Run("escort");
        public static void RunGestureSmoke() => Run("gesture");
        public static void RunEscapeRouteSmoke() => Run("escape-route");
        public static void RunVictoryRouteSmoke() => Run("victory-route");
        public static void RunCapturedTitleSmoke() => Run("captured-title");
        public static void RunAggressionSmoke() => Run("aggression");
        public static void RunMainSmoke() => Run("main");

        static void Run(string mode)
        {
            EditorSceneManager.OpenScene(mode == "main" ? OpeningMainInstaller.MainPath : OpeningHarnessBuilder.ScenePath);
            EditorPrefs.SetString(Mode, mode);
            initialized = false;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            var mode = EditorPrefs.GetString(Mode, "");
            if (string.IsNullOrEmpty(mode) || !EditorApplication.isPlaying) return;
            try
            {
                if (!initialized)
                {
                    initialized = true;
                    var mainDirector = mode == "main" ? UnityEngine.Object.FindFirstObjectByType<OpeningDirector>() : null;
                    body = mainDirector ? mainDirector.player : UnityEngine.Object.FindFirstObjectByType<VRBodyController>();
                    knockout = mainDirector ? mainDirector.knockout : UnityEngine.Object.FindFirstObjectByType<PlayerKnockout>();
                    if (!body || !knockout || !body.hmd || !body.leftController || !body.rightController)
                        throw new InvalidOperationException("Player XR/KO references missing in Play mode");
                    move = body.GetComponentInChildren<ContinuousMoveProvider>(true);
                    originalMove = move && move.enabled;
                    playerHealth = mainDirector ? mainDirector.playerHealth : body.GetComponentInParent<ScavengerNPC.ScavengerPlayerHealth>();
                    if (!playerHealth) playerHealth = body.GetComponentInChildren<ScavengerNPC.ScavengerPlayerHealth>(true);
                    if (!playerHealth) throw new InvalidOperationException("Player health missing");
                    if (mode == "knockout" || mode == "nonlethal" || mode == "capture" || mode == "escort" || mode == "gesture" ||
                        mode == "escape-route" || mode == "victory-route" || mode == "captured-title")
                    {
                        var director = UnityEngine.Object.FindFirstObjectByType<OpeningDirector>();
                        if (director && mode != "escape-route" && mode != "victory-route" && mode != "captured-title") director.enabled = false;
                        var flow = UnityEngine.Object.FindFirstObjectByType<OpeningFlow>();
                        if (flow) flow.enabled = false;
                        var ultimatum = UnityEngine.Object.FindFirstObjectByType<SlaverUltimatum>();
                        if (ultimatum) ultimatum.enabled = false;
                    }
                    if (mode == "knockout" || mode == "nonlethal" || mode == "gesture" ||
                        mode == "escape-route" || mode == "victory-route" || mode == "captured-title")
                    {
                        var group = UnityEngine.Object.FindFirstObjectByType<BanditGroup>();
                        if (group) group.enabled = false;
                        foreach (var bandit in UnityEngine.Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
                            bandit.enabled = false;
                    }
                    if (mode == "knockout")
                    {
                        knockout.KnockoutStarted += _ => started++;
                        knockout.Unconscious += () => unconscious++;
                        knockout.RecoveryStarted += () => recovering++;
                        knockout.Recovered += () => recovered++;
                        cycle = 0;
                        StartCycle();
                    }
                    else if (mode == "nonlethal")
                    {
                        deaths = 0;
                        playerHealth.onDeath.AddListener(() => deaths++);
                        playerHealth.Restore();
                        playerHealth.knockoutTrauma = 1f;
                        playerHealth.ApplyDamage(new DamagePacket {
                            BaseDamage = 200f, DamageType = DamageType.Kinetic,
                            PhysicalImpact = PhysicalImpactKind.Blunt, Intent = DamageIntent.Nonlethal,
                            HitPointWorld = playerHealth.transform.position + Vector3.up
                        });
                        if (playerHealth.IsDead || playerHealth.Current < 1f || deaths != 0 ||
                            knockout.State != PlayerConsciousness.KnockoutStarting)
                            throw new InvalidOperationException("Nonlethal hit caused death or missed KO");
                        next = EditorApplication.timeSinceStartup + .7d;
                        gameDeadline = Time.time + .7f;
                        wallDeadline = EditorApplication.timeSinceStartup + 15d;
                    }
                    else if (mode == "capture" || mode == "escort")
                    {
                        escortMode = mode == "escort";
                        capture = UnityEngine.Object.FindFirstObjectByType<OpeningCaptureGroup>();
                        binder = UnityEngine.Object.FindFirstObjectByType<OpeningBindingAction>();
                        restraints = UnityEngine.Object.FindFirstObjectByType<PlayerWristRestraints>();
                        escort = UnityEngine.Object.FindFirstObjectByType<OpeningPrisonerEscort>();
                        if (!capture || !binder || !restraints || !escort) throw new InvalidOperationException("Capture components missing");
                        escort.DestinationReached += () => arrivals++;
                        cycle = 0;
                        next = EditorApplication.timeSinceStartup + 1d;
                    }
                    else if (mode == "gesture")
                    {
                        gesture = UnityEngine.Object.FindFirstObjectByType<PlayerSurrenderGesture>();
                        if (!gesture) throw new InvalidOperationException("Surrender gesture missing");
                        gesture.requireTrackedDevices = false;
                        gesture.confirmationSeconds = .25f;
                        gesture.cancellationGraceSeconds = 0f;
                        gesture.retryCooldownSeconds = 0f;
                        gesture.SurrenderConfirmed += () => confirms++;
                        gesture.SurrenderCancelled += _ => cancellations++;
                        gestureStageAt = Time.time;
                        cycle = 0;
                    }
                    else if (mode == "escape-route" || mode == "victory-route" || mode == "captured-title")
                    {
                        victoryMode = mode == "victory-route";
                        captureMode = mode == "captured-title";
                        titleSeen = false;
                        openingDirector = UnityEngine.Object.FindFirstObjectByType<OpeningDirector>();
                        if (!openingDirector || !openingDirector.presentation)
                            throw new InvalidOperationException("Opening director or title missing");
                        openingDirector.presentation.fadeToBlackSeconds = .1f;
                        openingDirector.presentation.titleHoldSeconds = .1f;
                        openingDirector.presentation.fadeTitleSeconds = .1f;
                        openingDirector.presentation.fadeBackSeconds = .1f;
                        if (!openingDirector.presentation.TitleRendersInFront)
                            throw new InvalidOperationException("Welcome title is behind its blackout panel");
                        openingDirector.Resolved += _ => resolutions++;
                        openingDirector.onOpeningCompleted.AddListener(() => completions++);
                        cycle = 0;
                        gameDeadline = Time.time + .5f;
                        wallDeadline = EditorApplication.timeSinceStartup + 15d;
                    }
                    else if (mode == "main")
                    {
                        openingDirector = UnityEngine.Object.FindFirstObjectByType<OpeningDirector>();
                        if (!openingDirector) throw new InvalidOperationException("Main opening director missing");
                        gameDeadline = Time.time + 5f;
                        wallDeadline = EditorApplication.timeSinceStartup + 45d;
                    }
                    else if (mode == "aggression")
                    {
                        openingDirector = UnityEngine.Object.FindFirstObjectByType<OpeningDirector>();
                        if (!openingDirector || !openingDirector.ultimatum)
                            throw new InvalidOperationException("Ultimatum missing");
                        gameDeadline = Time.time + openingDirector.awakeningSeconds + .4f;
                        wallDeadline = EditorApplication.timeSinceStartup + 20d;
                    }
                    else
                    {
                        var health = body.GetComponentInParent<ScavengerNPC.ScavengerPlayerHealth>();
                        if (!health) health = body.GetComponentInChildren<ScavengerNPC.ScavengerPlayerHealth>(true);
                        if (!health) throw new InvalidOperationException("Player health missing");
                        initialHealth = health.Current;
                        next = EditorApplication.timeSinceStartup + 25d;
                    }
                }
                if (mode == "knockout") TickKnockout();
                else if (mode == "nonlethal") TickNonlethal();
                else if (mode == "capture" || mode == "escort") TickCapture();
                else if (mode == "gesture") TickGesture();
                else if (mode == "escape-route" || mode == "victory-route" || mode == "captured-title") TickRoute();
                else if (mode == "main") TickMain();
                else if (mode == "aggression") TickAggression();
                else if (EditorApplication.timeSinceStartup >= next) FinishEncounter();
            }
            catch (Exception error)
            {
                Debug.LogError("OPENING_PLAY_VALIDATION_FAIL " + error);
                Finish(false);
            }
        }

        static void StartCycle()
        {
            if (!knockout.RequestKnockout("validation", true) || knockout.RequestKnockout("duplicate", true))
                throw new InvalidOperationException("KO request idempotence failed");
            if (move && move.enabled) throw new InvalidOperationException("Movement stayed enabled during KO");
            if (!body.hmd.gameObject.activeInHierarchy || !body.leftController.gameObject.activeInHierarchy ||
                !body.rightController.gameObject.activeInHierarchy)
                throw new InvalidOperationException("XR tracking objects disabled during KO");
            next = EditorApplication.timeSinceStartup + .7d;
            wallDeadline = EditorApplication.timeSinceStartup + 15d;
            gameDeadline = Time.time + .7f;
        }

        static void TickKnockout()
        {
            if (Time.time < gameDeadline && EditorApplication.timeSinceStartup < wallDeadline) return;
            if (knockout.State == PlayerConsciousness.Unconscious)
            {
                if (!knockout.Recover()) throw new InvalidOperationException("Recovery request failed");
                next = EditorApplication.timeSinceStartup + .8d;
                wallDeadline = EditorApplication.timeSinceStartup + 15d;
                gameDeadline = Time.time + .8f;
                return;
            }
            if (knockout.State != PlayerConsciousness.Conscious)
                throw new InvalidOperationException("KO state stalled: " + knockout.State +
                    " gameTime=" + Time.time + " paused=" + EditorApplication.isPaused);
            if (move && move.enabled != originalMove)
                throw new InvalidOperationException("Movement prior state was not restored");
            cycle++;
            if (cycle < 3) { StartCycle(); return; }
            if (started != 3 || unconscious != 3 || recovering != 3 || recovered != 3)
                throw new InvalidOperationException($"KO event counts {started}/{unconscious}/{recovering}/{recovered}");
            Debug.Log("OPENING_KNOCKOUT_CYCLES_PASS cycles=3");
            Finish(true);
        }

        static void FinishEncounter()
        {
            var health = body.GetComponentInParent<ScavengerNPC.ScavengerPlayerHealth>();
            if (!health) health = body.GetComponentInChildren<ScavengerNPC.ScavengerPlayerHealth>(true);
            var bandits = UnityEngine.Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None);
            var contacts = bandits.Sum(b => b.GetComponent<BanditDuelStrikeDamage>()?.ContactHits ?? 0);
            var overlaps = bandits.Sum(b => b.GetComponent<BanditDuelStrikeDamage>()?.TipOverlaps ?? 0);
            Debug.Log($"OPENING_ENCOUNTER_SMOKE contacts={contacts} overlaps={overlaps} playerHealth={health.Current:F1}/{initialHealth:F1} " +
                "states=" + string.Join(",", bandits.Select(b => b.state.ToString())));
            Finish(contacts > 0 && health.Current < initialHealth);
        }

        static void TickNonlethal()
        {
            if (Time.time < gameDeadline && EditorApplication.timeSinceStartup < wallDeadline) return;
            if (knockout.State != PlayerConsciousness.Unconscious)
                throw new InvalidOperationException("Nonlethal KO did not reach unconscious state: " + knockout.State);
            playerHealth.Restore();
            knockout.Recover();
            playerHealth.ApplyDamage(new DamagePacket {
                BaseDamage = 200f, DamageType = DamageType.Kinetic,
                PhysicalImpact = PhysicalImpactKind.Sharp, Intent = DamageIntent.Lethal,
                HitPointWorld = playerHealth.transform.position + Vector3.up
            });
            if (!playerHealth.IsDead || deaths != 1)
                throw new InvalidOperationException("Ordinary lethal damage did not trigger one death");
            Debug.Log("OPENING_NONLETHAL_LETHAL_PASS");
            Finish(true);
        }

        static void TickCapture()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            if (cycle == 0)
            {
                capture.SetCompliance(true);
                if (!capture.BeginCapture() || !capture.Captor)
                    throw new InvalidOperationException("Captor assignment failed");
                var captor = capture.Captor;
                var left = body.leftOpticalWrist ? body.leftOpticalWrist.position : body.leftController.position;
                var right = body.rightOpticalWrist ? body.rightOpticalWrist.position : body.rightController.position;
                var midpoint = (left + right) * .5f;
                captor.transform.position = midpoint - captor.transform.up * 1.25f - captor.transform.forward * .35f;
                binder.bindSeconds = .1f;
                if (!binder.Begin()) throw new InvalidOperationException("Reach-checked binding did not start");
                cycle = 1;
                next = EditorApplication.timeSinceStartup + .5d;
                return;
            }
            if (!restraints.IsRestrained) throw new InvalidOperationException("Binding did not apply restraints");
            if (body.xrOrigin.GetComponentsInChildren<XRDirectInteractor>(true).Any(hand => hand.enabled))
                throw new InvalidOperationException("Bound player can still grab");
            if (move && !move.enabled) throw new InvalidOperationException("Binding disabled voluntary locomotion");
            if (escortMode)
            {
                if (cycle == 1)
                {
                    if (!NavMesh.SamplePosition(body.transform.position, out var hit, 3f, NavMesh.AllAreas))
                        throw new InvalidOperationException("Escort destination has no NavMesh");
                    if (!capture.Captor.agent.Warp(hit.position))
                        throw new InvalidOperationException("Captor could not reach escort NavMesh");
                    escort.destination.position = hit.position;
                    if (!escort.Begin()) throw new InvalidOperationException("Escort did not begin");
                    cycle = 2;
                    next = EditorApplication.timeSinceStartup + 1d;
                    return;
                }
                if (escort.State != EscortState.Completed || arrivals != 1)
                    throw new InvalidOperationException("Escort arrival did not resolve once: " + escort.State + " events=" + arrivals);
                Debug.Log("OPENING_ESCORT_ARRIVAL_PASS");
                Finish(true);
                return;
            }
            if (!restraints.RemoveRestraints()) throw new InvalidOperationException("Restraint removal failed");
            Debug.Log("OPENING_CAPTURE_BIND_PASS");
            Finish(true);
        }

        static void TickGesture()
        {
            var head = body.hmd;
            body.leftController.position = head.position - head.right * .28f +
                Vector3.up * (cycle == 1 ? .65f : .02f);
            body.rightController.position = head.position + head.right * .28f +
                Vector3.up * (cycle == 0 || cycle == 2 ? -.5f : .65f);
            if (Time.time - gestureStageAt < .6f) return;
            if (cycle == 0)
            {
                if (gesture.State != SurrenderState.NotSurrendering || confirms != 0)
                    throw new InvalidOperationException("One-hand raise falsely surrendered");
                cycle = 1; gestureStageAt = Time.time;
            }
            else if (cycle == 1)
            {
                if (gesture.State != SurrenderState.Surrendering || confirms != 1)
                    throw new InvalidOperationException("Fully raised two-hand pose did not confirm surrender: " + gesture.State);
                cycle = 2; gestureStageAt = Time.time;
            }
            else
            {
                if (gesture.State != SurrenderState.NotSurrendering || cancellations != 1)
                    throw new InvalidOperationException("Lowering hand did not cancel surrender");
                Debug.Log("OPENING_GESTURE_PASS oneHand/overheadTwoHands/cancel");
                Finish(true);
            }
        }

        static void TickRoute()
        {
            titleSeen |= openingDirector.presentation.TitleVisible;
            if (Time.time < gameDeadline && EditorApplication.timeSinceStartup < wallDeadline) return;
            if (cycle == 0)
            {
                if (openingDirector.Phase == OpeningPhase.Dormant)
                    throw new InvalidOperationException("Director never started");
                if (captureMode)
                {
                    if (!knockout.RequestKnockout("captured route validation", true))
                        throw new InvalidOperationException("Captured route KO did not start");
                }
                else if (victoryMode)
                {
                    foreach (var member in openingDirector.slavers) member.health.ForceKnockout();
                }
                else if (!openingDirector.Resolve(OpeningOutcome.Escaped) || openingDirector.Resolve(OpeningOutcome.Escaped))
                    throw new InvalidOperationException("Escape resolution was not idempotent");
                cycle = 1;
                gameDeadline = Time.time + (victoryMode || captureMode ? 3f : 1.5f);
                wallDeadline = EditorApplication.timeSinceStartup + 15d;
                return;
            }
            var expected = captureMode ? OpeningOutcome.CapturedUnconscious :
                victoryMode ? OpeningOutcome.SlaversDefeated : OpeningOutcome.Escaped;
            if (openingDirector.Outcome != expected || openingDirector.Phase != OpeningPhase.Completed ||
                resolutions != 1 || completions != 1)
                throw new InvalidOperationException($"Route failed: outcome={openingDirector.Outcome} phase={openingDirector.Phase} events={resolutions}/{completions}");
            if (!titleSeen || openingDirector.presentation.BlackoutOpacity > .1f)
                throw new InvalidOperationException("Welcome title did not appear and fade back to the scene");
            if (captureMode && knockout.State != PlayerConsciousness.Conscious)
                throw new InvalidOperationException("Captured player remained fainted after title: " + knockout.State);
            Debug.Log(captureMode ? "OPENING_CAPTURED_TITLE_RECOVERY_PASS" :
                victoryMode ? "OPENING_VICTORY_ROUTE_PASS" : "OPENING_ESCAPE_ROUTE_PASS");
            Finish(true);
        }

        static void TickAggression()
        {
            if (Time.time < gameDeadline && EditorApplication.timeSinceStartup < wallDeadline) return;
            var ultimatum = openingDirector.ultimatum;
            if (!ultimatum.IsDemanding || !ultimatum.leader.defensiveOnly ||
                ultimatum.guards.Any(guard => !guard.defensiveOnly))
                throw new InvalidOperationException("Opening demand did not start");
            var guardHealth = ultimatum.guards[0].health;
            guardHealth.ApplyDamage(new DamagePacket {
                BaseDamage = 1f, DamageType = DamageType.Kinetic,
                PhysicalImpact = PhysicalImpactKind.Blunt, Intent = DamageIntent.Lethal,
                HitPointWorld = guardHealth.transform.position + Vector3.up
            });
            if (ultimatum.Result != UltimatumResult.Combat || ultimatum.leader.defensiveOnly ||
                ultimatum.guards.Any(guard => guard.defensiveOnly))
                throw new InvalidOperationException("A hit did not release all slavers into combat");
            Debug.Log("OPENING_AGGRESSION_COMBAT_PASS");
            Finish(true);
        }

        static void TickMain()
        {
            if (Time.time < gameDeadline && EditorApplication.timeSinceStartup < wallDeadline) return;
            var slavers = openingDirector.slavers;
            if (openingDirector.Phase == OpeningPhase.Dormant || !openingDirector.gameObject.activeInHierarchy ||
                slavers == null || slavers.Length != 3 || slavers.Any(member => !member || !member.gameObject.activeInHierarchy))
                throw new InvalidOperationException("Main opening failed to activate");
            var kareth = UnityEngine.Object.FindObjectsByType<BanditOutpost>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .SingleOrDefault(poi => poi.poiId.StartsWith("zandea.pueblo.", StringComparison.Ordinal));
            var city = kareth ? kareth.transform.Find("Layout Preview") : null;
            int visibleCityRenderers = city ? city.GetComponentsInChildren<Renderer>(false)
                .Count(renderer => renderer && renderer.enabled && renderer.gameObject.activeInHierarchy) : 0;
            if (!kareth || kareth.name != "Kareth" || !kareth.holdAuthoredPreviewAtStart ||
                !city || visibleCityRenderers < 50 || Vector3.Distance(kareth.transform.position, city.position) > .01f)
                throw new InvalidOperationException("Kareth did not stay visible at its saved scene position");
            Debug.Log($"OPENING_KARETH_STATIC_PASS renderers={visibleCityRenderers} position={city.position}");
            var nav = string.Join(",", slavers.Select(member => member.agent && member.agent.isOnNavMesh));
            var ground = Terrain.activeTerrains
                .Where(t => t && t.terrainData)
                .Select(t => new { t, p = openingDirector.player.transform.position })
                .Where(x => x.p.x >= x.t.transform.position.x && x.p.x <= x.t.transform.position.x + x.t.terrainData.size.x &&
                            x.p.z >= x.t.transform.position.z && x.p.z <= x.t.transform.position.z + x.t.terrainData.size.z)
                .Select(x => x.t.SampleHeight(x.p) + x.t.transform.position.y).DefaultIfEmpty(float.NaN).First();
            Debug.Log($"OPENING_MAIN_GEOMETRY player={openingDirector.player.transform.position} ground={ground:F2} " +
                "slavers=" + string.Join(",", slavers.Select(member => member.transform.position.ToString("F2"))) +
                " sample=" + string.Join(",", slavers.Select(member => NavMesh.SamplePosition(member.transform.position, out var hit, 200f, NavMesh.AllAreas) ? hit.position.ToString("F2") : "none")));
            Debug.Log($"OPENING_MAIN_SMOKE phase={openingDirector.Phase} nav={nav} " +
                "states=" + string.Join(",", slavers.Select(member => member.state.ToString())));
            Finish(slavers.All(member => member.agent && member.agent.isOnNavMesh));
        }

        static void Finish(bool success)
        {
            EditorPrefs.DeleteKey(Mode);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
