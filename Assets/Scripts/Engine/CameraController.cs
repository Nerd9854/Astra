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
        [Tooltip("Local Unity Transform used for all orbit/pan/zoom math (RotateAround, Lerp, etc). Assign any empty GameObject to use as the pivot.")]
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
    [Header("Follow Moving Orbit Target")]
    [Tooltip("While orbiting (or transitioning into orbit), the camera shifts by however much the target's real worldPosition moves on its own each frame, so a moving planet/moon doesn't slip out from under you. Only applies when target.useWorldPosition is true.")]
    [SerializeField] private bool followMovingTarget = true;

    private double3 lastTargetWorldPosition;
    private bool hasLastTargetWorldPosition = false;

    // How much of this frame's target.worldPosition change came from our own
    // NudgeTarget() calls (deliberate pan/click), so FollowTarget() doesn't
    // double-count movement that's already being applied via the normal
    // camera-movement path.
    private double3 nudgeAccumulatorThisFrame;


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
    }

    public void camUpdate()
    {
        if (movingToOrbit)
        {
            MoveToOrbit();
            return;
        }

        if (focusOrbit)
        {
            HandleFocusMouseInput();
        }
        else
        {
            NormalUpdate();
        }
    }

    private void LateUpdate()
    {
        FrameDeltaPosition = transform.position - anchorPosition;
        FrameDeltaRotation = transform.rotation * Quaternion.Inverse(lastRawRotation);
        lastRawRotation = transform.rotation;

        // Fold this frame's small, precise delta into the big, precise total (real km).
        worldPos += new double3(FrameDeltaPosition.x, FrameDeltaPosition.y, FrameDeltaPosition.z) * kmPerUnit;

        FollowTarget();

        SyncTargetTransform();

        // Whatever nudge happened this frame has now been accounted for by
        // FollowTarget - clear it before the next frame.
        nudgeAccumulatorThisFrame = double3.zero;

        // The camera never repositions anything else in the scene - worldPos
        // above is all any other script needs to place itself relative to the camera.
        transform.position = anchorPosition;
    }

    private void SyncTargetTransform()
    {
        if (target == null || target.transform == null || !target.useWorldPosition) return;

        double3 relativeUnits = (target.worldPosition - worldPos) / kmPerUnit;
        target.transform.position = new Vector3((float)relativeUnits.x, (float)relativeUnits.y, (float)relativeUnits.z);
    }

    // Keeps the orbit offset (distance + angle) fixed relative to the target
    // even while the target's true worldPosition is moving on its own -
    // e.g. a moon actually orbiting its planet. Only the part of the
    // target's movement that ISN'T from our own NudgeTarget() calls counts,
    // otherwise panning/clicking during orbit would move the camera twice.
    private void FollowTarget()
    {
        if (!followMovingTarget || target == null || target.transform == null || !target.useWorldPosition)
        {
            hasLastTargetWorldPosition = false;
            return;
        }

        bool trackingOrbit = focusOrbit || movingToOrbit || transitioningToOrbit;

        if (!trackingOrbit)
        {
            // Not orbiting right now - don't carry a stale delta into the
            // next time orbit mode is entered (that would cause one big jump).
            hasLastTargetWorldPosition = false;
            return;
        }

        if (hasLastTargetWorldPosition)
        {
            double3 targetDelta = (target.worldPosition - lastTargetWorldPosition) - nudgeAccumulatorThisFrame;
            worldPos += targetDelta;
        }

        lastTargetWorldPosition = target.worldPosition;
        hasLastTargetWorldPosition = true;
    }

    // Call this from whatever script assigns a brand-new orbit target (a
    // different planet/moon), so the first frame after switching doesn't
    // read as one huge, fake "movement" of the old target.
    public void ResetFollowTracking()
    {
        hasLastTargetWorldPosition = false;
    }

    private void NudgeTarget(Vector3 unityDelta)
    {
        if (target == null || target.transform == null) return;

        target.transform.position += unityDelta;

        if (target.useWorldPosition)
        {
            double3 worldDelta = new double3(unityDelta.x, unityDelta.y, unityDelta.z) * kmPerUnit;
            target.worldPosition += worldDelta;
            nudgeAccumulatorThisFrame += worldDelta;
        }
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

        Vector3 direction = transform.position - target.transform.position;

        if (direction.magnitude < 0.1f)
            direction = -transform.forward;

        orbitDirection = direction.normalized;

        movingToOrbit = true;
    }

    private void MoveToOrbit()
    {
        // Recomputed live every frame from the target's current position
        // (which FollowTarget/SyncTargetTransform already keep correct even
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

        // One raycast, reused for both purposes below. Distance-to-pivot
        // alone isn't enough to slow the step down properly - the pivot is
        // often a planet's centre, far past the actual surface - so this
        // also checks the collider itself and uses whichever is closer.
        bool hitSomething = Physics.Raycast(transform.position, direction.normalized,
            out RaycastHit hit, direction.magnitude, ~0, QueryTriggerInteraction.Ignore);

        float scaleDistance = hitSomething ? Mathf.Min(direction.magnitude, hit.distance) : direction.magnitude;

        // Step size scales with that distance (feels natural at any scale),
        // but never shrinks below minZoomStep - that floor is what stops zoom
        // grinding to a halt near an object, and stops it crawling when
        // backing back out from close range.
        float stepDistance = Mathf.Max(scaleDistance, minZoomStep);
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
    [Tooltip("Fraction of the open space ahead of you that WASD flight speed targets each second (1.0 = roughly cross that distance in a second at full speed).")]
    [SerializeField] private float moveSpeedFraction = 1f;
    [Tooltip("How sharply currentSpeed ramps toward the target speed, as a fraction of the target speed per second. Higher = snappier response when the scale ahead of you changes.")]
    [SerializeField] private float speedRampSharpness = 3f;
    [Tooltip("Floor for the raycast distance used to scale WASD speed and pan-mode zoom (Unity units) - stops speed/zoom collapsing to near-zero right next to small objects.")]
    [SerializeField] private float minScaleDistance = 0.5f;
    [Tooltip("Used as the scale distance when there's nothing ahead to hit (e.g. deep space) - keep this large so open-space flight/zoom stays fast.")]
    [SerializeField] private float openSpaceScaleDistance = 5000f;
    [Tooltip("How far the reference raycast checks, in Unity units.")]
    [SerializeField] private float scaleRaycastMaxDistance = 1000000f;



    [SerializeField] private bool isEnabled = true;
    [SerializeField] private bool allowMovement = true;
    [SerializeField] private bool allowRotation = false;
    [SerializeField] private bool allowRotationOnRightClick = true;
    [SerializeField] private bool cursorVisibleByDefault = true;

    [SerializeField] private bool allowMiddleClickPanning = true;
    [Tooltip("Fraction of the open space ahead of you that a full screen-width middle-click drag pans - same logarithmic idea as WASD/zoom, so this no longer goes flat and slow when zoomed out.")]
    [Range(0.05f, 5f)][SerializeField] private float middleClickPanFraction = 0.5f;
    [SerializeField] private float normalZoomSpeed = 2;

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

    // Distance to whatever's ahead of you in a given direction - the same
    // idea orbit-mode Zoom() uses for its target, just aimed wherever you're
    // currently flying/looking instead of at a fixed pivot. Used to scale
    // both WASD speed and pan-mode scroll zoom so they feel proportional to
    // your surroundings at any scale, instead of a flat units/second.
    private float GetScaleDistance(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;

        if (Physics.Raycast(transform.position, direction.normalized, out RaycastHit hit,
            scaleRaycastMaxDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            return Mathf.Max(hit.distance, minScaleDistance);
        }

        return openSpaceScaleDistance;
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
                // Logarithmic feel: the speed you can reach scales with how
                // much open space is ahead of you, so a held key crosses
                // roughly the same *proportion* of your surroundings whether
                // you're skimming a moon's surface or crossing empty space
                // between planets - and you naturally slow down as you
                // approach something instead of blowing through it.
                float scaleDistance = GetScaleDistance(deltaPosition);
                float targetSpeed = scaleDistance * moveSpeedFraction;

                if (moving != lastMoving)
                {
                    currentSpeed = Mathf.Min(initialSpeed, targetSpeed);
                }

                currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, targetSpeed * speedRampSharpness * Time.deltaTime);

                transform.position += deltaPosition.normalized * currentSpeed * Time.deltaTime;
            }
            else currentSpeed = 0f;

            // Scroll wheel now drives its own logarithmic zoom (see
            // ZoomPanMode), completely independent of the WASD speed ramp
            // above - it was getting entangled with currentSpeed before.
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

    // Same idea as orbit-mode Zoom(): step size scales with however much
    // space is ahead of you, and clips against whatever you're approaching
    // so a fast zoom-in can't overshoot through it. This replaces the old
    // scroll handling that piggybacked on the WASD speed ramp.
    private void ZoomPanMode()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll == 0f) return;

        Vector3 direction = transform.forward;
        bool hitSomething = Physics.Raycast(transform.position, direction, out RaycastHit hit,
            scaleRaycastMaxDistance, ~0, QueryTriggerInteraction.Ignore);
        float aheadDistance = hitSomething ? hit.distance : openSpaceScaleDistance;

        float stepDistance = Mathf.Max(aheadDistance, minScaleDistance);
        Vector3 movement = direction * stepDistance * scroll * normalZoomSpeed;

        bool zoomingIn = scroll > 0f;
        if (zoomingIn && hitSomething)
        {
            float requestedDistance = movement.magnitude;
            float allowedDistance = Mathf.Max(0f, hit.distance - minZoomClipDistance);
            movement = movement.normalized * Mathf.Min(requestedDistance, allowedDistance);
        }

        transform.position += movement;
    }


    private void BeginMiddleMousePan()
    {
        panLastMouse = Input.mousePosition;
    }

    private void UpdateMiddleMousePan()
    {
        // Delta since LAST FRAME (not since the drag started) - this is the
        // fix. Assigning an absolute "total offset since drag start" every
        // frame kept re-folding the entire cumulative offset into worldPos
        // every single frame it was held, which was the runaway you were seeing.
        Vector3 pixelDelta = Input.mousePosition - panLastMouse;
        float normalizedDX = pixelDelta.x / Screen.width;
        float normalizedDY = pixelDelta.y / Screen.height;

        // Same logarithmic idea as WASD/zoom: scale by how much open space
        // is around you right now instead of a flat units-per-drag amount -
        // this was the one control still going flat and slow when zoomed out.
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