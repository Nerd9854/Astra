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

    public Transform target; // The target object to orbit around
    public float distance = 10.0f; // Initial distance from the target
    public float zoomSpeed = -2.0f; // Speed of zooming
    public float panSpeed = 0.3f; // Speed of panning
    public float orbitSpeed = 5.0f; // Speed of orbiting
    public bool orbitWithLMB = true; // Orbit with Left Mouse Button (LMB) if true, else Right Mouse Button (RMB)
    public bool updateTargetOnClick = true; // Update orbit target on mouse click
    public bool updateTargetOnPan = true; // Update orbit target on pan

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

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void Start()
    {
        Vector3 angles = transform.eulerAngles;

        cameraYaw = angles.y;
        cameraPitch = NormalizeAngle(angles.x);
    }

    private void Update()
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

    public void EnterOrbitMode()
    {
        transitioningToOrbit = true;
        focusOrbit = false;

        currentSpeed = 0;
        moving = false;

        Vector3 direction = transform.position - target.position;

        if (direction.magnitude < 0.1f)
            direction = -transform.forward;

        direction.Normalize();

        orbitTargetPosition = target.position + direction * distance;

        Vector3 lookDirection = target.position - orbitTargetPosition;

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

    private float NormalizeAngle(float angle)
    {
        if (angle > 180f)
            angle -= 360f;

        return angle;
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
        if (Input.GetMouseButton(2))
        {
            panStartMouse = Input.mousePosition;
            panStartPosition = transform.position;

            float planeDistance = Vector3.Distance(transform.position, target.position);
            Vector3 planePoint = transform.position + transform.forward * planeDistance;

            panPlane = new Plane(-transform.forward, planePoint);
        }

        lastMousePosition = Input.mousePosition;
    }

    void Orbit()
    {
        Vector3 delta = Input.mousePosition - lastMousePosition;

        float angleX = delta.x * orbitSpeed * Time.deltaTime;
        float angleY = delta.y * orbitSpeed * Time.deltaTime;

        transform.RotateAround(target.position, Vector3.up, angleX);

        Vector3 right = transform.right;
        transform.RotateAround(target.position, right, -angleY);

        lastMousePosition = Input.mousePosition;
    }

    void Pan()
    {
        Vector3 delta = Input.mousePosition - lastMousePosition;
        Vector3 panMovement = new Vector3(-delta.x * panSpeed * Time.deltaTime, -delta.y * panSpeed * Time.deltaTime, 0);
        transform.Translate(panMovement, Space.Self);

        if (updateTargetOnPan)
        {
            target.position += transform.TransformDirection(panMovement);
        }

        lastMousePosition = Input.mousePosition;
    }

    void Zoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        Vector3 direction = transform.position - target.position;
        Vector3 movement = direction * scroll * zoomSpeed;
        transform.position += movement;
    }

    void UpdateTargetOnClick()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            target.position = hit.point;
        }
    }

    public void FitAll()
    {
        // Implement the logic to fit all objects in the view
        // This is a placeholder and needs to be customized based on your scene
        Bounds bounds = new Bounds(target.position, Vector3.zero);
        Renderer[] renderers = FindObjectsOfType<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }

        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        distance = maxSize * 1.5f;
        transform.position = target.position - transform.forward * distance;
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
    [Range(1f, 100f)][SerializeField] private float middleClickPanningSpeed = 3f;
    [SerializeField] private float panSensitivity = 0.05f;
    [SerializeField] private float normalZoomSpeed = 2;

    private Vector3 panStartMouse;
    private Vector3 panStartPosition;
    private Plane panPlane;


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

            Vector3 forward = transform.forward;
            forward.y = 0;
            forward.Normalize();

            Vector3 right = transform.right;
            right.y = 0;
            right.Normalize();

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

        if (Input.GetMouseButtonDown(2))
        {
            panStartMouse = Input.mousePosition;
            panStartPosition = transform.position;

            float planeDistance = Vector3.Distance(transform.position, target.position);
            Vector3 planePoint = transform.position + transform.forward * planeDistance;

            panPlane = new Plane(-transform.forward, planePoint);
        }


        if (Input.GetMouseButton(2))
        {
            Ray startRay = cam.ScreenPointToRay(panStartMouse);
            Ray currentRay = cam.ScreenPointToRay(Input.mousePosition);

            if (panPlane.Raycast(startRay, out float startDistance) &&
                panPlane.Raycast(currentRay, out float currentDistance))
            {
                Vector3 startPoint = startRay.GetPoint(startDistance);
                Vector3 currentPoint = currentRay.GetPoint(currentDistance);

                Vector3 difference = startPoint - currentPoint;

                transform.position = panStartPosition + difference;
            }
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


    private void CheckMove(KeyCode keyCode, ref Vector3 deltaPosition, Vector3 directionVector, bool forceForward, bool forceBackward)
    {
        if (Input.GetKey(keyCode) || forceBackward || forceForward)
        {
            moving = true;
            deltaPosition += directionVector;
        }
    }
}