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
    private Vector3 orbitTargetPosition;
    private Quaternion orbitTargetRotation;


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

        if (movingToOrbit)
        {
            orbitTargetPosition -= FrameDeltaPosition;
        }

        SyncTargetTransform();

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

    private void NudgeTarget(Vector3 unityDelta)
    {
        if (target == null || target.transform == null) return;

        target.transform.position += unityDelta;

        if (target.useWorldPosition)
        {
            target.worldPosition += new double3(unityDelta.x, unityDelta.y, unityDelta.z) * kmPerUnit;
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

        direction.Normalize();

        orbitTargetPosition = target.transform.position + direction * distance;

        Vector3 lookDirection = target.transform.position - orbitTargetPosition;

        orbitTargetRotation = Quaternion.LookRotation(lookDirection);

        movingToOrbit = true;
    }

    private void MoveToOrbit()
    {
        transform.position = Vector3.Lerp(
            transform.position,
            orbitTargetPosition,
            Time.deltaTime * orbitEnterSpeed
        );

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            orbitTargetRotation,
            Time.deltaTime * orbitEnterSpeed
        );


        if (Vector3.Distance(transform.position, orbitTargetPosition) < orbitStopDistance &&
           Quaternion.Angle(transform.rotation, orbitTargetRotation) < 1f)
        {
            transform.position = orbitTargetPosition;
            transform.rotation = orbitTargetRotation;

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

        // Step size scales with current distance (feels natural at any scale),
        // but never shrinks below minZoomStep - that floor is what stops zoom
        // grinding to a halt near an object, and stops it crawling when
        // backing back out from close range.
        float stepDistance = Mathf.Max(direction.magnitude, minZoomStep);
        Vector3 movement = direction.normalized * stepDistance * scroll * zoomSpeed;

        bool zoomingIn = Vector3.Dot(movement, direction) < 0f;
        if (zoomingIn)
        {
            float requestedDistance = movement.magnitude;
            if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit,
                requestedDistance + minZoomClipDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                float allowedDistance = Mathf.Max(0f, hit.distance - minZoomClipDistance);
                movement = movement.normalized * Mathf.Min(requestedDistance, allowedDistance);
            }
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
    [SerializeField] private float increaseSpeed = 1.25f;



    [SerializeField] private bool isEnabled = true;
    [SerializeField] private bool allowMovement = true;
    [SerializeField] private bool allowRotation = false;
    [SerializeField] private bool allowRotationOnRightClick = true;
    [SerializeField] private bool cursorVisibleByDefault = true;

    [SerializeField] private bool allowMiddleClickPanning = true;
    [Tooltip("World units panned across one full screen-width drag. This is the ONLY thing that controls middle-mouse pan speed - turn it down if panning feels too fast.")]
    [Range(0.1f, 100f)][SerializeField] private float middleClickPanningSpeed = 3f;
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



            if (moving)
                currentSpeed += increaseSpeed * Time.deltaTime;



            moving = false;

            // Full 3D flight - forward/right follow exactly where the camera
            // is pointed (pitch included), same as the scroll-wheel movement below.
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;

            CheckMove(forwardButton, ref deltaPosition, forward, false, false);
            CheckMove(backwardButton, ref deltaPosition, -forward, false, false);
            CheckMove(rightButton, ref deltaPosition, right, false, false);
            CheckMove(leftButton, ref deltaPosition, -right, false, false);

            if (Input.GetAxis("Mouse ScrollWheel") > 0f) // forward
            {
                CheckMove(forwardButton, ref deltaPosition, transform.forward * normalZoomSpeed, true, false);
            }
            else if (Input.GetAxis("Mouse ScrollWheel") < 0f) // backwards
            {
                CheckMove(backwardButton, ref deltaPosition, -transform.forward * normalZoomSpeed, false, true);
            }



            if (moving)
            {
                if (moving != lastMoving)
                    currentSpeed = initialSpeed;



                transform.position += deltaPosition * currentSpeed * Time.deltaTime;
            }
            else currentSpeed = 0f;
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

        float worldDX = normalizedDX * middleClickPanningSpeed;
        float worldDY = normalizedDY * middleClickPanningSpeed;

        transform.position -= (transform.right * worldDX) + (transform.up * worldDY);

        panLastMouse = Input.mousePosition;
    }

    private void CheckMove(KeyCode keyCode, ref Vector3 deltaPosition, Vector3 directionVector, bool forceForward, bool forceBackward)
    {
        if (Input.GetKey(keyCode) || forceBackward || forceForward)
        {
            moving = true;
            deltaPosition += directionVector;
        }
    }
}