using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    //NOT MINE
    //ALL CODE REALTING TO ORBITING THE CAMERA AROUND A POINT IS THIS GUY
    //All that i ahve don is added the trnation stuff and big fixing
    //https://gist.github.com/lalamax3d/57058738d57ea084bcf1bd2b954fd570?#file-cameracontrol-cs-L7
    //Go here for code
    //Made by lalamax3d

    public bool focusOrbit = false;

    [Serializable]
    public class WorldTarget
    {
        [Tooltip("Local Unity Transform used for all orbit/pan/zoom math (RotateAround, Lerp, etc). Assign any EMPTY GameObject to use as the pivot - don't assign the planet's own transform, the planet script and this script would both be writing to it.")]
        public Transform transform;

        [Tooltip("If true, 'transform' position is recalculated every frame from worldPosition and the camera's worldPos - stays correct at any distance from the origin. If false, transform is left exactly as assigned/moved (plain local pivot - original behaviour).")]
        public bool useWorldPosition = false;

        [Tooltip("True real-world position, same units as kmPerUnit (km by default). Only read when useWorldPosition is true. Set this from your planet/physics script to orbit a specific body - update it every frame if that body moves.")]
        public double3 worldPosition;
    }

    [Header("Orbit Target")]
    public WorldTarget target = new WorldTarget();

    public float distance = 10.0f; // Initial distance from the target
    public float zoomSpeed = -2.0f; // Speed of zooming
    public float panSpeed = 0.3f; // Speed of panning (orbit-mode LMB/RMB pan)
    public float orbitSpeed = 5.0f; // Speed of orbiting
    public bool orbitWithLMB = true; // Orbit with Left Mouse Button (LMB) if true, else Right Mouse Button (RMB)
    public bool updateTargetOnClick = true; // Update orbit target on mouse click
    public bool updateTargetOnPan = true; // Update orbit target on pan

    [Header("Zoom")]
    [Tooltip("Camera won't zoom closer than this to whatever the center-screen ray hits. In Unity units - tune to taste.")]
    [SerializeField] private float minZoomClipDistance = 0.1f;
    [Tooltip("Smallest distance a single zoom tick can cover, in Unity units. Stops zoom grinding to a halt as you approach something, and stops it crawling when backing out from close range.")]
    [SerializeField] private float minZoomStep = 0.1f;

    [SerializeField] private float orbitEnterSpeed = 5f;
    [SerializeField] private float orbitDistance = 10f;
    [SerializeField] private float orbitStopDistance = 0.1f;
    private bool transitioningToOrbit = false;

    private bool movingToOrbit = false;
    private Vector3 orbitDirection; // unit direction from target to camera, fixed for the duration of the transition


    private Vector3 lastMousePosition;
    private float lastClickTime;
    private const float doubleClickTime = 0.3f; // Time interval to detect double-click

    private Camera cam;
    private float cameraYaw;
    private float cameraPitch;
    private bool justExitedOrbit = false;
    private bool ignoreMiddleMouseUntilRelease = false;

    // ---- Double-precision "true position" tracking (solar-system scale) ----
    [Header("Double Precision / Camera-Relative Rendering")]
    [Tooltip("World position the transform gets pinned back to every frame, so it never accumulates large float coordinates.")]
    [SerializeField] private Vector3 anchorPosition = Vector3.zero;

    [Tooltip("How many real km one Unity unit represents. Your Neptune trip = 100.")]
    [SerializeField] private SceneManager simulation;
    private double kmPerUnit;
    public double KmPerUnit => kmPerUnit;

    [Tooltip("The camera's true position in real-world units (km by default), safe at any scale. Read this from other scripts - e.g. to decide which planets are near enough to render.")]
    public double3 worldPos;

    public Vector3 FrameDeltaPosition { get; private set; }      // this frame's movement in Unity units (small, precise)
    public Quaternion FrameDeltaRotation { get; private set; }   // this frame's rotation change

    private Quaternion lastRawRotation;

    // ---- Following a moving orbit target ----
    // Following is "pin the camera to the target's true position + its current
    // offset from the pivot" (see FinalizeFrame), instead of adding up per-frame deltas.
    [Header("Follow Moving Orbit Target")]
    [Tooltip("While orbiting (or transitioning into orbit), the camera stays pinned to the target's real worldPosition every frame, so a moving planet/moon doesn't slip out from under you. Only applies when target.useWorldPosition is true.")]
    [SerializeField] private bool followMovingTarget = true;

    public PhysicsManager.physicsObject orbitTar;

    // Deliberate pan / double-click shifts of the pivot, kept as an offset (km) FROM
    // target.worldPosition instead of being written into it - so they survive your
    // planet script overwriting target.worldPosition every frame.
    private double3 pivotOffsetKm;

    // Frame FinalizeFrame() last ran on, so LateUpdate only acts as a fallback on frames
    // where camUpdate() wasn't called.
    private int lastFinalizeFrame = -1;


    void Awake()
    {
        cam = GetComponent<Camera>();
        kmPerUnit = (double)simulation.scale;
    }

    private void Start()
    {
        Vector3 angles = transform.eulerAngles;

        cameraYaw = angles.y;
        cameraPitch = NormalizeAngle(angles.x);

        // Wherever you placed the camera in the Editor becomes its starting
        // "true" position, converted to real km via kmPerUnit. From here on
        // the transform itself is pinned to anchorPosition, and worldPos
        // (in km) is what actually accumulates over solar-system distances.
        worldPos = new double3(transform.position.x, transform.position.y, transform.position.z) * kmPerUnit;
        transform.position = anchorPosition;

        lastRawRotation = transform.rotation;

        ResetScaleMemory();
    }

    // ORDER MATTERS. Each frame, do this in this order:
    //   1) advance your bodies / write target.worldPosition
    //   2) camUpdate()   <- input, orbit-follow and worldPos are all final when this returns
    //   3) place your objects relative to worldPos
    // If objects get placed BEFORE camUpdate() runs, a moving orbit target will look one frame behind.
    public void camUpdate()
    {
        // Your objects were re-placed around the camera after last frame's camUpdate(), but
        // Unity only pushes moved transforms into the physics world on a physics step (or when
        // told to). Without this, every raycast / overlap test below measures against where the
        // colliders WERE - which is what made things go wrong around moving planets.
        Physics.SyncTransforms();

        if (movingToOrbit)
        {
            MoveToOrbit();
        }
        else if (focusOrbit)
        {
            HandleFocusMouseInput();
        }
        else
        {
            NormalUpdate();
        }

        FinalizeFrame();
    }

    // Fallback only - does nothing on frames where camUpdate() already ran.
    private void LateUpdate()
    {
        if (lastFinalizeFrame != Time.frameCount)
        {
            FinalizeFrame();
        }
    }

    private void FinalizeFrame()
    {
        lastFinalizeFrame = Time.frameCount;

        FrameDeltaPosition = transform.position - anchorPosition;
        FrameDeltaRotation = transform.rotation * Quaternion.Inverse(lastRawRotation);
        lastRawRotation = transform.rotation;

        if (ShouldFollowTarget())
        {
            updateWorldPos(target);
            // Orbiting something that may be moving on its own. Instead of adding up
            // deltas, pin the camera to: (target's true position RIGHT NOW) + (where the
            // camera sits relative to the pivot after this frame's orbit / zoom / pan).
            // target.transform.position still holds last frame's pivot position here, so
            // this keeps the exact same offset while the target itself moves.
            // (Both positions are in the same frame - anchorPosition included - so this
            // is also correct when anchorPosition isn't zero.)
            Vector3 offsetFromPivot = transform.position - target.transform.position;
            worldPos = target.worldPosition + pivotOffsetKm + ToDouble3(offsetFromPivot) * kmPerUnit;
        }
        else
        {
            // Free flight: fold this frame's small, precise delta into the big, precise total (real km).
            worldPos += ToDouble3(FrameDeltaPosition) * kmPerUnit;
        }

        SyncTargetTransform();

        // The camera never repositions anything else in the scene - worldPos
        // above is all any other script needs to place itself relative to the camera.
        transform.position = anchorPosition;
    }

    private bool ShouldFollowTarget()
    {
        if (!followMovingTarget || target == null || target.transform == null || !target.useWorldPosition)
            return false;

        return focusOrbit || movingToOrbit || transitioningToOrbit;
    }

    private void updateWorldPos(WorldTarget tar)
    {
        tar.worldPosition = orbitTar.pos;
    }

    private void SyncTargetTransform()
    {
        if (target == null || target.transform == null || !target.useWorldPosition) return;
        updateWorldPos(target);
        // Pivot expressed relative to the camera, which sits at anchorPosition.
        double3 relativeUnits = (target.worldPosition + pivotOffsetKm - worldPos) / kmPerUnit;
        target.transform.position = anchorPosition + new Vector3((float)relativeUnits.x, (float)relativeUnits.y, (float)relativeUnits.z);
    }

    // Kept so existing calls still compile. Call it from whatever script assigns a
    // brand-new orbit target (a different planet/moon) - it clears any pan/click
    // offset that belonged to the old one.
    public void ResetFollowTracking()
    {
        pivotOffsetKm = double3.zero;
    }

    private void NudgeTarget(Vector3 unityDelta)
    {
        if (target == null || target.transform == null) return;

        target.transform.position += unityDelta;

        // Stored as an offset from target.worldPosition (not written into it), so it
        // sticks to the body even if your planet script rewrites worldPosition each frame.
        if (target.useWorldPosition)
        {
            pivotOffsetKm += ToDouble3(unityDelta) * kmPerUnit;
        }
    }

    private static double3 ToDouble3(Vector3 v)
    {
        return new double3(v.x, v.y, v.z);
    }

    private float NormalizeAngle(float angle)
    {
        if (angle > 180f)
            angle -= 360f;

        return angle;
    }

    public void EnterOrbitMode()
    {
        transitioningToOrbit = true;
        focusOrbit = false;

        currentSpeed = 0;
        moving = false;
        zoomPending = 0f;

        // A fresh orbit starts on the target's true position (drops any pan /
        // double-click offset from a previous orbit), and the pivot transform is refreshed
        // so the direction below isn't worked out from last frame's stale position.
        pivotOffsetKm = double3.zero;
        SyncTargetTransform();

        Vector3 direction = transform.position - target.transform.position;

        if (direction.magnitude < 0.1f)
            direction = -transform.forward;

        orbitDirection = direction.normalized;

        movingToOrbit = true;
    }

    private void MoveToOrbit()
    {
        // Recomputed live every frame from the target's current position
        // (which FinalizeFrame/SyncTargetTransform already keep correct even
        // while the target is moving) - this is what was causing the
        // "buggy" entry: the old code aimed at a destination point captured
        // once at the start, which went stale if the target kept moving
        // during the Lerp-in.
        Vector3 livePivot = target.transform.position;
        Vector3 liveTargetPosition = livePivot + orbitDirection * distance;
        Quaternion liveTargetRotation = Quaternion.LookRotation(livePivot - liveTargetPosition);

        transform.position = Vector3.Lerp(
            transform.position,
            liveTargetPosition,
            Time.deltaTime * orbitEnterSpeed
        );

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            liveTargetRotation,
            Time.deltaTime * orbitEnterSpeed
        );


        if (Vector3.Distance(transform.position, liveTargetPosition) < orbitStopDistance &&
           Quaternion.Angle(transform.rotation, liveTargetRotation) < 1f)
        {
            transform.position = liveTargetPosition;
            transform.rotation = liveTargetRotation;

            movingToOrbit = false;
            transitioningToOrbit = false;
            focusOrbit = true;

            lastMousePosition = Input.mousePosition;
            currentSpeed = 0;
            moving = false;
        }
    }

    void HandleFocusMouseInput()
    {
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
        {
            lastMousePosition = Input.mousePosition;
        }

        if (Input.GetMouseButton(orbitWithLMB ? 0 : 1))
        {
            Orbit();
        }
        else if (Input.GetMouseButton(orbitWithLMB ? 1 : 0))
        {
            Pan();
        }
        else if (Input.GetMouseButtonDown(2))
        {
            ExitOrbitMode();

            // clear any mouse movement left over
            Input.GetAxis("Mouse X");
            Input.GetAxis("Mouse Y");

            return;
        }

        Zoom();

        if (updateTargetOnClick && Input.GetMouseButtonDown(0))
        {
            float timeSinceLastClick = Time.time - lastClickTime;
            if (timeSinceLastClick <= doubleClickTime)
            {
                UpdateTargetOnClick();
            }
            lastClickTime = Time.time;
        }
    }

    private void ExitOrbitMode()
    {
        focusOrbit = false;
        movingToOrbit = false;
        transitioningToOrbit = false;

        Vector3 angles = transform.eulerAngles;

        cameraYaw = angles.y;
        cameraPitch = NormalizeAngle(angles.x);

        currentSpeed = 0;
        moving = false;
        zoomPending = 0f;

        justExitedOrbit = true;

        // Start a normal pan immediately if middle mouse is already held
        if (allowMiddleClickPanning && Input.GetMouseButton(2))
        {
            BeginMiddleMousePan();
        }

        lastMousePosition = Input.mousePosition;
    }

    void Orbit()
    {
        Vector3 delta = Input.mousePosition - lastMousePosition;

        float angleX = delta.x * orbitSpeed * Time.deltaTime;
        float angleY = delta.y * orbitSpeed * Time.deltaTime;

        transform.RotateAround(target.transform.position, Vector3.up, angleX);

        Vector3 right = transform.right;
        transform.RotateAround(target.transform.position, right, -angleY);

        lastMousePosition = Input.mousePosition;
    }

    void Pan()
    {
        Vector3 delta = Input.mousePosition - lastMousePosition;
        Vector3 panMovement = new Vector3(-delta.x * panSpeed * Time.deltaTime, -delta.y * panSpeed * Time.deltaTime, 0);
        transform.Translate(panMovement, Space.Self);

        if (updateTargetOnPan)
        {
            NudgeTarget(transform.TransformDirection(panMovement));
        }

        lastMousePosition = Input.mousePosition;
    }

    void Zoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll == 0f) return;

        Vector3 direction = transform.position - target.transform.position;
        if (direction.sqrMagnitude < 0.0001f) direction = -transform.forward;

        // The ray has to point from the camera TOWARD the pivot (it used to point away,
        // so it never saw the planet being orbited). If the pivot sits inside a planet,
        // this hits that planet's surface, which is what we clip zooming-in against.
        float pivotDistance = direction.magnitude;
        bool hitSomething = Physics.Raycast(transform.position, -direction.normalized,
            out RaycastHit hit, pivotDistance, ~0, QueryTriggerInteraction.Ignore);

        // Step size still scales with distance to the pivot, exactly as before, so the
        // feel is unchanged - the surface only ever clips the step, it doesn't scale it.
        // It never shrinks below minZoomStep, which stops zoom grinding to a halt near
        // an object and stops it crawling when backing out from close range.
        float stepDistance = Mathf.Max(pivotDistance, minZoomStep);
        Vector3 movement = direction.normalized * stepDistance * scroll * zoomSpeed;

        bool zoomingIn = Vector3.Dot(movement, direction) < 0f;
        if (zoomingIn && hitSomething)
        {
            float requestedDistance = movement.magnitude;
            float allowedDistance = Mathf.Max(0f, hit.distance - minZoomClipDistance);
            movement = movement.normalized * Mathf.Min(requestedDistance, allowedDistance);
        }

        transform.position += movement;
    }

    void UpdateTargetOnClick()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            if (target == null || target.transform == null) return;

            Vector3 unityDelta = hit.point - target.transform.position;
            NudgeTarget(unityDelta);
        }
    }

    public void FitAll()
    {
        // Implement the logic to fit all objects in the view
        // This is a placeholder and needs to be customized based on your scene
        Bounds bounds = new Bounds(target.transform.position, Vector3.zero);
        Renderer[] renderers = FindObjectsOfType<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }

        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        distance = maxSize * 1.5f;
        transform.position = target.transform.position - transform.forward * distance;
    }

    //END OF lalamax3d's CODE


    //NOT MINE AGAIN, but i have hevily editited it
    //OG: https://gist.github.com/beardordie/27961d466b497a843d69f8209b07d1b5?

    [SerializeField] private float initialSpeed = 10f;

    [Header("Pan Mode - Logarithmic Speed")]
    [Tooltip("Fraction of your 'altitude' (distance to the nearest surface) that WASD flight speed targets each second (1.0 = roughly cross that distance in a second at full speed).")]
    [SerializeField] private float moveSpeedFraction = 1f;
    [Tooltip("How sharply currentSpeed ramps UP toward the target speed, as a fraction of the target speed per second. Slowing down is always instant, so you can't carry speed into something.")]
    [SerializeField] private float speedRampSharpness = 3f;
    [Tooltip("Floor for altitude used to scale WASD speed, zoom and panning (Unity units) - stops everything collapsing to near-zero right next to small objects.")]
    [SerializeField] private float minScaleDistance = 0.5f;
    [Tooltip("Radius of the 'any direction' search for the nearest surface (Unity units). Beyond this, altitude is remembered from the last time something was in range and grows as you fly away (see ResetScaleMemory).")]
    [SerializeField] private float scaleRaycastMaxDistance = 1000000f;
    [Tooltip("Stops WASD flight and zoom passing through collider surfaces.")]
    [SerializeField] private bool clipMovementAgainstSurfaces = true;



    [SerializeField] private bool isEnabled = true;
    [SerializeField] private bool allowMovement = true;
    [SerializeField] private bool allowRotation = false;
    [SerializeField] private bool allowRotationOnRightClick = true;
    [SerializeField] private bool cursorVisibleByDefault = true;

    [SerializeField] private bool allowMiddleClickPanning = true;
    [Tooltip("Fraction of your altitude that a full screen-width middle-click drag pans - same logarithmic idea as WASD/zoom.")]
    [Range(0.05f, 5f)][SerializeField] private float middleClickPanFraction = 0.5f;

    [Header("Pan Mode - Scroll Zoom")]
    [Tooltip("Zoom strength. One scroll tick closes (or opens) roughly this much x 0.1 of your altitude.")]
    [SerializeField] private float normalZoomSpeed = 2;
    [Tooltip("How quickly scroll input is spent, per second. Higher = snappier, lower = smoother glide. 0 = apply instantly. Because altitude is re-measured every frame while gliding, the glide naturally brakes as you close on a surface.")]
    [SerializeField] private float zoomSmoothing = 12f;

    private Vector3 panLastMouse;


    [SerializeField] private KeyCode forwardButton = KeyCode.W;
    [SerializeField] private KeyCode backwardButton = KeyCode.S;
    [SerializeField] private KeyCode rightButton = KeyCode.D;
    [SerializeField] private KeyCode leftButton = KeyCode.A;



    [SerializeField] private float cursorSensitivity = 3f;
    [SerializeField] private bool cursorToggleAllowed = false;
    [SerializeField] private KeyCode cursorToggleButton = KeyCode.Escape;



    private float currentSpeed = 0f;
    private bool moving = false;
    private bool togglePressed = false;

    // Scroll input waiting to be spent, as a signed "fraction of altitude" (+ = in, - = out).
    private float zoomPending = 0f;

    // ---- Remembered altitude (for when nothing is within scaleRaycastMaxDistance) ----
    // The last place we actually measured a nearest-surface distance, and what it was.
    // Out of range, altitude <= that distance + how far we've moved from that spot
    // (triangle inequality). It depends only on POSITION, not on where you're looking
    // or what path you took, so turning around can't make it forget.
    private double3 scaleRefWorldPos;
    private float scaleRefDistance;

    [SerializeField] private float minAngle = -80f;
    [SerializeField] private float maxAngle = 80f;



    private void OnEnable()
    {
        if (cursorToggleAllowed && isEnabled)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // Call this after teleporting the camera (writing worldPos directly), otherwise the
    // remembered altitude thinks you flew all that way.
    public void ResetScaleMemory()
    {
        scaleRefWorldPos = worldPos;
        scaleRefDistance = scaleRaycastMaxDistance;
    }

    // The camera's true position right now, including any movement already applied to the
    // transform this frame but not yet folded into worldPos by FinalizeFrame().
    private double3 TruePosition()
    {
        return worldPos + ToDouble3(transform.position - anchorPosition) * kmPerUnit;
    }

    // "Altitude": how far you are from the nearest thing, used to scale WASD speed, scroll
    // zoom and middle-click pan so they all feel proportional at any scale.
    //
    // It's the nearest surface in ANY direction (so looking around never changes it), capped
    // by whatever the ray in `direction` hits at ANY range (so approaching something far away
    // still slows you down before you reach it).
    private float GetScaleDistance(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
        direction.Normalize();

        float scale = GetNearestSurfaceOrRemembered();

        if (Physics.Raycast(transform.position, direction, out RaycastHit hit,
            Mathf.Infinity, ~0, QueryTriggerInteraction.Ignore))
        {
            scale = Mathf.Min(scale, hit.distance);
        }

        return Mathf.Max(scale, minScaleDistance);
    }

    private float GetNearestSurfaceOrRemembered()
    {
        double3 pos = TruePosition();

        if (TryGetNearestSurfaceDistance(transform.position, scaleRaycastMaxDistance, out float measured))
        {
            scaleRefWorldPos = pos;
            scaleRefDistance = measured;
            return measured;
        }

        // Nothing in range, so we're at least scaleRaycastMaxDistance from everything.
        // Grow from the last measurement by however far we've moved since then.
        float travelled = (float)(math.length(pos - scaleRefWorldPos) / kmPerUnit);
        return Mathf.Max(scaleRefDistance + travelled, scaleRaycastMaxDistance);
    }

    // Distance from `origin` to the closest collider in ANY direction. Returns false if
    // nothing is within maxDistance. Uses overlap tests, so it works with every collider
    // type (including non-convex MeshColliders) and doesn't care where the camera points.
    private bool TryGetNearestSurfaceDistance(Vector3 origin, float maxDistance, out float result)
    {
        float lo = Mathf.Max(minZoomClipDistance, 0.001f);
        result = maxDistance;

        if (maxDistance <= lo) return false;

        if (Physics.CheckSphere(origin, lo, ~0, QueryTriggerInteraction.Ignore))
        {
            result = lo;
            return true;
        }

        if (!Physics.CheckSphere(origin, maxDistance, ~0, QueryTriggerInteraction.Ignore))
            return false;

        // Something is between lo and maxDistance. Bisect in log space so the answer is
        // equally precise whether it's 0.5 units away or 500,000 (14 steps = well under 1%).
        float hi = maxDistance;
        for (int i = 0; i < 14; i++)
        {
            float mid = Mathf.Sqrt(lo * hi);
            if (Physics.CheckSphere(origin, mid, ~0, QueryTriggerInteraction.Ignore)) hi = mid;
            else lo = mid;
        }

        result = hi;
        return true;
    }

    // How far we can actually move along `dir` this frame without ending up inside a surface.
    private float ClipStepAgainstSurfaces(Vector3 dir, float step)
    {
        if (!clipMovementAgainstSurfaces) return step;

        if (Physics.Raycast(transform.position, dir, out RaycastHit hit,
            step + minZoomClipDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            return Mathf.Min(step, Mathf.Max(0f, hit.distance - minZoomClipDistance));
        }

        return step;
    }

    private void NormalUpdate()
    {
        if (transitioningToOrbit) return;

        if (!isEnabled) return;

        if (ignoreMiddleMouseUntilRelease)
        {
            if (!Input.GetMouseButton(2))
            {
                ignoreMiddleMouseUntilRelease = false;
            }

            return;
        }

        if (justExitedOrbit)
        {
            // eat one frame of mouse input
            Input.GetAxis("Mouse X");
            Input.GetAxis("Mouse Y");

            justExitedOrbit = false;
        }
        else if (allowRotationOnRightClick && Input.GetMouseButton(1))
        {
            cameraYaw += Input.GetAxis("Mouse X") * cursorSensitivity;
            cameraPitch -= Input.GetAxis("Mouse Y") * cursorSensitivity;

            cameraPitch = Mathf.Clamp(cameraPitch, minAngle, maxAngle);

            transform.rotation = Quaternion.Euler(
                cameraPitch,
                cameraYaw,
                0f
            );
        }

        if (allowMovement)
        {
            bool lastMoving = moving;
            Vector3 deltaPosition = Vector3.zero;

            moving = false;

            // Full 3D flight - forward/right follow exactly where the camera
            // is pointed (pitch included), same as before.
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;

            CheckMove(forwardButton, ref deltaPosition, forward);
            CheckMove(backwardButton, ref deltaPosition, -forward);
            CheckMove(rightButton, ref deltaPosition, right);
            CheckMove(leftButton, ref deltaPosition, -right);

            if (moving)
            {
                // Logarithmic feel: the speed you can reach scales with your altitude, so a
                // held key crosses roughly the same *proportion* of your surroundings whether
                // you're skimming a moon's surface or crossing the gap between planets.
                // Altitude no longer depends on where you're looking, so flying backwards
                // away from a planet speeds up exactly like flying towards one slows down.
                float scaleDistance = GetScaleDistance(deltaPosition);
                float targetSpeed = scaleDistance * moveSpeedFraction;

                if (moving != lastMoving)
                {
                    currentSpeed = Mathf.Min(initialSpeed, targetSpeed);
                }

                // Speeding up is a ramp, slowing down is instant: never carry more speed than
                // your surroundings allow (e.g. flying in from deep space at huge speed).
                currentSpeed = Mathf.Min(currentSpeed, targetSpeed);
                currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, targetSpeed * speedRampSharpness * Time.deltaTime);

                Vector3 moveDir = deltaPosition.normalized;
                float step = ClipStepAgainstSurfaces(moveDir, currentSpeed * Time.deltaTime);
                transform.position += moveDir * step;
            }
            else currentSpeed = 0f;

            // Scroll wheel drives its own logarithmic zoom (see ZoomPanMode), completely
            // independent of the WASD speed ramp above.
            ZoomPanMode();
        }

        if (allowMiddleClickPanning && Input.GetMouseButtonDown(2))
        {
            BeginMiddleMousePan();
        }

        if (allowMiddleClickPanning && Input.GetMouseButton(2))
        {
            UpdateMiddleMousePan();
        }

        if (cursorToggleAllowed)
        {
            if (Input.GetKey(cursorToggleButton))
            {
                if (!togglePressed)
                {
                    togglePressed = true;
                    Cursor.lockState = (Cursor.lockState == CursorLockMode.None) ? CursorLockMode.Locked : CursorLockMode.None;
                    Cursor.visible = !Cursor.visible;
                }
            }
            else togglePressed = false;
        }
        else
        {
            togglePressed = false;
            if (!cursorVisibleByDefault) Cursor.visible = false;
        }
    }

    // Pan-mode scroll zoom.
    //  - Scroll ticks are banked in zoomPending and spent over a few frames (zoomSmoothing).
    //    Altitude is re-measured every frame they're spent, so a fast spin of the wheel
    //    brakes itself as you close on a surface instead of slamming into it.
    //  - Each spend moves you a fraction of your altitude, 1 - e^-amount. That's the same as
    //    before for normal ticks, but can never exceed 100% of the distance however hard the
    //    wheel is spun (a burst of ticks in one frame used to cover the whole gap).
    //  - Altitude comes from GetScaleDistance, so it no longer changes with where you look.
    private void ZoomPanMode()
    {
        zoomPending += Input.GetAxis("Mouse ScrollWheel") * normalZoomSpeed;
        if (zoomPending == 0f) return;

        float amount = zoomSmoothing > 0f
            ? zoomPending * (1f - Mathf.Exp(-zoomSmoothing * Time.deltaTime))
            : zoomPending;
        zoomPending -= amount;

        // Flush the long tail of the exponential rather than creeping forever.
        if (Mathf.Abs(zoomPending) < 1e-4f)
        {
            amount += zoomPending;
            zoomPending = 0f;
        }

        if (amount == 0f) return;

        Vector3 moveDir = amount > 0f ? transform.forward : -transform.forward;   // scroll up = in, scroll down = out

        float altitude = GetScaleDistance(moveDir);
        float step = altitude * (1f - Mathf.Exp(-Mathf.Abs(amount)));

        // Clip against whatever is in the direction we're actually moving (in OR out).
        step = ClipStepAgainstSurfaces(moveDir, step);

        transform.position += moveDir * step;
    }


    private void BeginMiddleMousePan()
    {
        panLastMouse = Input.mousePosition;
    }

    private void UpdateMiddleMousePan()
    {
        // Delta since LAST FRAME (not since the drag started), otherwise the whole
        // cumulative offset gets re-folded into worldPos every frame it's held.
        Vector3 pixelDelta = Input.mousePosition - panLastMouse;
        float normalizedDX = pixelDelta.x / Screen.width;
        float normalizedDY = pixelDelta.y / Screen.height;

        // Same logarithmic idea as WASD/zoom: scale by your altitude instead of a flat
        // units-per-drag amount.
        float scaleDistance = GetScaleDistance(transform.forward);
        float effectivePanSpeed = scaleDistance * middleClickPanFraction;

        float worldDX = normalizedDX * effectivePanSpeed;
        float worldDY = normalizedDY * effectivePanSpeed;

        transform.position -= (transform.right * worldDX) + (transform.up * worldDY);

        panLastMouse = Input.mousePosition;
    }

    private void CheckMove(KeyCode keyCode, ref Vector3 deltaPosition, Vector3 directionVector)
    {
        if (Input.GetKey(keyCode))
        {
            moving = true;
            deltaPosition += directionVector;
        }
    }
}