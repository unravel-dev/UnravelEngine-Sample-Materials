using System;
using System.Collections.Generic;
using Unravel.Core;

/// <summary>
/// A training dummy to cut up. Drag the left mouse button across it to slash along the stroke: the faster the stroke,
/// the deeper the cut. A click stabs. Left alone for a few seconds, an unseen blade slashes it on its own.
///
/// A cut leaves a gash on the part it crossed, throws blood off the blade and keeps bleeding: blood runs down the part
/// and drips into a pool that grows under the dummy. Every drop of blood is simulated here, drawn with
/// Graphics.DrawMeshInstanced and raycast against the bay's colliders; where one lands, Decals.Spawn leaves a drop
/// stretched along its travel. A deep cut across the belly spills guts and flesh (prefabs with physics bodies); one
/// across the head or an arm cuts it off, and the stump sprays. The dummy wobbles on its post and heals after a while.
/// </summary>
[ScriptSourceFile]
public class GoreDummy : ScriptComponent
{
    /// <summary>The pivot on top of the post: the torso, head and arms hang under it, and it wobbles when hit.</summary>
    public Entity body;
    /// <summary>The torso, its origin at the bottom of the hips. A deep cut across the belly spills guts.</summary>
    public Entity torso;
    /// <summary>The head. A deep cut across it cuts it off.</summary>
    public Entity head;
    /// <summary>The left arm. A deep cut across it cuts it off.</summary>
    public Entity armLeft;
    /// <summary>The right arm. A deep cut across it cuts it off.</summary>
    public Entity armRight;
    /// <summary>A Decal entity on the floor under the dummy: it grows into a pool as the dummy bleeds.</summary>
    public Entity pool;
    /// <summary>The camera the player slashes through; empty finds the one named Main Camera.</summary>
    public Entity viewCamera;

    /// <summary>The Decal material of a cut.</summary>
    public Material cutMaterial;
    /// <summary>The Decal material a drop of blood leaves where it lands.</summary>
    public Material dropMaterial;
    /// <summary>The Decal material of blood running down from a cut.</summary>
    public Material runMaterial;
    /// <summary>The Decal material of a stump, where a part was cut off.</summary>
    public Material stumpMaterial;
    /// <summary>The material of drops in flight.</summary>
    public Material dropletMaterial;
    /// <summary>The material of the blade's streak.</summary>
    public Material streakMaterial;
    /// <summary>The mesh of a drop in flight (a sphere), stretched along its travel.</summary>
    public Mesh dropletMesh;
    /// <summary>The mesh of the blade's streak (a cube), stretched along the stroke.</summary>
    public Mesh streakMesh;
    /// <summary>A rope of guts, spilled by a deep cut across the belly.</summary>
    public Prefab gutsPrefab;
    /// <summary>A chunk of flesh, thrown off by deep cuts.</summary>
    public Prefab chunkPrefab;

    /// <summary>Metres above the torso's origin where the belly ends: a deep cut below spills guts.</summary>
    public float bellyHeight = 0.42f;
    /// <summary>Metres above the torso's origin where the shoulders are: the unseen blade cuts below.</summary>
    public float shoulderHeight = 0.62f;
    /// <summary>Seconds without a stroke before the unseen blade starts.</summary>
    public float idleSeconds = 3.0f;
    /// <summary>Seconds between two strokes of the unseen blade.</summary>
    public float autoInterval = 1.4f;
    /// <summary>Seconds after which cut-off parts grow back and cuts fade.</summary>
    public float healSeconds = 25.0f;
    /// <summary>Within this many metres of the camera the dummy can be slashed and slashes itself.</summary>
    public float reach = 14.0f;
    /// <summary>The most drops of blood in flight at once.</summary>
    public int maxDrops = 700;
    /// <summary>The most drops that leave a decal in one frame; the others vanish where they land.</summary>
    public int maxLandingsPerFrame = 14;

    private const float BLADE_SPEED_FOR_FULL_DEPTH = 9.0f;
    private const float STREAK_SECONDS = 0.14f;
    private const float BLADE_STEP = 0.015f;
    private const float DROP_LIFETIME = 4.0f;
    private const float DROP_STAIN_SECONDS = 35.0f;
    private const float GUTS_COOLDOWN = 3.0f;
    private const int MAX_PIECES = 36;

    private struct Drop
    {
        public Vector3 position;
        public Vector3 velocity;
        public float size;
        public float age;
        public Matrix4x4 previous;
    }

    private sealed class Part
    {
        public Entity entity;
        public bool canBeCutOff;
        public Vector3 restPosition;
        public Quaternion restRotation;
        public Vector3 restScale;
        /// <summary>Where the part joins the body, in the body's space, and the direction out of the body there.</summary>
        public Vector3 joint;
        public Vector3 jointOut;
        public float damage;
        public bool isCutOff;
        /// <summary>The stump left on the torso; it goes when the part grows back.</summary>
        public Entity stump;
        public float cutOffTime;
        public float regrow = 1.0f;
    }

    private sealed class Wound
    {
        public Part part;
        public Vector3 point;
        public Vector3 along;
        public Vector3 normal;
        public float length;
        public float strength;
        public float age;
        public float duration;
        public bool isFountain;
        public float pending;
    }

    private struct StreakPoint
    {
        public Vector3 point;
        public float age;
    }

    private readonly List<Part> parts = new List<Part>();
    private readonly List<Wound> wounds = new List<Wound>();
    private readonly List<Drop> drops = new List<Drop>();
    private readonly List<StreakPoint> streak = new List<StreakPoint>();
    private readonly List<Entity> pieces = new List<Entity>();
    private Matrix4x4[] dropMatrices = new Matrix4x4[0];
    private Matrix4x4[] dropPrevious = new Matrix4x4[0];
    private Matrix4x4[] streakMatrices = new Matrix4x4[0];

    private CameraComponent cameraComponent;
    private Part torsoPart;

    // The stroke: the part the blade is in, where it went in, how far and fast it went.
    private bool isStroking;
    private bool isAutoStroke;
    private float strokeSeconds;
    private float strokeTravel;
    private Part contactPart;
    private Vector3 contactStart;
    private Vector3 contactLast;
    private Vector3 contactNormal;
    private float contactSpeed;
    private bool hasBladePoint;
    private Vector3 bladePoint;
    private bool hasLastRay;
    private Ray lastRay;
    private float bladeSpeed;

    // The unseen blade.
    private float idleTime;
    private float autoTimer;
    private Vector3 autoFrom;
    private Vector3 autoTo;
    private float autoProgress;
    private float autoDuration;
    private int autoCount;

    private Vector2 tilt;
    private Vector2 tiltSpeed;
    private Quaternion bodyRest;
    private float poolBlood;
    private float poolSize;
    private float lastBleedTime = -100.0f;
    private float gutsCooldown;
    private float hitStop;
    private int landingsThisFrame;

    public override void OnStart()
    {
        Decals.maxTransientDecals = 1200;
        bodyRest = body.IsValid() ? body.transform.localRotation : Quaternion.identity;
        // The built-in capsule reaches 1 from its centre along Y, the sphere 0.5.
        torsoPart = AddPart(torso, false, 1.0f);
        AddPart(head, true, 0.5f);
        AddPart(armLeft, true, 1.0f);
        AddPart(armRight, true, 1.0f);
        var cameraEntity = viewCamera.IsValid() ? viewCamera : Scene.FindEntityByName("Main Camera");
        cameraComponent = cameraEntity.IsValid() ? cameraEntity.GetComponent<CameraComponent>() : null;
        poolBlood = 0.3f;
    }

    public override void OnDestroy()
    {
        Time.timeScale = 1.0f;
    }

    public override void OnUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0.0f)
        {
            return;
        }
        landingsThisFrame = 0;
        UpdateHitStop();
        bool isWatched = IsWatched();
        UpdatePlayerBlade(isWatched, dt);
        UpdateUnseenBlade(isWatched, dt);
        UpdateWounds(dt);
        UpdateDrops(dt);
        UpdateWobble(dt);
        UpdateHealing(dt);
        UpdatePool(dt);
        DrawDrops();
        DrawStreak();
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The blade
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Draws the blade from <paramref name="from"/> to <paramref name="to"/> (world points on or through the dummy, seen
    /// from the camera) over <paramref name="seconds"/>, as a player's stroke would: faster cuts deeper, and a fast
    /// stroke across the head or an arm cuts it off. A repeatable way to slash it from a test or an agent
    /// (scripts_invoke).
    /// </summary>
    /// <returns>False while another stroke runs, or when the camera is not near the dummy and facing it.</returns>
    public bool Slash(Vector3 from, Vector3 to, float seconds)
    {
        if (isStroking || !IsWatched())
        {
            return false;
        }
        autoFrom = from;
        autoTo = to;
        autoProgress = 0.0f;
        autoDuration = Mathf.Max(seconds, 0.02f);
        idleTime = 0.0f;
        BeginStroke(true);
        return true;
    }

    private void UpdatePlayerBlade(bool isWatched, float dt)
    {
        bool isHeld = Input.IsDown(MouseButton.Left) && isWatched && cameraComponent != null;
        if (!isHeld)
        {
            if (isStroking && !isAutoStroke)
            {
                EndStroke();
            }
            return;
        }
        if (!cameraComponent.ScreenPointToRay(Input.mousePosition, out Ray ray))
        {
            return;
        }
        idleTime = 0.0f;
        if (isAutoStroke)
        {
            EndStroke();
        }
        if (!isStroking)
        {
            BeginStroke(false);
        }
        MoveBlade(ray, dt);
    }

    private void UpdateUnseenBlade(bool isWatched, float dt)
    {
        if (!isWatched || cameraComponent == null)
        {
            if (isAutoStroke)
            {
                EndStroke();
            }
            return;
        }
        if (isAutoStroke)
        {
            autoProgress = Mathf.Min(autoProgress + dt / autoDuration, 1.0f);
            float eased = autoProgress * autoProgress * (3.0f - 2.0f * autoProgress);
            Vector3 target = Vector3.Lerp(autoFrom, autoTo, eased);
            Vector3 origin = cameraComponent.transform.position;
            MoveBlade(new Ray { origin = origin, direction = (target - origin).normalized }, dt);
            if (autoProgress >= 1.0f)
            {
                EndStroke();
            }
            return;
        }
        if (isStroking)
        {
            return;
        }
        idleTime += dt;
        autoTimer -= dt;
        if (idleTime < idleSeconds || autoTimer > 0.0f)
        {
            return;
        }
        autoTimer = autoInterval * Random.Range(0.75f, 1.3f);
        PlanUnseenStroke();
    }

    /// <summary>A stroke across a part, in the plane facing the camera: every fifth one deep (it cuts off or spills).</summary>
    private void PlanUnseenStroke()
    {
        autoCount++;
        bool isDeep = autoCount % 5 == 0;
        Part target = PickTarget(isDeep);
        if (target == null)
        {
            return;
        }
        var camera = cameraComponent.transform;
        Vector3 centre = target.entity.transform.position;
        Vector3 across;
        float half;
        if (target == torsoPart)
        {
            float height = isDeep ? Random.Range(0.15f, bellyHeight - 0.05f) : Random.Range(0.1f, shoulderHeight);
            centre += torso.transform.up * height;
            float angle = Random.Range(-40.0f, 40.0f) * Mathf.Deg2Rad;
            across = camera.right * Mathf.Cos(angle) + camera.up * Mathf.Sin(angle);
            half = 0.34f;
        }
        else
        {
            // Across the part's long axis, through the part.
            Vector3 axis = target.entity.transform.up;
            across = Vector3.Cross(axis, camera.forward).normalized;
            if (across.sqrMagnitude < 0.01f)
            {
                across = camera.right;
            }
            half = 0.22f;
        }
        if (Random.Range(0.0f, 1.0f) < 0.5f)
        {
            across = -across;
        }
        autoFrom = centre - across * half;
        autoTo = centre + across * half;
        autoProgress = 0.0f;
        autoDuration = isDeep ? 0.06f : Random.Range(0.11f, 0.2f);
        BeginStroke(true);
    }

    private Part PickTarget(bool isDeep)
    {
        if (isDeep)
        {
            var attached = parts.FindAll(part => part.canBeCutOff && !part.isCutOff && part.regrow >= 1.0f);
            bool spill = attached.Count == 0 || Random.Range(0.0f, 1.0f) < 0.35f;
            return spill ? torsoPart : attached[Random.Range(0, attached.Count)];
        }
        float roll = Random.Range(0.0f, 1.0f);
        foreach (var part in parts)
        {
            if (part == torsoPart || part.isCutOff || part.regrow < 1.0f)
            {
                continue;
            }
            if (roll < 0.12f)
            {
                return part;
            }
            roll -= 0.12f;
        }
        return torsoPart;
    }

    private void BeginStroke(bool isAuto)
    {
        isStroking = true;
        isAutoStroke = isAuto;
        strokeSeconds = 0.0f;
        strokeTravel = 0.0f;
        hasBladePoint = false;
        hasLastRay = false;
        bladeSpeed = 0.0f;
        contactPart = null;
    }

    private void EndStroke()
    {
        bool isClick = !isAutoStroke && strokeSeconds < 0.25f && strokeTravel < 0.03f;
        if (contactPart != null && isClick && (contactLast - contactStart).magnitude < 0.03f)
        {
            Stab(contactPart, contactStart, contactNormal);
            contactPart = null;
        }
        FinishContact();
        isStroking = false;
        isAutoStroke = false;
    }

    /// <summary>
    /// Moves the blade to <paramref name="ray"/>, sweeping from where it was in steps of BLADE_STEP at the dummy: a fast
    /// stroke crosses an arm between two frames, and every step must see it.
    /// </summary>
    private void MoveBlade(Ray ray, float dt)
    {
        strokeSeconds += dt;
        int steps = 1;
        if (hasLastRay)
        {
            float travel = (PointOnDummyPlane(ray) - PointOnDummyPlane(lastRay)).magnitude;
            steps = Mathf.Clamp((int)Mathf.Ceil(travel / BLADE_STEP), 1, 48);
        }
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Ray step = hasLastRay
                           ? new Ray { origin = Vector3.Lerp(lastRay.origin, ray.origin, t),
                                       direction = Vector3.Lerp(lastRay.direction, ray.direction, t).normalized }
                           : ray;
            MoveBladeStep(step, dt / steps);
        }
        lastRay = ray;
        hasLastRay = true;
    }

    /// <summary>
    /// One step of the blade along <paramref name="ray"/>: into a part it starts a contact, through it the contact grows
    /// and throws blood, out of it the cut is made.
    /// </summary>
    private void MoveBladeStep(Ray ray, float dt)
    {
        var hit = Physics.Raycast(ray, 30.0f);
        Part part = hit.HasValue ? FindPart(hit.Value.entity) : null;
        Vector3 point = part != null ? hit.Value.point : PointOnDummyPlane(ray);
        if (hasBladePoint)
        {
            float travel = (point - bladePoint).magnitude;
            strokeTravel += travel;
            bladeSpeed = Mathf.Lerp(bladeSpeed, travel / Mathf.Max(dt, 0.0001f), 0.6f);
        }
        AddStreakPoint(point);
        if (part != contactPart)
        {
            FinishContact();
            if (part != null)
            {
                contactPart = part;
                contactStart = point;
                contactLast = point;
                contactNormal = hit.Value.normal;
                contactSpeed = bladeSpeed;
            }
        }
        else if (part != null)
        {
            SprayAlongBlade(point, hit.Value.normal, point - contactLast);
            contactLast = point;
            contactNormal = (contactNormal + hit.Value.normal).normalized;
            contactSpeed = Mathf.Max(contactSpeed, bladeSpeed);
        }
        bladePoint = point;
        hasBladePoint = true;
    }

    private void FinishContact()
    {
        if (contactPart == null)
        {
            return;
        }
        Part part = contactPart;
        contactPart = null;
        if ((contactLast - contactStart).magnitude < 0.04f || part.isCutOff)
        {
            return;
        }
        float depth = Mathf.Clamp(contactSpeed / BLADE_SPEED_FOR_FULL_DEPTH, 0.2f, 1.0f);
        Cut(part, contactStart, contactLast, contactNormal, depth);
    }

    /// <summary>Where <paramref name="ray"/> crosses the plane through the dummy facing the camera: the blade's depth
    /// between the parts, so its streak and speed stay smooth.</summary>
    private Vector3 PointOnDummyPlane(Ray ray)
    {
        Vector3 centre = torso.IsValid() ? torso.transform.position : transform.position;
        Vector3 facing = cameraComponent.transform.forward;
        float along = Vector3.Dot(ray.direction, facing);
        float distance = Mathf.Abs(along) > 0.0001f ? Vector3.Dot(centre - ray.origin, facing) / along : 3.0f;
        return ray.origin + ray.direction * Mathf.Clamp(distance, 0.1f, 30.0f);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Cuts
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>A cut from <paramref name="from"/> to <paramref name="to"/> on <paramref name="part"/>, of
    /// <paramref name="depth"/> 0 to 1.</summary>
    private void Cut(Part part, Vector3 from, Vector3 to, Vector3 normal, float depth)
    {
        Vector3 along = to - from;
        float length = along.magnitude;
        along /= length;
        Vector3 centre = (from + to) * 0.5f;
        float width = Mathf.Lerp(0.05f, 0.12f, depth);
        var gash = Decals.Spawn(cutMaterial, centre, normal, new Vector2(length + width, width * 1.6f), RollTowards(normal, along),
                                healSeconds + Random.Range(0.0f, 4.0f), 3.0f, part.entity);
        if (gash != null)
        {
            gash.depth = Mathf.Max(0.12f, length * 0.6f);
        }
        if (depth > 0.35f && part != PartOf(head))
        {
            // Blood runs down from a few places along the cut.
            int runs = depth > 0.7f ? 3 : 2;
            for (int i = 0; i < runs; i++)
            {
                float runLength = Random.Range(0.15f, 0.25f + 0.25f * depth);
                Vector3 start = from + (to - from) * Random.Range(0.15f, 0.85f);
                Decals.Spawn(runMaterial, start - Vector3.up * (runLength * 0.42f), normal, new Vector2(Random.Range(0.05f, 0.1f), runLength),
                             0.0f, healSeconds, 3.0f, part.entity);
            }
        }
        wounds.Add(new Wound
        {
            part = part,
            point = ToLocal(part.entity.transform, centre),
            along = part.entity.transform.InverseTransformDirection(along),
            normal = part.entity.transform.InverseTransformDirection(normal),
            length = length,
            strength = depth,
            duration = 2.5f + 4.0f * depth,
        });
        ThrowOffBlade(from, to, normal, depth);
        Push(along, depth);
        if (depth > 0.65f)
        {
            hitStop = 0.05f;
        }
        part.damage += depth;
        if (part.canBeCutOff && (depth > 0.8f || part.damage > 1.8f))
        {
            CutOff(part, along, depth);
        }
        else if (part == torsoPart && depth > 0.6f && IsBelly(centre) && gutsCooldown <= 0.0f)
        {
            SpillGuts(centre, normal, along);
        }
    }

    private void Stab(Part part, Vector3 point, Vector3 normal)
    {
        float angle = Random.Range(0.0f, 360.0f);
        var gash = Decals.Spawn(cutMaterial, point, normal, new Vector2(0.09f, 0.06f), angle, healSeconds, 3.0f, part.entity);
        if (gash != null)
        {
            gash.depth = 0.12f;
        }
        EmitDrops(point + normal * 0.015f, normal * 2.2f, 0.6f, 26, 0.004f, 0.012f);
        wounds.Add(new Wound
        {
            part = part,
            point = ToLocal(part.entity.transform, point),
            along = Vector3.zero,
            normal = part.entity.transform.InverseTransformDirection(normal),
            length = 0.0f,
            strength = 0.5f,
            duration = 4.0f,
        });
        Push(-normal, 0.4f);
    }

    /// <summary>
    /// The arc of blood a slash flings as the blade leaves the cut: along the stroke and out of the surface, more and
    /// faster the deeper the cut, so it lands in a line across the floor and the walls.
    /// </summary>
    private void ThrowOffBlade(Vector3 from, Vector3 to, Vector3 normal, float depth)
    {
        Vector3 along = (to - from).normalized;
        int count = (int)(Mathf.Lerp(20.0f, 90.0f, depth) * Mathf.Clamp((to - from).magnitude / 0.3f, 0.5f, 1.5f));
        for (int i = 0; i < count; i++)
        {
            float t = Random.Range(0.0f, 1.0f);
            Vector3 start = Vector3.Lerp(from, to, t) + normal * 0.015f;
            // Later along the cut, faster: the blade has the most speed as it leaves.
            float speed = Mathf.Lerp(1.0f, 5.5f, depth) * Mathf.Lerp(0.4f, 1.1f, t) * Random.Range(0.6f, 1.2f);
            Vector3 velocity = along * speed + normal * Random.Range(0.3f, 1.4f) + Vector3.up * Random.Range(0.3f, 1.5f) * depth;
            EmitDrop(start, velocity + Random.insideUnitSphere * 0.3f, Random.Range(0.003f, 0.013f));
        }
    }

    /// <summary>Blood thrown off the blade as it moves <paramref name="moved"/> through a part.</summary>
    private void SprayAlongBlade(Vector3 point, Vector3 normal, Vector3 moved)
    {
        float distance = moved.magnitude;
        if (distance < 0.0001f)
        {
            return;
        }
        float depth = Mathf.Clamp01(bladeSpeed / BLADE_SPEED_FOR_FULL_DEPTH);
        int count = Mathf.Clamp((int)(distance * 160.0f * (0.3f + depth)), 1, 30);
        Vector3 direction = moved / distance;
        for (int i = 0; i < count; i++)
        {
            Vector3 start = point - moved * Random.Range(0.0f, 1.0f) + normal * 0.015f;
            Vector3 velocity = direction * bladeSpeed * Random.Range(0.15f, 0.45f) + normal * Random.Range(0.4f, 1.8f) * (0.5f + depth) +
                               Vector3.up * Random.Range(0.0f, 0.8f);
            EmitDrop(start, velocity + Random.insideUnitSphere * 0.4f, Random.Range(0.003f, 0.011f));
        }
    }

    private void CutOff(Part part, Vector3 along, float depth)
    {
        part.isCutOff = true;
        part.cutOffTime = Time.time;
        var bodyTransform = body.transform;
        Vector3 joint = bodyTransform.position + bodyTransform.rotation * part.joint;
        Vector3 jointOut = (bodyTransform.rotation * part.jointOut).normalized;
        var partTransform = part.entity.transform;
        partTransform.SetParent(Entity.Invalid, true);
        var physics = part.entity.GetComponent<PhysicsComponent>();
        if (physics != null)
        {
            physics.bodyType = RigidbodyType.Dynamic;
            physics.velocity = along * Random.Range(1.5f, 3.0f) * depth + jointOut * 1.2f + Vector3.up * 1.4f;
            physics.angularVelocity = Random.insideUnitSphere * 9.0f;
        }
        var stump = Decals.Spawn(stumpMaterial, joint, jointOut, new Vector2(0.17f, 0.17f), Random.Range(0.0f, 360.0f), 0.0f, 2.0f, torso);
        part.stump = stump != null ? stump.owner : Entity.Invalid;
        Decals.Spawn(stumpMaterial, joint, -jointOut, new Vector2(0.15f, 0.15f), Random.Range(0.0f, 360.0f), healSeconds, 2.0f, part.entity);
        wounds.Add(new Wound
        {
            part = torsoPart,
            point = ToLocal(torso.transform, joint),
            along = Vector3.zero,
            normal = torso.transform.InverseTransformDirection(jointOut),
            strength = 1.0f,
            duration = 3.5f,
            isFountain = true,
        });
        SpawnChunks(joint, jointOut, 2);
        hitStop = 0.08f;
    }

    private void SpillGuts(Vector3 point, Vector3 normal, Vector3 along)
    {
        gutsCooldown = GUTS_COOLDOWN;
        if (gutsPrefab != null)
        {
            var guts = Scene.Instantiate(gutsPrefab, point + normal * 0.06f);
            if (guts.IsValid())
            {
                // The prefab's segments run up its Y axis: out of the wound, then they fall and hang.
                guts.transform.rotation = Quaternion.LookRotation(along, normal);
                Entity first = Entity.Invalid;
                foreach (var segment in guts.transform.children)
                {
                    if (!segment.HasComponent<PhysicsComponent>())
                    {
                        continue;
                    }
                    if (!first.IsValid())
                    {
                        first = segment;
                    }
                    Throw(segment, normal * Random.Range(0.6f, 1.6f) + Vector3.down * 0.2f + along * Random.Range(-0.4f, 0.4f), 4.0f);
                }
                TieToWound(first, point);
                KeepPiece(guts);
            }
        }
        SpawnChunks(point, normal, 3);
        EmitDrops(point + normal * 0.02f, normal * 1.6f + Vector3.down * 0.3f, 0.7f, 70, 0.005f, 0.016f);
        wounds.Add(new Wound
        {
            part = torsoPart,
            point = ToLocal(torso.transform, point),
            along = torso.transform.InverseTransformDirection(along),
            normal = torso.transform.InverseTransformDirection(normal),
            length = 0.15f,
            strength = 1.0f,
            duration = 6.0f,
        });
    }

    /// <summary>Ties the first length of guts to the torso at the wound with a ball joint that tears when pulled hard.</summary>
    private void TieToWound(Entity first, Vector3 point)
    {
        if (!first.IsValid())
        {
            return;
        }
        var tether = Scene.CreateEntity("Guts Tether");
        tether.transform.position = point;
        var joint = tether.AddComponent<JointComponent>();
        joint.type = JointType.Ball;
        joint.bodyA = torso;
        joint.bodyB = first;
        joint.breakForce = 60.0f;
        tether.transform.SetParent(first.transform.parent, true);
    }

    private void SpawnChunks(Vector3 point, Vector3 normal, int count)
    {
        if (chunkPrefab == null)
        {
            return;
        }
        for (int i = 0; i < count; i++)
        {
            var chunk = Scene.Instantiate(chunkPrefab, point + normal * 0.05f + Random.insideUnitSphere * 0.03f);
            if (!chunk.IsValid())
            {
                continue;
            }
            float scale = Random.Range(0.7f, 1.3f);
            chunk.transform.localScale = chunk.transform.localScale * scale;
            chunk.transform.rotation = Quaternion.Euler(Random.Range(0.0f, 360.0f), Random.Range(0.0f, 360.0f), Random.Range(0.0f, 360.0f));
            Throw(chunk, normal * Random.Range(1.5f, 3.0f) + Vector3.up * Random.Range(0.8f, 2.0f) + Random.insideUnitSphere, 10.0f);
            KeepPiece(chunk);
        }
    }

    private static void Throw(Entity entity, Vector3 velocity, float spin)
    {
        var physics = entity.GetComponent<PhysicsComponent>();
        if (physics == null)
        {
            return;
        }
        physics.velocity = velocity;
        physics.angularVelocity = Random.insideUnitSphere * spin;
    }

    /// <summary>Keeps a thrown piece for the heal time; past the most pieces at once the oldest goes now.</summary>
    private void KeepPiece(Entity piece)
    {
        pieces.RemoveAll(kept => !Scene.IsEntityValid(kept));
        if (pieces.Count >= MAX_PIECES)
        {
            Scene.DestroyEntity(pieces[0]);
            pieces.RemoveAt(0);
        }
        pieces.Add(piece);
        Scene.DestroyEntity(piece, healSeconds + Random.Range(0.0f, 5.0f));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Bleeding, drops and the pool
    // -----------------------------------------------------------------------------------------------------------------

    private void UpdateWounds(float dt)
    {
        for (int i = wounds.Count - 1; i >= 0; i--)
        {
            var wound = wounds[i];
            wound.age += dt;
            if (wound.age >= wound.duration || wound.part.isCutOff && wound.part != torsoPart)
            {
                wounds.RemoveAt(i);
                continue;
            }
            float fading = 1.0f - wound.age / wound.duration;
            float rate = wound.isFountain ? 110.0f * (0.55f + 0.45f * Mathf.Sin(wound.age * 9.0f)) : 16.0f * wound.strength;
            wound.pending += rate * fading * dt;
            var partTransform = wound.part.entity.transform;
            Vector3 normal = partTransform.TransformDirection(wound.normal).normalized;
            Vector3 along = partTransform.TransformDirection(wound.along);
            while (wound.pending >= 1.0f)
            {
                wound.pending -= 1.0f;
                Vector3 point = ToWorld(partTransform, wound.point) + along * (Random.Range(-0.5f, 0.5f) * wound.length) + normal * 0.015f;
                Vector3 velocity = wound.isFountain
                                       ? normal * Random.Range(1.6f, 3.0f) * (0.4f + fading) + Random.insideUnitSphere * 0.35f
                                       : normal * Random.Range(0.05f, 0.3f) + Vector3.down * 0.2f + Random.insideUnitSphere * 0.05f;
                EmitDrop(point, velocity, wound.isFountain ? Random.Range(0.004f, 0.012f) : Random.Range(0.004f, 0.009f));
            }
            lastBleedTime = Time.time;
        }
    }

    private void EmitDrops(Vector3 point, Vector3 velocity, float spread, int count, float minSize, float maxSize)
    {
        for (int i = 0; i < count; i++)
        {
            EmitDrop(point, velocity * Random.Range(0.6f, 1.2f) + Random.insideUnitSphere * spread, Random.Range(minSize, maxSize));
        }
    }

    private void EmitDrop(Vector3 position, Vector3 velocity, float size)
    {
        if (drops.Count >= maxDrops)
        {
            return;
        }
        drops.Add(new Drop { position = position, velocity = velocity, size = size, previous = DropMatrix(position, velocity, size) });
    }

    private void UpdateDrops(float dt)
    {
        Vector3 gravity = new Vector3(0.0f, -9.81f, 0.0f);
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            var drop = drops[i];
            drop.velocity += gravity * dt;
            drop.velocity *= 1.0f / (1.0f + 0.6f * dt);
            Vector3 step = drop.velocity * dt;
            float distance = step.magnitude;
            if (distance > 0.00001f)
            {
                var hit = Physics.Raycast(drop.position, step, distance + drop.size);
                bool isLeavingDummy = hit.HasValue && drop.age < 0.06f && FindPart(hit.Value.entity) != null;
                if (hit.HasValue && !isLeavingDummy)
                {
                    Land(drop, hit.Value);
                    RemoveDrop(i);
                    continue;
                }
            }
            drop.previous = DropMatrix(drop.position, drop.velocity, drop.size);
            drop.position += step;
            drop.age += dt;
            if (drop.age > DROP_LIFETIME || drop.position.y < -2.0f)
            {
                RemoveDrop(i);
                continue;
            }
            drops[i] = drop;
        }
    }

    private void RemoveDrop(int index)
    {
        int last = drops.Count - 1;
        drops[index] = drops[last];
        drops.RemoveAt(last);
    }

    /// <summary>A drop hits a surface: a stain stretched along its travel, longer the more it glances off.</summary>
    private void Land(Drop drop, RaycastHit hit)
    {
        Vector3 normal = hit.normal.normalized;
        if (normal.y > 0.7f && pool.IsValid() && (hit.point - pool.transform.position).magnitude < poolSize * 0.5f + 0.4f)
        {
            poolBlood += drop.size * 2.5f;
        }
        if (landingsThisFrame >= maxLandingsPerFrame)
        {
            return;
        }
        landingsThisFrame++;
        float speed = drop.velocity.magnitude;
        Vector3 direction = drop.velocity / Mathf.Max(speed, 0.0001f);
        Vector3 tangent = direction - normal * Vector3.Dot(direction, normal);
        float glance = 1.0f - Mathf.Abs(Vector3.Dot(direction, normal));
        float size = Mathf.Clamp(drop.size * Random.Range(5.0f, 8.0f) * (0.7f + speed * 0.06f), 0.015f, 0.18f);
        float stretch = 1.0f + glance * 2.5f;
        float roll = tangent.sqrMagnitude > 0.000001f ? RollTowards(normal, tangent) : Random.Range(0.0f, 360.0f);
        Entity attachTo = IsMoving(hit.entity) ? hit.entity : Entity.Invalid;
        Decals.Spawn(dropMaterial, hit.point, normal, new Vector2(size * stretch, size), roll, DROP_STAIN_SECONDS + Random.Range(0.0f, 8.0f),
                     4.0f, attachTo);
    }

    private void UpdatePool(float dt)
    {
        if (!pool.IsValid())
        {
            return;
        }
        // It dries slowly once the dummy stops bleeding.
        if (Time.time - lastBleedTime > 8.0f)
        {
            poolBlood = Mathf.Max(0.3f, poolBlood - dt * 0.02f);
        }
        poolBlood = Mathf.Min(poolBlood, 6.0f);
        float target = Mathf.Clamp(Mathf.Sqrt(poolBlood) * 1.6f, 0.9f, 3.4f);
        poolSize = Mathf.Lerp(poolSize <= 0.0f ? target : poolSize, target, 1.0f - Mathf.Exp(-dt * 0.8f));
        var decal = pool.GetComponent<DecalComponent>();
        if (decal != null)
        {
            decal.size = new Vector2(poolSize, poolSize * 0.85f);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The dummy's body
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>A hit pushes the body over along <paramref name="direction"/>; the post springs it back.</summary>
    private void Push(Vector3 direction, float strength)
    {
        Vector3 flat = new Vector3(direction.x, 0.0f, direction.z);
        if (flat.sqrMagnitude < 0.0001f)
        {
            return;
        }
        flat = flat.normalized;
        tiltSpeed += new Vector2(flat.z, -flat.x) * (40.0f + 80.0f * strength);
    }

    private void UpdateWobble(float dt)
    {
        if (!body.IsValid())
        {
            return;
        }
        const float stiffness = 90.0f;
        const float damping = 6.0f;
        tiltSpeed += (-tilt * stiffness - tiltSpeed * damping) * dt;
        tilt += tiltSpeed * dt;
        tilt = new Vector2(Mathf.Clamp(tilt.x, -25.0f, 25.0f), Mathf.Clamp(tilt.y, -25.0f, 25.0f));
        body.transform.localRotation = Quaternion.Euler(tilt.x, 0.0f, tilt.y) * bodyRest;
    }

    /// <summary>Cut-off parts grow back after the heal time, and every part's damage heals.</summary>
    private void UpdateHealing(float dt)
    {
        gutsCooldown -= dt;
        foreach (var part in parts)
        {
            if (part.isCutOff && Time.time - part.cutOffTime > healSeconds)
            {
                Regrow(part);
            }
            if (part.regrow < 1.0f)
            {
                part.regrow = Mathf.Min(part.regrow + dt * 1.6f, 1.0f);
                float grown = part.regrow * part.regrow * (3.0f - 2.0f * part.regrow);
                part.entity.transform.localScale = part.restScale * Mathf.Max(grown, 0.01f);
            }
            part.damage = Mathf.Max(0.0f, part.damage - dt * 0.03f);
        }
    }

    private void Regrow(Part part)
    {
        if (Scene.IsEntityValid(part.stump))
        {
            Scene.DestroyEntity(part.stump);
        }
        part.stump = Entity.Invalid;
        part.isCutOff = false;
        part.damage = 0.0f;
        part.regrow = 0.0f;
        var physics = part.entity.GetComponent<PhysicsComponent>();
        if (physics != null)
        {
            physics.velocity = Vector3.zero;
            physics.angularVelocity = Vector3.zero;
            physics.bodyType = RigidbodyType.Kinematic;
        }
        var partTransform = part.entity.transform;
        partTransform.SetParent(body, false);
        partTransform.localPosition = part.restPosition;
        partTransform.localRotation = part.restRotation;
        partTransform.localScale = part.restScale * 0.01f;
        // The stains it gathered while it lay on the floor go with it.
        foreach (var child in partTransform.children)
        {
            if (child.HasComponent<DecalComponent>())
            {
                Scene.DestroyEntity(child);
            }
        }
    }

    /// <summary>
    /// Adds a part of the dummy. It joins the body at the end of its long axis nearer the torso's upright axis, a little
    /// inside its mesh's reach (<paramref name="meshReach"/> from its centre along Y, before scaling).
    /// </summary>
    private Part AddPart(Entity entity, bool canBeCutOff, float meshReach)
    {
        if (!entity.IsValid())
        {
            return null;
        }
        var partTransform = entity.transform;
        var part = new Part
        {
            entity = entity,
            canBeCutOff = canBeCutOff,
            restPosition = partTransform.localPosition,
            restRotation = partTransform.localRotation,
            restScale = partTransform.localScale,
        };
        Vector3 reachAlong = part.restRotation * Vector3.up * (part.restScale.y * meshReach * 0.85f);
        Vector3 axis = torsoPart != null ? torsoPart.restPosition : Vector3.zero;
        Vector3 endA = part.restPosition + reachAlong;
        Vector3 endB = part.restPosition - reachAlong;
        // The end nearer the body's upright axis; on the axis (the head), the lower one.
        float offA = new Vector2(endA.x - axis.x, endA.z - axis.z).magnitude;
        float offB = new Vector2(endB.x - axis.x, endB.z - axis.z).magnitude;
        bool isA = Mathf.Abs(offA - offB) > 0.01f ? offA < offB : endA.y < endB.y;
        part.joint = isA ? endA : endB;
        Vector3 outward = part.restPosition - part.joint;
        part.jointOut = outward.sqrMagnitude > 0.000001f ? outward.normalized : Vector3.up;
        parts.Add(part);
        return part;
    }

    private Part FindPart(Entity entity)
    {
        foreach (var part in parts)
        {
            if (part.entity == entity)
            {
                return part;
            }
        }
        return null;
    }

    private Part PartOf(Entity entity) => FindPart(entity);

    private bool IsBelly(Vector3 point)
    {
        var frame = torso.transform;
        return Vector3.Dot(point - frame.position, frame.up) < bellyHeight;
    }

    /// <summary>Whether <paramref name="entity"/> can move (a part, a piece): a stain on it must go with it.</summary>
    private static bool IsMoving(Entity entity)
    {
        var physics = entity.IsValid() ? entity.GetComponent<PhysicsComponent>() : null;
        return physics != null && physics.bodyType != RigidbodyType.Static;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Drawing
    // -----------------------------------------------------------------------------------------------------------------

    private static Matrix4x4 DropMatrix(Vector3 position, Vector3 velocity, float size)
    {
        float speed = velocity.magnitude;
        Quaternion rotation = speed > 0.001f ? Quaternion.LookRotation(velocity / speed) : Quaternion.identity;
        return Matrix4x4.TRS(position, rotation, new Vector3(size, size, size * (1.0f + speed * 0.05f)));
    }

    private void DrawDrops()
    {
        if (dropletMesh == null || drops.Count == 0)
        {
            return;
        }
        if (dropMatrices.Length < drops.Count)
        {
            dropMatrices = new Matrix4x4[maxDrops];
            dropPrevious = new Matrix4x4[maxDrops];
        }
        for (int i = 0; i < drops.Count; i++)
        {
            dropMatrices[i] = DropMatrix(drops[i].position, drops[i].velocity, drops[i].size);
            dropPrevious[i] = drops[i].previous;
        }
        Graphics.DrawMeshInstanced(dropletMesh, dropletMaterial, new ReadOnlySpan<Matrix4x4>(dropMatrices, 0, drops.Count),
                                   new ReadOnlySpan<Matrix4x4>(dropPrevious, 0, drops.Count), false);
    }

    private void AddStreakPoint(Vector3 point)
    {
        streak.Add(new StreakPoint { point = point, age = 0.0f });
    }

    /// <summary>The blade's streak: thin glowing boxes between the last points of the stroke, thinning as they age.</summary>
    private void DrawStreak()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = streak.Count - 1; i >= 0; i--)
        {
            var point = streak[i];
            point.age += dt;
            if (point.age > STREAK_SECONDS)
            {
                streak.RemoveAt(i);
                continue;
            }
            streak[i] = point;
        }
        if (streakMesh == null || streak.Count < 2)
        {
            return;
        }
        if (streakMatrices.Length < streak.Count)
        {
            streakMatrices = new Matrix4x4[streak.Count * 2];
        }
        int count = 0;
        for (int i = 1; i < streak.Count; i++)
        {
            Vector3 segment = streak[i].point - streak[i - 1].point;
            float length = segment.magnitude;
            if (length < 0.0005f || length > 1.5f)
            {
                continue;
            }
            float thickness = 0.014f * (1.0f - streak[i].age / STREAK_SECONDS);
            Vector3 middle = (streak[i].point + streak[i - 1].point) * 0.5f;
            streakMatrices[count++] = Matrix4x4.TRS(middle, Quaternion.LookRotation(segment / length), new Vector3(thickness, thickness * 0.35f, length));
        }
        if (count > 0)
        {
            Graphics.DrawMeshInstanced(streakMesh, streakMaterial, new ReadOnlySpan<Matrix4x4>(streakMatrices, 0, count), default, false);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Whether the camera is near the dummy and looking its way: only then can it be slashed.</summary>
    private bool IsWatched()
    {
        if (cameraComponent == null || !torso.IsValid())
        {
            return false;
        }
        var camera = cameraComponent.transform;
        Vector3 toDummy = torso.transform.position - camera.position;
        float distance = toDummy.magnitude;
        return distance < reach && Vector3.Dot(camera.forward, toDummy / Mathf.Max(distance, 0.001f)) > 0.5f;
    }

    /// <summary>A short slow-down on a heavy hit, so it lands with weight.</summary>
    private void UpdateHitStop()
    {
        if (hitStop > 0.0f)
        {
            hitStop -= Time.unscaledDeltaTime;
            Time.timeScale = hitStop > 0.0f ? 0.08f : 1.0f;
        }
    }

    /// <summary>
    /// The roll Decals.Spawn takes to lay a decal's width along <paramref name="direction"/> on a surface of
    /// <paramref name="normal"/>: it faces the decal into the surface with world up (or +Z on floors) as its height at
    /// roll 0.
    /// </summary>
    private static float RollTowards(Vector3 normal, Vector3 direction)
    {
        Vector3 forward = -normal.normalized;
        Vector3 upHint = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f ? new Vector3(0.0f, 0.0f, 1.0f) : Vector3.up;
        Vector3 x = Vector3.Cross(upHint, forward).normalized;
        Vector3 y = Vector3.Cross(forward, x);
        return Mathf.Atan2(Vector3.Dot(direction, y), Vector3.Dot(direction, x)) * Mathf.Rad2Deg;
    }

    private static Vector3 ToLocal(TransformComponent frame, Vector3 point)
    {
        Vector3 local = Quaternion.Inverse(frame.rotation) * (point - frame.position);
        Vector3 scale = frame.scale;
        return new Vector3(local.x / scale.x, local.y / scale.y, local.z / scale.z);
    }

    private static Vector3 ToWorld(TransformComponent frame, Vector3 local)
    {
        Vector3 scale = frame.scale;
        return frame.position + frame.rotation * new Vector3(local.x * scale.x, local.y * scale.y, local.z * scale.z);
    }
}
