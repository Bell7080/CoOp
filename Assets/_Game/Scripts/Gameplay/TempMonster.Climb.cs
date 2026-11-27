using System.Collections.Generic;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace CoOpHorror.Gameplay
{
    // Clock spider on walls and ceilings (host only; every peer just sees the replicated transform).
    //
    // Pathfinding on the ceiling uses the floor NavMesh: the NavMeshAgent keeps running on the floor as the
    // spider's "shadow" (updatePosition off) and the body hangs from whatever ceiling is above that shadow,
    // so the usual brain (patrol, noises, chase) steers it over the ceiling through doorways and around the
    // room layout. Under a lower lintel or pipe it creeps down to that height and tilts with the slope.
    //
    // Getting up and down uses real walls: it probes for a plain wall face that goes all the way from the
    // floor to the ceiling (no door, nothing in the way of its body, walkable floor at its foot), then walks
    // a route along it: floor -> rounded inside corner -> straight up the wall -> rounded corner -> ceiling,
    // its body turning with the surface normal the whole way (and the same route backwards, head first, to
    // come down). Being stunned or drained flat up there makes it fall off.
    //
    // The ceiling attack: hanging over (or near) its target it goes still and silent for a moment, creaks,
    // then lets go and drops onto them, flipping over in the air. Touching them on the way down it clings to
    // their back (the usual 5 s alarm latch); missing, it lands with a thud and is dazed for a second.
    public sealed partial class TempMonster
    {
        public const float ClimbSpeed = 2.4f;            // along walls and around corners
        public const float CornerRadius = 0.5f;           // how wide it rounds an inside corner
        public const float WallProbe = 1.8f;              // how far from a wall it decides to climb it
        public const float CeilingStayMin = 20f, CeilingStayMax = 40f;
        public const float FloorStayMin = 6f, FloorStayMax = 16f;
        public const float DropRange = 3f;                // flat distance it pounces from
        public const float DropWindup = 0.45f;            // still and silent before letting go
        public const float DropGravity = 22f;
        public const float DropMaxSideSpeed = 6f;
        public const float DropLatchReach = 1.1f;         // touching the target on the way down
        public const float LandStun = 1f;                 // dazed after a missed drop
        public const float CeilingChaseGiveUp = 10f;      // can't get over them: drops down and runs instead
        private const float LookAhead = 1f;

        private enum Surface : byte { Floor, Route, Ceiling, Windup, Falling }

        private Surface surface;
        private readonly List<Vector3> routePos = new();
        private readonly List<Vector3> routeUp = new();
        private float routeAt;
        private int routeSegment;
        private bool routeToCeiling;
        private Vector3 routeShadow;     // floor point under the end of an upward route
        private float routeCeilingY;
        private float nextSurfaceChange, nextWallProbe;
        private float ceilingY;
        private float ceilingChaseSince = -1f;
        private float pounceAt;
        private PlayerHealth dropTarget;
        private Vector3 fallVelocity;
        private float fallFloorY;
        private float silentUntil;       // every peer: the ticking stops before a drop
        private static readonly RaycastHit[] ClimbHits = new RaycastHit[16];

        // Host: hanging from or walking on something other than the floor.
        private bool OffFloor => surface != Surface.Floor;

        // Host: the surface it stands on faces this way (its own up; world up on the floor).
        private Vector3 SurfaceUp => surface == Surface.Floor ? Vector3.up : transform.up;

        // Host: everything that moves the spider by itself this frame (a wall route, the drop wind-up, a fall).
        // Also knocks it off a wall or ceiling when it is stunned or drained flat up there.
        private bool ServerTickClimbing()
        {
            if (Kind != CreatureKind.ClockSpider || surface == Surface.Floor) return false;
            if (Mode.Value == CreatureMode.Latched) { ServerResetSurface(); return false; }
            var knocked = Mode.Value == CreatureMode.Stunned || Mode.Value == CreatureMode.Dead;
            if (knocked && surface != Surface.Falling) ServerStartFall(null);
            if (Frozen) return surface != Surface.Ceiling;
            switch (surface)
            {
                case Surface.Route: ServerTickRoute(); return true;
                case Surface.Windup: ServerTickWindup(); return true;
                case Surface.Falling: ServerTickFall(); return true;
            }
            return false; // on the ceiling the usual brain steers the shadow (followed in LateUpdate)
        }

        // Host, after the usual brain: decide to go up a wall, come down one, or drop on someone.
        private void ServerSpiderThink()
        {
            if (Kind != CreatureKind.ClockSpider || Mode.Value != CreatureMode.Active || ringUntil > 0f) return;
            if (surface == Surface.Floor)
            {
                if (target != null || Time.time < nextSurfaceChange || Time.time < nextWallProbe) return;
                nextWallProbe = Time.time + 0.4f;
                if (FindClimbWall(false, out var foot, out var normal, out var floorY, out var ceilY))
                    ServerStartRoute(true, foot, normal, floorY, ceilY);
                return;
            }
            if (surface != Surface.Ceiling) return;
            if (target != null)
            {
                if (ceilingChaseSince < 0f) ceilingChaseSince = Time.time;
                var to = target.transform.position - transform.position;
                var seenNow = Time.time - lastSeenTime < 0.3f;
                if (seenNow && Time.time >= nextLunge && Flat(to).magnitude < DropRange && to.y < -1f
                    && ClearLine(transform.position + transform.up * 0.5f, target.transform.position))
                {
                    ServerStartPounce(target);
                    return;
                }
                if (Time.time - ceilingChaseSince > CeilingChaseGiveUp) ServerStartFall(null);
                return;
            }
            ceilingChaseSince = -1f;
            if (Time.time < nextSurfaceChange || Time.time < nextWallProbe) return;
            nextWallProbe = Time.time + 0.4f;
            if (FindClimbWall(true, out var downFoot, out var downNormal, out var downFloor, out var downCeil))
                ServerStartRoute(false, downFoot, downNormal, downFloor, downCeil);
        }

        // Host: back on the floor, upright, agent in charge again (spawn, teleport, latch).
        private void ServerResetSurface()
        {
            var wasOff = surface != Surface.Floor;
            surface = Surface.Floor;
            dropTarget = null;
            routePos.Clear();
            routeUp.Clear();
            ceilingChaseSince = -1f;
            if (agent != null)
            {
                agent.updatePosition = true;
                agent.updateRotation = true;
            }
            if (wasOff)
            {
                var forward = Flat(transform.forward);
                if (forward.sqrMagnitude < 0.01f) forward = Flat(-transform.up);
                transform.rotation = Quaternion.LookRotation(forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward);
            }
            nextSurfaceChange = Time.time + Random.Range(3f, 8f);
        }

        // ---------- walls ----------

        // Host: a plain wall within reach that goes from the floor all the way to the ceiling it can use.
        // fromCeiling: looked for from the ceiling (to climb down), at the same ceiling height it hangs from.
        private bool FindClimbWall(bool fromCeiling, out Vector3 foot, out Vector3 normal, out float floorY, out float ceilY)
        {
            foot = normal = default;
            floorY = ceilY = 0f;
            var p = transform.position;
            var baseY = fromCeiling ? agent.nextPosition.y : p.y;
            var probeY = fromCeiling ? p.y - 0.45f : baseY + 0.45f;
            var first = Random.Range(0, 8);
            for (var k = 0; k < 8; k++)
            {
                var dir = Quaternion.Euler(0f, (first + k) * 45f, 0f) * Vector3.forward;
                if (!SolidRay(new Vector3(p.x, probeY, p.z), dir, WallProbe, out var hit)) continue;
                if (Mathf.Abs(hit.normal.y) > 0.15f || IsDoor(hit.collider)) continue;
                var n = Flat(hit.normal).normalized;
                if (Vector3.Dot(n, dir) > -0.5f) continue; // too glancing
                var f = new Vector3(hit.point.x, baseY, hit.point.z);
                if (!SolidRay(f + n * CornerRadius + Vector3.up * 0.3f, Vector3.up, 7f, out var top) || top.normal.y > -0.5f) continue;
                var cy = top.point.y;
                if (cy - baseY < 2.4f || cy - baseY > 6.5f) continue;
                if (fromCeiling && Mathf.Abs(cy - p.y) > 0.3f) continue; // not the ceiling it hangs from
                // one flat face from bottom to top
                if (!WallFaceAt(f, n, baseY + 0.4f) || !WallFaceAt(f, n, (baseY + cy) / 2f) || !WallFaceAt(f, n, cy - 0.4f)) continue;
                // nothing on the wall in the way of its body (pipes, cameras, a shelf against the wall)
                var climb = cy - baseY - 1.4f;
                if (climb > 0f && Blocked(f + n * (CornerRadius + 0.15f) + Vector3.up * 0.7f, Vector3.up, climb, 0.3f)) continue;
                if (!NavMesh.SamplePosition(f + n * (CornerRadius + 0.4f), out _, 0.6f, NavMesh.AllAreas)) continue;
                foot = f;
                normal = n;
                floorY = baseY;
                ceilY = cy;
                return true;
            }
            return false;
        }

        private bool WallFaceAt(Vector3 foot, Vector3 n, float y)
        {
            var from = new Vector3(foot.x, y, foot.z) + n * 0.5f;
            return SolidRay(from, -n, 0.75f, out var h) && Mathf.Abs(h.distance - 0.5f) < 0.12f
                && Vector3.Dot(h.normal, n) > 0.9f && !IsDoor(h.collider);
        }

        // Host: the route floor -> corner -> up the wall -> corner -> ceiling (or the same backwards).
        private void ServerStartRoute(bool up, Vector3 foot, Vector3 n, float floorY, float ceilY)
        {
            HaltAgent();
            agent.updatePosition = false;
            agent.updateRotation = false;
            routePos.Clear();
            routeUp.Clear();
            var r = CornerRadius;
            var wallTop = new Vector3(foot.x, ceilY, foot.z);
            var onFloor = foot + n * (r + 0.4f);
            var onCeiling = wallTop + n * (r + 0.4f);
            if (up) AddPoint(transform.position, Vector3.up);
            else AddPoint(onFloor, Vector3.up);
            AddPoint(foot + n * r, Vector3.up);
            // floor -> wall: an inside corner rounded about a line r off both faces
            var c1 = foot + n * r + Vector3.up * r;
            for (var i = 1; i <= 6; i++)
            {
                var a = i / 6f * Mathf.PI * 0.5f;
                var point = c1 + (-Vector3.up * Mathf.Cos(a) - n * Mathf.Sin(a)) * r;
                AddPoint(point, (c1 - point).normalized);
            }
            AddPoint(wallTop - Vector3.up * r, n);
            // wall -> ceiling
            var c2 = wallTop + n * r - Vector3.up * r;
            for (var i = 1; i <= 6; i++)
            {
                var a = i / 6f * Mathf.PI * 0.5f;
                var point = c2 + (-n * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * r;
                AddPoint(point, (c2 - point).normalized);
            }
            if (up) AddPoint(onCeiling, Vector3.down);
            else
            {
                AddPoint(transform.position, Vector3.down);
                routePos.Reverse();
                routeUp.Reverse();
            }
            routeToCeiling = up;
            routeCeilingY = ceilY;
            routeShadow = NavMesh.SamplePosition(onFloor, out var hit, 1f, NavMesh.AllAreas) ? hit.position : onFloor;
            routeAt = 0f;
            routeSegment = 0;
            surface = Surface.Route;
            target = null;
            Chasing.Value = false;
        }

        private void AddPoint(Vector3 position, Vector3 up)
        {
            if (routePos.Count > 0 && (routePos[^1] - position).sqrMagnitude < 0.0004f) return;
            routePos.Add(position);
            routeUp.Add(up);
        }

        private void ServerTickRoute()
        {
            var step = ClimbSpeed * Time.deltaTime;
            while (step > 0f && routeSegment < routePos.Count - 1)
            {
                var a = routePos[routeSegment];
                var b = routePos[routeSegment + 1];
                var length = Vector3.Distance(a, b);
                var left = length - routeAt;
                if (step < left) { routeAt += step; step = 0f; break; }
                step -= left;
                routeAt = 0f;
                routeSegment++;
            }
            if (routeSegment >= routePos.Count - 1) { ServerFinishRoute(); return; }
            var from = routePos[routeSegment];
            var to = routePos[routeSegment + 1];
            var span = Vector3.Distance(from, to);
            var t = span > 0.0001f ? routeAt / span : 1f;
            var position = Vector3.Lerp(from, to, t);
            var normal = Vector3.Slerp(routeUp[routeSegment], routeUp[routeSegment + 1], t).normalized;
            var tangent = Vector3.ProjectOnPlane(to - from, normal);
            if (tangent.sqrMagnitude < 0.0001f) tangent = transform.forward;
            transform.position = position;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(tangent.normalized, normal), 1f - Mathf.Exp(-14f * Time.deltaTime));
        }

        private void ServerFinishRoute()
        {
            var end = routePos[^1];
            routePos.Clear();
            routeUp.Clear();
            if (routeToCeiling)
            {
                agent.Warp(routeShadow); // the shadow on the floor below (updatePosition is off)
                transform.position = end;
                ceilingY = routeCeilingY;
                surface = Surface.Ceiling;
                ceilingChaseSince = -1f;
                nextSurfaceChange = Time.time + Random.Range(CeilingStayMin, CeilingStayMax);
            }
            else
            {
                surface = Surface.Floor;
                agent.updatePosition = true;
                agent.updateRotation = true;
                agent.Warp(routeShadow);
                transform.position = routeShadow;
                nextSurfaceChange = Time.time + Random.Range(FloorStayMin, FloorStayMax);
            }
            nextRepath = 0f;
        }

        // ---------- ceiling ----------

        // Clinging to a head (every peer): smaller, gripping the skull. The victim's own camera sees it from
        // inside: the clock sits just above their brow, out of sight, tipped forward so its legs hang down over
        // the top and the sides of the screen and twitch there (its idle clip taps one leg after another).
        public const float ClingScale = 0.6f;
        public static readonly Vector3 ClingViewOffset = new(0f, 0.06f, 0.05f);   // from the camera (right, up, forward)
        public const float ClingViewPitch = 38f;                                    // tipped forward over the eyes
        private bool clingShown;

        private void UpdateClingVisual()
        {
            if (Kind != CreatureKind.ClockSpider) return;
            var latched = IsSpawned && Mode.Value == CreatureMode.Latched;
            CacheRestPose();
            if (deathVisual == null) return;
            if (!latched)
            {
                if (!clingShown) return;
                clingShown = false;
                deathVisual.localPosition = restPosition;
                deathVisual.localRotation = restRotation;
                deathVisual.localScale = restScale;
                return;
            }
            clingShown = true;
            deathVisual.localScale = restScale * ClingScale;
            if (LatchedToLocal) return; // placed in front of the camera just before it renders (OnBeginCamera)
            deathVisual.localPosition = restPosition;
            deathVisual.localRotation = restRotation;
        }

        private bool LatchedToLocal
        {
            get
            {
                var me = PlayerInteractor.Local;
                return IsSpawned && Mode.Value == CreatureMode.Latched && me != null && LatchedTo.Value == me.NetworkObjectId;
            }
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        // After the camera has moved this frame, so the legs stay glued to the view while looking around.
        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (Kind != CreatureKind.ClockSpider || cam != Camera.main || deathVisual == null || !clingShown || !LatchedToLocal) return;
            {
                var view = cam.transform;
                var shake = Mathf.PerlinNoise(Time.time * 9f, 0.7f) - 0.5f; // its grip shifting
                deathVisual.SetPositionAndRotation(
                    view.position + view.right * ClingViewOffset.x + view.up * (ClingViewOffset.y + 0.01f * shake) + view.forward * ClingViewOffset.z,
                    view.rotation * Quaternion.Euler(ClingViewPitch + 4f * shake, 0f, 3f * shake) * restRotation);
            }
        }

        private void LateUpdate()
        {
            UpdateClingVisual();
            if (!IsServer || !IsSpawned || Kind != CreatureKind.ClockSpider || surface != Surface.Ceiling) return;
            if (Mode.Value != CreatureMode.Active || !agent.enabled) return;
            ServerFollowCeiling();
        }

        // Host: hang from the ceiling over the agent's shadow, facing where it goes, tilted with the ceiling.
        private void ServerFollowCeiling()
        {
            var dt = Time.deltaTime;
            var shadow = agent.nextPosition;
            var velocity = Flat(agent.velocity);
            var heading = velocity.sqrMagnitude > 0.04f ? velocity.normalized : Flat(transform.forward);
            heading = heading.sqrMagnitude > 0.01f ? heading.normalized : Vector3.forward;
            if (!CeilingAt(shadow, out var here)) { ServerStartFall(null); return; }
            var ahead = CeilingAt(shadow + heading * LookAhead, out var aheadY) ? aheadY : here;
            ceilingY = Mathf.MoveTowards(ceilingY, Mathf.Min(here, ahead), 4f * dt);
            transform.position = new Vector3(shadow.x, ceilingY, shadow.z);
            // tilt along the slope (creeping down to a lintel and back up)
            var slope = Mathf.Clamp((ahead - here) / LookAhead, -1.5f, 1.5f);
            var tangent = (heading + Vector3.up * slope).normalized;
            var right = Vector3.Cross(Vector3.up, heading).normalized;
            var normal = Vector3.Cross(tangent, right).normalized;
            if (normal.y > 0f) normal = -normal; // hanging: its feet point up into the ceiling
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(tangent, normal), 1f - Mathf.Exp(-10f * dt));
        }

        // Host: the underside of the ceiling (or lintel, or pipe) above this floor point, near the height it
        // hangs at (lamps hanging lower are not ceilings).
        private bool CeilingAt(Vector3 floorPoint, out float y)
        {
            y = 0f;
            var from = new Vector3(floorPoint.x, ceilingY - 1f, floorPoint.z);
            if (!SolidRay(from, Vector3.up, 2.5f, out var hit) || hit.normal.y > -0.5f) return false;
            y = hit.point.y;
            return true;
        }

        // ---------- the drop ----------

        private void ServerStartPounce(PlayerHealth who)
        {
            HaltAgent();
            surface = Surface.Windup;
            dropTarget = who;
            pounceAt = Time.time + DropWindup;
            PounceFxRpc();
        }

        private void ServerTickWindup()
        {
            if (dropTarget != null)
            {
                var direction = Flat(dropTarget.transform.position - transform.position);
                if (direction.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction, transform.up), 240f * Time.deltaTime);
            }
            if (Time.time < pounceAt) return;
            var valid = dropTarget != null && dropTarget.IsSpawned && !dropTarget.IsDead.Value
                && Flat(dropTarget.transform.position - transform.position).magnitude < DropRange + 1.5f;
            if (valid) ServerStartFall(dropTarget);
            else { surface = Surface.Ceiling; dropTarget = null; nextLunge = Time.time + 1f; }
        }

        // Host: let go. aim: the player it drops onto (null: just falls, e.g. stunned or out of ceiling).
        private void ServerStartFall(PlayerHealth aim)
        {
            if (agent.enabled) HaltAgent();
            var position = transform.position;
            fallFloorY = agent.enabled ? agent.nextPosition.y : homePosition.y;
            if (SolidRay(position + Vector3.down * 0.3f, Vector3.down, 10f, out var floor)) fallFloorY = Mathf.Max(fallFloorY, floor.point.y);
            if (fallFloorY > position.y) fallFloorY = position.y;
            fallVelocity = Vector3.zero;
            dropTarget = aim;
            if (aim != null)
            {
                var fallTime = Mathf.Sqrt(2f * Mathf.Max(0.5f, position.y - fallFloorY) / DropGravity);
                fallVelocity = Vector3.ClampMagnitude(Flat(aim.transform.position - position) / fallTime, DropMaxSideSpeed);
                DropFxRpc();
            }
            routePos.Clear();
            routeUp.Clear();
            surface = Surface.Falling;
            ceilingChaseSince = -1f;
        }

        private void ServerTickFall()
        {
            var dt = Time.deltaTime;
            var aim = dropTarget != null && dropTarget.IsSpawned && !dropTarget.IsDead.Value ? dropTarget : null;
            if (aim != null)
            {
                // a little steering toward where they are now (they still get to dodge)
                var wanted = Vector3.ClampMagnitude(Flat(aim.transform.position - transform.position) * 3f, DropMaxSideSpeed);
                var side = Vector3.MoveTowards(Flat(fallVelocity), wanted, 8f * dt);
                fallVelocity = new Vector3(side.x, fallVelocity.y, side.z);
            }
            fallVelocity.y -= DropGravity * dt;
            var position = transform.position + fallVelocity * dt;
            var face = Flat(fallVelocity).sqrMagnitude > 0.05f ? Flat(fallVelocity).normalized : Flat(transform.forward);
            if (face.sqrMagnitude < 0.01f) face = Vector3.forward;
            // flips over in the air to land on its feet
            transform.SetPositionAndRotation(position,
                Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face.normalized, Vector3.up), 620f * dt));
            if (aim != null && Mode.Value == CreatureMode.Active && Vector3.Distance(position, aim.transform.position) < DropLatchReach)
            {
                ServerLatch(aim);
                return;
            }
            if (position.y <= fallFloorY) ServerLand(aim != null);
        }

        private void ServerLand(bool missed)
        {
            var spot = new Vector3(transform.position.x, fallFloorY, transform.position.z);
            var face = Flat(transform.forward);
            surface = Surface.Floor;
            dropTarget = null;
            transform.rotation = Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face.normalized : Vector3.forward);
            if (Mode.Value == CreatureMode.Dead || !agent.enabled) { transform.position = spot; return; }
            if (!NavMesh.SamplePosition(spot, out var hit, 3f, NavMesh.AllAreas)) hit.position = homePosition;
            agent.updatePosition = true;
            agent.updateRotation = true;
            transform.position = hit.position;
            agent.Warp(hit.position);
            if (sync != null && sync.CanCommitToTransform && (hit.position - spot).sqrMagnitude > 1f)
                sync.Teleport(hit.position, transform.rotation, transform.localScale);
            LandFxRpc(missed);
            nextSurfaceChange = Time.time + Random.Range(FloorStayMin, FloorStayMax);
            nextLunge = Time.time + 1.5f;
            nextAttack = Time.time + AttackCooldown;
            nextRepath = 0f;
            if (missed && Mode.Value == CreatureMode.Active) ServerStun(LandStun);
        }

        // ---------- physics helpers ----------

        private bool IsSolid(Collider c) =>
            c != null && !c.isTrigger && !c.transform.IsChildOf(transform)
            && c.GetComponentInParent<PlayerHealth>() == null && c.GetComponentInParent<TempMonster>() == null
            && (c.attachedRigidbody == null || c.attachedRigidbody.isKinematic);

        private static bool IsDoor(Collider c) =>
            c.GetComponentInParent<SwingDoor>() != null || c.GetComponentInParent<PoweredDoor>() != null;

        // Nearest solid hit, ignoring itself, players, creatures and loose objects.
        private bool SolidRay(Vector3 origin, Vector3 direction, float distance, out RaycastHit best)
        {
            best = default;
            var found = false;
            var nearest = float.MaxValue;
            var count = Physics.RaycastNonAlloc(origin, direction, ClimbHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var h = ClimbHits[i];
                if (h.distance >= nearest || !IsSolid(h.collider)) continue;
                nearest = h.distance;
                best = h;
                found = true;
            }
            return found;
        }

        private bool Blocked(Vector3 origin, Vector3 direction, float distance, float radius)
        {
            var count = Physics.SphereCastNonAlloc(origin, radius, direction, ClimbHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
                if (IsSolid(ClimbHits[i].collider)) return true;
            return false;
        }

        private bool ClearLine(Vector3 from, Vector3 to)
        {
            var offset = to - from;
            return !SolidRay(from, offset.normalized, offset.magnitude, out _);
        }

        // Where the creature's battery should drop: on the floor under it, even when it dies up on the ceiling.
        private Vector3 FloorUnder(Vector3 position)
        {
            if (!OffFloor) return position;
            if (SolidRay(position + Vector3.down * 0.3f, Vector3.down, 10f, out var floor)) return floor.point + Vector3.up * 0.05f;
            return agent.enabled ? agent.nextPosition : position;
        }

        // ---------- effects (every peer) ----------

        // Goes still: the ticking stops, a dry creak overhead.
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void PounceFxRpc()
        {
            silentUntil = Time.time + DropWindup + 0.6f;
            ProtoAudio.PlayPitched(ProtoSound.StingerScrape, transform.position, 0.55f, 1.35f);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void DropFxRpc()
        {
            ProtoAudio.Play(ProtoSound.MonsterLunge, transform.position, 0.9f);
            var rig = GetComponentInChildren<RigAnimator>();
            if (rig != null) rig.PlayOnce(rig.AttackState, 0.8f);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void LandFxRpc(bool missed)
        {
            ProtoAudio.Play(ProtoSound.Land, transform.position, missed ? 0.9f : 0.6f);
            if (missed) ProtoAudio.PlayPitched(ProtoSound.Alarm, transform.position + Vector3.up * 0.5f, 0.25f, 1.6f); // a dazed ding
        }
    }
}
