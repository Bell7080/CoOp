#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using System.Text;
using CoOpHorror.Gameplay;
using CoOpHorror.Networking;
using CoOpHorror.Player;
using CoOpHorror.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CoOpHorror.Testing
{
    // End-to-end test driver. tools/e2e-elecmole.ps1 runs the DeveloperHostClient scenario (main Editor = host,
    // "Player 2" clone = client, separate processes over Relay) and calls these entry points with
    // `unity command script eval` in each process. Each step drives the LOCAL player through the real input
    // paths (automation hooks on PlayerInteractor / FirstPersonController / PlayerInventory): it walks along
    // NavMesh paths, aims, presses, holds and answers skill checks. Poll() reports progress; Snapshot()
    // prints the replicated game state for assertions. Development builds and the Editor only.
    public sealed class ElecMoleE2E : MonoBehaviour
    {
        private static ElecMoleE2E runner;
        private static string state = "idle";
        private static string current = string.Empty;
        private static string message = string.Empty;
        private static float startedAt;
        private static readonly StringBuilder Log = new();
        private float walkPitch = 5f;

        // ---------- entry points (script eval) ----------

        public static string Begin(string step, string arg = "")
        {
            if (state == "running") return "busy|" + current;
            if (runner == null)
            {
                var go = new GameObject("ElecMoleE2E") { hideFlags = HideFlags.HideInHierarchy };
                DontDestroyOnLoad(go);
                runner = go.AddComponent<ElecMoleE2E>();
            }
            current = step + (string.IsNullOrEmpty(arg) ? "" : " " + arg);
            state = "running";
            message = string.Empty;
            startedAt = Time.realtimeSinceStartup;
            runner.StartCoroutine(runner.Run(step, arg));
            return "started|" + current;
        }

        public static string Poll() => $"{state}|{current}|{Time.realtimeSinceStartup - startedAt:0.0}s|{message}";

        public static string TakeLog()
        {
            var text = Log.ToString();
            Log.Clear();
            return text;
        }

        public static string HostRoom()
        {
            var room = RoomController.Instance;
            if (room == null) return "no-room-controller";
            _ = room.HostAsync();
            return "hosting";
        }

        public static string JoinRoom(string code)
        {
            var room = RoomController.Instance;
            if (room == null) return "no-room-controller";
            _ = room.JoinAsync(code);
            return "joining";
        }

        public static string RoomStatus()
        {
            var room = RoomController.Instance;
            var nm = NetworkManager.Singleton;
            return room == null ? "none" : $"code={room.JoinCode}|connected={room.Connected}|host={room.IsHost}|players={room.PlayerCount}|busy={room.Busy}|status={room.Status}|listening={(nm != null && nm.IsListening)}";
        }

        public static bool SkillVisible() => Me != null && Me.Check.Visible;

        // Replicated state as key=value pairs.
        public static string Snapshot()
        {
            var sb = new StringBuilder();
            var grid = PowerGrid.Instance;
            sb.Append("grid=").Append(grid != null && grid.IsSpawned ? grid.Stored.Value.ToString("0") : "-");
            var stage = StageDirector.Instance;
            sb.Append(";stage=").Append(stage != null && stage.IsSpawned ? stage.Phase.Value.ToString() : "-");
            if (stage != null && stage.IsSpawned)
            {
                sb.Append(";earned=").Append(stage.Earned.Value);
                sb.Append(";aboard=").Append(Ids(stage.Aboard));
                sb.Append(";door=").Append(stage.ShipDoor != null && stage.ShipDoor.Open.Value ? "open" : "closed");
            }
            var tx = Transmitter.Instance;
            if (tx != null && tx.IsSpawned) sb.Append(";tx=").Append(tx.Phase.Value).Append(";plugged=").Append(tx.PluggedCount).Append('/').Append(tx.Required);
            var gen = Object.FindFirstObjectByType<Generator>();
            if (gen != null) sb.Append(";genStalled=").Append(gen.IsStalled);
            sb.Append(";cctv=").Append(string.Join(",", CctvCamera.All.Where(c => c.Online).Select(c => c.Order)));
            foreach (var a in Appliance.All.Where(a => a.IsSpawned).OrderBy(a => a.NameKey))
            {
                sb.Append(";").Append(a.NameKey.Replace("appliance.", "")).Append('=')
                  .Append(a.IsCharged ? "C" : "c").Append(a.Charge.Value.ToString("0"))
                  .Append(",v").Append(a.Value.Value).Append('/').Append(a.BaseValue)
                  .Append(a.IsStored ? ",stored" : "").Append(a.IsPlugged ? ",plugged" : "").Append(a.Swallowed.Value ? ",swallowed" : "")
                  .Append(a.IsHeld ? ",held" + a.Holders.Count : "")
                  .Append(a.Plug != null ? ",plug" + a.Plug.State.Value : "");
            }
            foreach (var p in PlayerHealth.All.OrderBy(p => p.OwnerClientId))
                sb.Append(";p").Append(p.OwnerClientId).Append("=hp").Append(p.Health.Value).Append(p.IsDead.Value ? ",dead" : "").Append(",rv").Append(p.ReviveCharge.Value)
                  .Append(stage != null && stage.InShip(p.transform.position) ? ",ship" : "")
                  .Append(p.TryGetComponent<FirstPersonController>(out var fpc) && fpc.IsTucked() ? ",tucked" : "");
            sb.Append(";lamps=").Append(InstalledDevice.Devices.Count(d => d is PowerLamp && d.Online));
            sb.Append(";ripped=").Append(InstalledDevice.Devices.Count(d => d.IsSpawned && d.Detached.Value));
            sb.Append(";corpses=").Append(Corpse.Bodies.Count(c => c != null && c.IsSpawned));
            foreach (var c in Corpse.Bodies.Where(c => c != null && c.IsSpawned))
                sb.Append(";corpse").Append(c.PlayerClientId.Value).Append(stage != null && stage.InShip(c.transform.position) ? "=ship" : "=out").Append(c.IsHeld ? ",held" : "");
            sb.Append(";monster=").Append(TempMonster.All.Any(m => m.Investigating) ? "investigating" : "idle").Append(TempMonster.Frozen ? ",frozen" : "");
            var me = Me;
            if (me != null)
            {
                sb.Append(";me=").Append(me.OwnerClientId).Append(";skillSeen=").Append(me.SkillChecksSeen).Append(";skillMissed=").Append(me.SkillChecksMissed);
                sb.Append(";spectating=").Append(FirstPersonController.SpectatingClient?.ToString() ?? "-");
                sb.Append(";cctvView=").Append(CctvView.Viewing).Append(CctvView.Instance != null ? ",map" + CctvView.Instance.MapButtonCount : "");
                var radio = me.GetComponent<PlayerRadio>();
                if (radio != null) sb.Append(";radio=").Append(radio.Mode.Value);
            }
            return sb.ToString();
        }

        // ---------- steps ----------

        private IEnumerator Run(string step, string arg)
        {
            Write("step " + step + " " + arg);
            IEnumerator body = step switch
            {
                "setup" => Setup(),
                "crank" => Crank(float.Parse(arg)),
                "crank-hold" => CrankHold(true),
                "crank-stop" => CrankHold(false),
                "skill-wait" => SkillWait(),
                "noise-test" => NoiseTest(),
                "door" => OpenDoor(),
                "power-cctv" => PowerCctv(int.Parse(arg)),
                "cctv-view" => CctvOpen(int.Parse(arg)),
                "cctv-exit" => CctvClose(),
                "activate-tx" => ActivateTransmitter(),
                "deliver" => Deliver(arg),
                "walk" => WalkStep(arg),
                "kill" => Kill(ulong.Parse(arg)),
                "revive-hit" => ReviveByHitting(ulong.Parse(arg)),
                "carry-corpse" => CarryCorpse(ulong.Parse(arg)),
                "launch" => Launch(),
                "wait-escape" => WaitEscape(),
                "radio" => Radio(arg),
                "spectate-check" => SpectateCheck(),
                "outside" => Outside(),
                "power-lamp" => PowerLamp(),
                "rip-cctv" => RipCctv(int.Parse(arg)),
                "hide" => Hide(),
                _ => Unknown(step),
            };
            while (true)
            {
                object next;
                try
                {
                    if (!body.MoveNext()) break;
                    next = body.Current;
                }
                catch (Exception e)
                {
                    Fail(e.GetType().Name + ": " + e.Message);
                    Release();
                    yield break;
                }
                if (state != "running") { Release(); yield break; }
                yield return next;
            }
            Release();
            if (state == "running") Ok(message.Length > 0 ? message : "done");
        }

        private IEnumerator Unknown(string step)
        {
            Fail("unknown step " + step);
            yield break;
        }

        private IEnumerator Setup()
        {
            if (Me == null) { Fail("no local player"); yield break; }
            Me.AutomationSkill = PlayerInteractor.SkillAutomation.Perfect;
            if (NetworkManager.Singleton.IsServer) TempMonster.Frozen = true;
            // Unfocused test windows drop the cursor lock, which brings the room panel over screenshots.
            FirstPersonController.SetCursorCaptured(true);
            message = "player " + Me.OwnerClientId + (NetworkManager.Singleton.IsServer ? " (host, monster frozen)" : " (client)");
        }

        private IEnumerator Crank(float amount)
        {
            var gen = Object.FindFirstObjectByType<Generator>();
            yield return WalkNear(gen, 1.3f);
            var seen = Me.SkillChecksSeen;
            yield return HoldUntil(gen, () => PowerGrid.Instance.Stored.Value >= amount, 90f, "grid >= " + amount);
            message = $"grid={PowerGrid.Instance.Stored.Value:0} skillChecks={Me.SkillChecksSeen - seen} missed={Me.SkillChecksMissed}";
        }

        // Keep cranking in the background while other steps (on the client) use power.
        private IEnumerator CrankHold(bool on)
        {
            var gen = Object.FindFirstObjectByType<Generator>();
            if (on)
            {
                yield return WalkNear(gen, 1.3f);
                yield return Face(gen);
                keepHolding = true;
                StartCoroutine(KeepCranking(gen));
                message = "cranking";
            }
            else
            {
                keepHolding = false;
                Me.AutomationHold = false;
                Me.AutomationTarget = null;
                message = "stopped at grid " + PowerGrid.Instance.Stored.Value.ToString("0");
            }
        }

        private static bool keepHolding;

        // Crank from a nearly empty grid until a skill check is on screen (left running for a screenshot).
        private IEnumerator SkillWait()
        {
            var gen = Object.FindFirstObjectByType<Generator>();
            PowerGrid.Instance.ServerTake(Mathf.Max(0f, PowerGrid.Instance.Stored.Value - 5f));
            yield return WalkNear(gen, 1.3f);
            yield return Face(gen);
            keepHolding = true;
            StartCoroutine(KeepCranking(gen));
            var until = Time.time + 20f;
            while (!Me.Check.Visible && Time.time < until) yield return null;
            if (!Me.Check.Visible) { Fail("no skill check within 20 s"); yield break; }
            yield return new WaitForSeconds(0.5f);
            message = "skill check on screen (" + Me.Check.Current + ")";
        }

        // Background: the hold ends whenever the grid is full; start it again once power is used.
        private IEnumerator KeepCranking(Generator gen)
        {
            while (keepHolding && Me != null)
            {
                Me.AutomationTarget = gen;
                if (!Me.HoldingUse && PowerGrid.Instance.Stored.Value < PowerGrid.Capacity - 3f)
                {
                    Me.AutomationHold = false;
                    yield return null;
                    Me.AutomationHold = true;
                }
                else Me.AutomationHold = true;
                yield return new WaitForSeconds(0.25f);
            }
        }

        // A missed skill check must stall the generator and bring a nearby monster to look.
        private IEnumerator NoiseTest()
        {
            var gen = Object.FindFirstObjectByType<Generator>();
            var monster = TempMonster.Instance;
            var home = monster.transform.position;
            monster.ServerTeleport(gen.transform.position + (StageDirector.Instance.ShipDoor.transform.position - gen.transform.position).normalized * 9f);
            // Room for cranking: a full grid ends the hold before a check appears.
            PowerGrid.Instance.ServerTake(Mathf.Max(0f, PowerGrid.Instance.Stored.Value - 10f));
            yield return WalkNear(gen, 1.3f);
            yield return Face(gen);
            var missed = Me.SkillChecksMissed;
            Me.AutomationSkill = PlayerInteractor.SkillAutomation.Miss;
            Me.AutomationTarget = gen;
            Me.AutomationHold = true;
            var until = Time.time + 25f;
            while (Me.SkillChecksMissed == missed && Time.time < until)
            {
                if (!Me.HoldingUse) { Me.AutomationHold = false; yield return null; Me.AutomationHold = true; }
                yield return null;
            }
            Me.AutomationSkill = PlayerInteractor.SkillAutomation.Perfect;
            if (Me.SkillChecksMissed == missed) { Fail("no skill check appeared to miss"); yield break; }
            yield return new WaitForSeconds(0.4f);
            var stalled = gen.IsStalled;
            var investigating = monster.Investigating;
            Me.AutomationHold = false;
            monster.ServerTeleport(home);
            if (!stalled || !investigating) { Fail($"after miss: stalled={stalled} monsterInvestigating={investigating}"); yield break; }
            message = "miss -> generator stalled, monster 11m away came to investigate";
        }

        private IEnumerator OpenDoor()
        {
            var door = StageDirector.Instance.ShipDoor;
            yield return WalkTo(door.transform.position + new Vector3(-1.6f, 0f, 0f), 0.4f);
            yield return Face(door.Panel.position);
            yield return HoldUntil(door, () => door.Open.Value, 20f, "door open");
            message = "ship door open";
        }

        private IEnumerator PowerCctv(int number)
        {
            var cam = CctvCamera.All.First(c => c.Order == number);
            yield return WalkTo(cam.transform.position + cam.transform.forward * 1.3f, 0.4f);
            yield return Face(cam.transform.position + Vector3.up * 1.3f + cam.transform.forward * 0.1f);
            yield return HoldUntil(cam, () => cam.Online, 30f, "cctv online");
            message = "CAM " + number + " online";
        }

        private IEnumerator CctvOpen(int number)
        {
            var panel = CctvPanel.Instance;
            yield return WalkTo(panel.transform.position + panel.transform.forward * 1.4f, 0.35f);
            yield return Face(panel.Screen.transform.position);
            Me.AutomationTarget = panel;
            Me.AutomationPress();
            yield return null;
            yield return null;
            Me.AutomationTarget = null;
            if (!CctvView.Viewing) { Fail("CCTV view did not open"); yield break; }
            CctvView.Instance.Select(number - 1);
            yield return new WaitForSeconds(1.5f);
            message = $"viewing CAM {number}, map buttons {CctvView.Instance.MapButtonCount}";
        }

        private IEnumerator CctvClose()
        {
            if (CctvView.Instance != null && CctvView.Viewing) CctvView.Instance.Exit();
            yield return null;
            message = "closed";
        }

        private IEnumerator ActivateTransmitter()
        {
            var tx = Transmitter.Instance;
            yield return WalkTo(TransmitterStand(), 0.4f);
            yield return Face(tx.transform.position + Vector3.up * 1.3f);
            yield return HoldUntil(tx, () => tx.Phase.Value != TransmitterPhase.Dormant, 60f, "transmitter active");
            message = "transmitter " + tx.Phase.Value;
        }

        private static Vector3 TransmitterStand()
        {
            var tx = Transmitter.Instance.transform;
            return tx.position + tx.forward * 2.3f;
        }

        // arg = "Fan:carry" | "Radio:drag" | "Toaster:inventory"
        //   carry: telekinesis to the transmitter, then plug. drag: carry it to cord length + 3 m away in the
        //   transmitter's room, then walk the plug over so the cord drags it (value loss). inventory: store it,
        //   walk across the map, take it out, drop it, plug.
        private IEnumerator Deliver(string arg)
        {
            var parts = arg.Split(':');
            var appliance = Appliance.All.First(a => a.name.EndsWith(parts[0], StringComparison.OrdinalIgnoreCase));
            var mode = parts.Length > 1 ? parts[1] : "carry";
            var tx = Transmitter.Instance;

            yield return WalkNear(appliance, 1.2f);
            yield return Face(appliance.transform.position + Vector3.up * 0.25f);
            if (!appliance.IsCharged)
                yield return HoldUntil(appliance, () => appliance.IsCharged, 60f, appliance.name + " charged");
            if (state != "running") yield break;
            var before = appliance.Value.Value;

            if (mode == "carry" || mode == "drag")
            {
                var away = Flat(TransmitterStand() - tx.transform.position).normalized;
                var goal = mode == "drag" ? TransmitterStand() + away * (appliance.CordLength + 3f) : TransmitterStand() + Vector3.right * 0.8f;
                yield return CarryTo(appliance, goal);
                if (state != "running") yield break;
            }
            if (mode == "inventory")
            {
                Me.AutomationHold = false;
                yield return null;
                yield return Face(appliance.transform.position + Vector3.up * 0.25f);
                Me.AutomationTarget = appliance;
                Me.AutomationHold = true;
                var until = Time.time + 5f;
                while (!appliance.HeldBy(Me) && Time.time < until) yield return null;
                if (!appliance.HeldBy(Me)) { Fail("could not lift " + appliance.name); yield break; }
                Me.AutomationTarget = null;
                Me.GetComponent<PlayerInventory>().UseSlot(0);
                until = Time.time + 3f;
                while (!appliance.IsStored && Time.time < until) yield return null;
                if (!appliance.IsStored) { Fail("could not store " + appliance.name); yield break; }
                Me.AutomationHold = false;
                Write("stored " + appliance.name + " in slot 1");
                yield return WalkTo(TransmitterStand() + Vector3.right * 0.8f, 0.5f);
                Me.GetComponent<PlayerInventory>().UseSlot(0);
                until = Time.time + 3f;
                while (!appliance.HeldBy(Me) && Time.time < until) yield return null;
                if (!appliance.HeldBy(Me)) { Fail("could not take " + appliance.name + " out"); yield break; }
                yield return new WaitForSeconds(0.6f);
                Me.AutomationDrop();
                yield return new WaitForSeconds(1.2f);
            }

            // Plug: take it, walk the cord to the transmitter, plug it in.
            var plug = appliance.Plug;
            yield return WalkTo(plug.transform.position + (Me.transform.position - plug.transform.position).normalized * 1.1f, 0.4f);
            yield return Face(plug.transform.position);
            Me.AutomationTarget = plug;
            Me.AutomationPress();
            var deadline = Time.time + 3f;
            while (!plug.HeldBy(Me) && Time.time < deadline) yield return null;
            Me.AutomationTarget = null;
            if (!plug.HeldBy(Me)) { Fail("could not take the plug of " + appliance.name); yield break; }
            yield return WalkTo(TransmitterStand(), 0.4f);
            yield return Face(tx.transform.position + Vector3.up * 1.0f);
            Me.AutomationTarget = tx;
            Me.AutomationPress();
            deadline = Time.time + 3f;
            while (!plug.IsSocketed && Time.time < deadline) yield return null;
            Me.AutomationTarget = null;
            if (!plug.IsSocketed) { Fail("could not plug " + appliance.name + " (" + plug.State.Value + ")"); yield break; }
            message = $"{appliance.name} plugged via {mode}, value {before} -> {appliance.Value.Value}, sockets {tx.PluggedCount}/{tx.Required}";
        }

        // Just outside the ship's door.
        private IEnumerator Outside()
        {
            var door = StageDirector.Instance.ShipDoor.transform.position;
            var center = (StageDirector.Instance.ShipMin + StageDirector.Instance.ShipMax) * 0.5f;
            yield return WalkTo(door + Flat(door - center).normalized * 4f, 0.5f);
            message = "outside at " + Me.transform.position.ToString("F1");
        }

        // Charge the nearest ceiling lamp.
        private IEnumerator PowerLamp()
        {
            var lamp = InstalledDevice.Devices.OfType<PowerLamp>().OrderBy(l => (l.transform.position - Me.transform.position).sqrMagnitude).First();
            yield return WalkTo(new Vector3(lamp.transform.position.x + 0.6f, 0f, lamp.transform.position.z), 0.3f);
            yield return Face(lamp.transform.position);
            yield return HoldUntil(lamp, () => lamp.Online, 30f, "lamp on");
            message = lamp.name + " lit";
        }

        // Rip a powered CCTV camera off its mount: it goes dark and drops sellable salvage.
        private IEnumerator RipCctv(int number)
        {
            var cam = CctvCamera.All.First(c => c.Order == number);
            yield return WalkTo(cam.transform.position + cam.transform.forward * 1.3f, 0.4f);
            yield return Face(cam.transform.position + Vector3.up * 1.3f + cam.transform.forward * 0.1f);
            yield return HoldUntil(cam, () => cam.Detached.Value, 20f, "camera ripped off");
            if (state != "running") yield break;
            yield return new WaitForSeconds(1.5f);
            if (cam.Salvage == null || !cam.Salvage.Revealed.Value) { Fail("no salvage dropped"); yield break; }
            message = $"CAM {number} ripped off (online={cam.Online}), salvage {cam.Salvage.name} value {cam.Salvage.Value.Value}";
        }

        // Squeeze under the nearest desk (Ctrl): the host must see the player as tucked.
        private IEnumerator Hide()
        {
            var desk = GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "Desk")
                .OrderBy(t => (t.position - Me.transform.position).sqrMagnitude).First();
            var behind = desk.position + desk.forward * 1.3f;
            yield return WalkTo(behind, 0.25f);
            Motor.AutomationCrouch = true;
            yield return new WaitForSeconds(0.5f);
            var until = Time.time + 6f;
            while (Flat(desk.position - Feet).magnitude > 0.2f && Time.time < until)
            {
                var d = Flat(desk.position - Feet);
                Motor.AutomationLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f);
                Motor.AutomationMove = Vector2.up;
                yield return null;
            }
            Motor.AutomationMove = Vector2.zero;
            yield return new WaitForSeconds(1f);
            var tucked = Motor.IsTucked();
            message = $"under {desk.name} at {Me.transform.position:F1}, crouched={Motor.Crouched.Value}, tucked={tucked}";
            if (!tucked) { Fail(message); yield break; }
        }

        // Stand back up (step "hide" leaves the player crouched for the host check).
        public static string StandUp()
        {
            if (Motor == null) return "no motor";
            Motor.AutomationCrouch = false;
            return "standing";
        }

        // Telekinesis-carry an appliance to a point; if it snags on a door frame and drops, pick it up again.
        private IEnumerator CarryTo(Appliance appliance, Vector3 goal)
        {
            for (var attempt = 0; attempt < 4 && Flat(appliance.transform.position - goal).magnitude > 3f; attempt++)
            {
                if (attempt > 0) Write($"re-grabbing {appliance.name} (attempt {attempt + 1})");
                Me.AutomationHold = false;
                yield return WalkNear(appliance, 1.0f);
                yield return Face(appliance.transform.position + Vector3.up * 0.25f);
                yield return null;
                Me.AutomationTarget = appliance;
                Me.AutomationHold = true;
                var until = Time.time + 5f;
                while (!appliance.HeldBy(Me) && Time.time < until) yield return null;
                Me.AutomationTarget = null;
                if (!appliance.HeldBy(Me)) continue;
                walkPitch = 20f;
                yield return WalkTo(goal, 0.5f, 75f, () => !appliance.HeldBy(Me));
                walkPitch = 5f;
                Me.AutomationHold = false;
                yield return new WaitForSeconds(1.2f);
            }
            if (Flat(appliance.transform.position - goal).magnitude > 3.5f) Fail($"could not carry {appliance.name} to {goal:F1} (at {appliance.transform.position:F1})");
        }

        private IEnumerator WalkStep(string arg)
        {
            var xz = arg.Split(',');
            yield return WalkTo(new Vector3(float.Parse(xz[0]), 0f, float.Parse(xz[1])), 0.5f);
            message = "at " + Me.transform.position.ToString("F1");
        }

        private IEnumerator Kill(ulong clientId)
        {
            var victim = PlayerHealth.All.FirstOrDefault(p => p.OwnerClientId == clientId);
            if (victim == null || !NetworkManager.Singleton.IsServer) { Fail("cannot kill " + clientId); yield break; }
            victim.ServerApplyDamage(999, Vector3.zero);
            yield return new WaitForSeconds(0.5f);
            message = $"player {clientId} dead={victim.IsDead.Value} body={(victim.Body != null)}";
        }

        private IEnumerator ReviveByHitting(ulong clientId)
        {
            var victim = PlayerHealth.All.First(p => p.OwnerClientId == clientId);
            var body = Corpse.Bodies.First(c => c.PlayerClientId.Value == clientId);
            yield return WalkTo(body.transform.position + (Me.transform.position - body.transform.position).normalized * 1.3f, 0.3f);
            // Revive = hold E on the body (PlayerReviver), paying 30 of our own health.
            var reviver = Me.GetComponent<PlayerReviver>();
            var myHealth = Me.GetComponent<PlayerHealth>();
            var before = myHealth.Health.Value;
            var until = Time.time + 12f;
            yield return Face(body.transform.position + Vector3.up * 0.3f);
            Me.AutomationTarget = body;
            reviver.AutomationHold = true;
            while (victim.IsDead.Value && Time.time < until) yield return null;
            reviver.AutomationHold = false;
            Me.AutomationTarget = null;
            if (victim.IsDead.Value) { Fail("still dead after holding E"); yield break; }
            yield return new WaitForSeconds(0.5f);
            message = $"revived by holding E, hp {victim.Health.Value}, reviver hp {before}->{myHealth.Health.Value}";
        }

        private IEnumerator CarryCorpse(ulong clientId)
        {
            var body = Corpse.Bodies.First(c => c.PlayerClientId.Value == clientId);
            yield return WalkTo(body.transform.position + (Me.transform.position - body.transform.position).normalized * 1.4f, 0.3f);
            yield return Face(body.transform.position + Vector3.up * 0.3f);
            Me.AutomationTarget = body;
            Me.AutomationPress();
            Me.AutomationHold = true;
            var until = Time.time + 4f;
            while (!body.HeldBy(Me) && Time.time < until) yield return null;
            Me.AutomationTarget = null;
            if (!body.HeldBy(Me)) { Fail("could not lift the body"); yield break; }
            walkPitch = 20f;
            var stage = StageDirector.Instance;
            yield return WalkTo((stage.ShipMin + stage.ShipMax) * 0.5f + new Vector3(2f, 0f, 2f), 0.5f);
            Me.AutomationHold = false;
            walkPitch = 5f;
            yield return new WaitForSeconds(1.5f);
            var inShip = StageDirector.Instance.InShip(body.transform.position);
            if (!inShip) { Fail("body not in the ship: " + body.transform.position.ToString("F1")); yield break; }
            message = "body carried into the ship";
        }

        private IEnumerator Launch()
        {
            var button = Object.FindFirstObjectByType<LaunchButton>();
            yield return WalkTo(button.transform.position + button.transform.forward * 1.2f, 0.3f);
            yield return Face(button.transform.position + Vector3.up * 1.03f);
            Me.AutomationTarget = button;
            Me.AutomationPress();
            yield return new WaitForSeconds(0.5f);
            Me.AutomationTarget = null;
            var stage = StageDirector.Instance;
            if (stage.Phase.Value != StagePhase.Launching) { Fail("launch not armed: " + stage.Phase.Value); yield break; }
            message = $"launch armed, {stage.LaunchRemaining:0.0}s";
        }

        private IEnumerator WaitEscape()
        {
            var stage = StageDirector.Instance;
            var until = Time.time + 20f;
            while (stage.Phase.Value == StagePhase.Launching && Time.time < until) yield return null;
            yield return new WaitForSeconds(0.6f);
            if (stage.Phase.Value != StagePhase.Escaped) { Fail("stage " + stage.Phase.Value); yield break; }
            message = "escaped, aboard " + Ids(stage.Aboard) + ", earned " + stage.Earned.Value;
        }

        private IEnumerator Radio(string arg)
        {
            var radio = Me.GetComponent<PlayerRadio>();
            radio.AutomationTalk = arg == "on" ? true : arg == "off" ? false : (bool?)null;
            yield return new WaitForSeconds(0.5f);
            message = "radio " + radio.Mode.Value;
        }

        private IEnumerator SpectateCheck()
        {
            yield return new WaitForSeconds(0.5f);
            var cam = Camera.main;
            if (!FirstPersonController.SpectatingClient.HasValue) { Fail("not spectating anyone"); yield break; }
            if (cam != null && cam.transform.parent != null) { Fail("camera still attached to the body"); yield break; }
            message = "ghost spectating player " + FirstPersonController.SpectatingClient.Value;
        }

        // ---------- helpers ----------

        private static PlayerInteractor Me => PlayerInteractor.Local;
        private static FirstPersonController Motor => FirstPersonController.Local;
        private static Vector3 Feet => Me.transform.position + Vector3.down * 1f;

        private IEnumerator WalkNear(Interactable target, float gap)
        {
            var bounds = new Bounds(target.transform.position, Vector3.zero);
            foreach (var c in target.GetComponentsInChildren<Collider>()) bounds.Encapsulate(c.bounds);
            var from = Vector3.ProjectOnPlane(Feet - bounds.center, Vector3.up);
            if (from.sqrMagnitude < 0.01f) from = Vector3.forward;
            var reach = Mathf.Max(bounds.extents.x, bounds.extents.z) + gap;
            var goal = bounds.center + from.normalized * reach;
            goal.y = 0f;
            yield return WalkTo(goal, 0.35f);
        }

        private IEnumerator WalkTo(Vector3 goal, float stop, float timeout = 75f, Func<bool> abort = null)
        {
            var path = new NavMeshPath();
            Vector3[] corners;
            if (NavMesh.SamplePosition(Feet, out var a, 2.5f, NavMesh.AllAreas) && NavMesh.SamplePosition(goal, out var b, 3f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.corners.Length > 0)
                corners = path.corners;
            else corners = new[] { goal };
            var deadline = Time.time + timeout;
            for (var i = 0; i < corners.Length; i++)
            {
                var last = i == corners.Length - 1;
                var corner = corners[i];
                var anchor = Feet;
                var anchorTime = Time.time;
                while (Flat(corner - Feet).magnitude > (last ? stop : 0.45f))
                {
                    if (abort != null && abort()) { Motor.AutomationMove = Vector2.zero; yield break; }
                    if (Time.time > deadline) { Fail($"walk timeout near {Feet:F1} -> {goal:F1}"); Motor.AutomationMove = Vector2.zero; yield break; }
                    var dir = Flat(corner - Feet);
                    Motor.AutomationLook(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, walkPitch);
                    Motor.AutomationMove = Vector2.up;
                    if ((Feet - anchor).sqrMagnitude > 0.09f) { anchor = Feet; anchorTime = Time.time; }
                    else if (Time.time - anchorTime > 1.5f)
                    {
                        // Stuck on a corner: sidestep for a moment.
                        Motor.AutomationMove = new Vector2(UnityEngine.Random.value < 0.5f ? -1f : 1f, 0.4f);
                        var stepUntil = Time.time + 0.5f;
                        while (Time.time < stepUntil) yield return null;
                        anchorTime = Time.time;
                    }
                    yield return null;
                }
            }
            Motor.AutomationMove = Vector2.zero;
        }

        private IEnumerator Face(Interactable target) => Face(target.transform.position + Vector3.up * 0.8f);

        private IEnumerator Face(Vector3 point)
        {
            var d = point - Me.EyePosition;
            var yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            var pitch = -Mathf.Atan2(d.y, Flat(d).magnitude) * Mathf.Rad2Deg;
            Motor.AutomationLook(yaw, pitch);
            yield return null;
        }

        // Holds the button on a target until done; re-presses when the hold dropped (e.g. grid ran dry).
        private IEnumerator HoldUntil(Interactable target, Func<bool> done, float timeout, string what)
        {
            Me.AutomationTarget = target;
            Me.AutomationHold = true;
            var deadline = Time.time + timeout;
            var notHolding = 0f;
            while (!done())
            {
                if (Time.time > deadline) { Fail("timeout waiting for " + what); break; }
                notHolding = Me.HoldingUse ? 0f : notHolding + Time.deltaTime;
                if (notHolding > 0.6f)
                {
                    // Let go for a frame so the next hold starts fresh.
                    Me.AutomationHold = false;
                    yield return null;
                    Me.AutomationHold = true;
                    notHolding = 0f;
                }
                yield return null;
            }
            Me.AutomationHold = false;
            Me.AutomationTarget = null;
            yield return null;
        }


        private void Release()
        {
            if (Motor != null) Motor.AutomationMove = Vector2.zero;
            if (Me == null || keepHolding) return;
            Me.AutomationHold = false;
            Me.AutomationTarget = null;
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        private static string Ids(NetworkList<ulong> list)
        {
            var sb = new StringBuilder();
            foreach (var id in list) sb.Append(sb.Length > 0 ? "," : "").Append(id);
            return sb.ToString();
        }

        private static void Ok(string text)
        {
            state = "ok";
            message = text;
            Write("ok " + text);
        }

        private static void Fail(string text)
        {
            if (state != "running") return;
            state = "fail";
            message = text;
            Write("FAIL " + text);
        }

        private static void Write(string line)
        {
            Log.Append($"[{Time.realtimeSinceStartup:0.0}] ").AppendLine(line);
            Debug.Log("[E2E] " + line);
        }
    }
}
#endif
