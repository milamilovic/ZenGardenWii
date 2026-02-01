using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WiimoteApi;

public class UnifiedMovementController : MonoBehaviour
{
    public enum ControlMode
    {
        MobileVR,
        WiiRemote,
        KeyboardMouse
    }

    [Header("Control Mode")]
    [SerializeField] private ControlMode currentMode = ControlMode.WiiRemote;
    [SerializeField] private bool autoDetectMode = false;
    [SerializeField] private bool showWiiInstructions = true;

    [Header("Movement Settings")]
    [SerializeField] private float walkingSpeed = 3.0f;

    [Header("Dead Zone Settings")]
    [SerializeField] private float deadZoneAngle = 30.0f;
    [SerializeField] private float fullSpeedAngle = 45.0f;

    [Header("Wii Movement Settings - Downward Tilt")]
    [SerializeField] private float wiiDownwardTiltThreshold = 15f;  // Minimum angle (degrees) to start walking
    [SerializeField] private float wiiDownwardTiltMaxSpeed = 50f;   // Angle for full speed
    [SerializeField] private float wiiTiltSmoothing = 8f;           // Smoothing for tilt input

    [Header("Wii Rotation Settings - D-Pad Only")]
    [SerializeField] private float wiiHorizontalRotationSpeed = 120f;  // Left/Right rotation (degrees/sec)
    [SerializeField] private float wiiVerticalRotationSpeed = 60f;     // Up/Down camera tilt (degrees/sec)
    [SerializeField] private float wiiMaxVerticalAngle = 60f;          // Max camera up/down angle

    [Header("Keyboard Settings")]
    [SerializeField] private float keyboardSpeed = 5.0f;
    [SerializeField] private float mouseSensitivity = 2f;

    [Header("Audio Settings")]
    [SerializeField] private AudioClip walkingAudioEffect;
    [SerializeField] private AudioClip sandWalkingAudioEffect;
    [SerializeField] private float sandSoundSpeed = 1.5f;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckDistance = 2f;

    [Header("Vignette")]
    [SerializeField] private VignetteController vignetteController;

    private bool isWalking = false;
    private Camera mainCamera;
    private AudioSource walkingAudioSource;
    private Rigidbody rb;
    private bool isOnSand = true;
    private float speedMultiplier = 1f;
    private float currentSpeedFactor = 0f;

    // Wii specific
    private Wiimote wiimote;
    private Vector3 wiiAcceleration;
    private float smoothedTiltInput = 0f;
    private float currentVerticalAngle = 0f;  // Track camera vertical rotation

    // Keyboard specific
    private float rotationX = 0f;
    private float rotationY = 0f;

    void Start()
    {
        mainCamera = Camera.main;
        walkingAudioSource = GetComponent<AudioSource>();
        rb = GetComponent<Rigidbody>();

        if (autoDetectMode)
        {
            DetectControlMode();
        }

        InitializeControlMode();
    }

    void DetectControlMode()
    {
        // Check for Wii Remote first
        WiimoteManager.FindWiimotes();

        if (WiimoteManager.HasWiimote())
        {
            currentMode = ControlMode.WiiRemote;
            Debug.Log("Wii Remote detected - using Wii control mode");
        }
        else if (Application.isMobilePlatform)
        {
            currentMode = ControlMode.MobileVR;
            Debug.Log("Mobile platform detected - using VR control mode");
        }
        else
        {
            currentMode = ControlMode.KeyboardMouse;
            Debug.Log("Using keyboard/mouse control mode");
        }
    }

    void InitializeControlMode()
    {
        switch (currentMode)
        {
            case ControlMode.WiiRemote:
                InitializeWii();
                // Unlock cursor for Wii mode
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
            case ControlMode.MobileVR:
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
            case ControlMode.KeyboardMouse:
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                break;
        }
    }

    void InitializeWii()
    {
        if (WiimoteManager.HasWiimote())
        {
            wiimote = WiimoteManager.Wiimotes[0];
            wiimote.SendPlayerLED(true, false, false, false);

            // Set data report mode for accelerometer
            wiimote.SendDataReportMode(InputDataType.REPORT_BUTTONS_ACCEL_IR10_EXT6);

            Debug.Log("Wii Remote initialized - Tilt down to walk, D-Pad to look around");
        }
        else
        {
            Debug.LogWarning("No Wii Remote found, falling back to keyboard");
            currentMode = ControlMode.KeyboardMouse;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    void Update()
    {
        // Allow mode switching with Tab key
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            CycleControlMode();
        }

        switch (currentMode)
        {
            case ControlMode.MobileVR:
                UpdateMobileVRMovement();
                break;
            case ControlMode.WiiRemote:
                UpdateWiiMovement();
                break;
            case ControlMode.KeyboardMouse:
                UpdateKeyboardMovement();
                break;
        }

        CheckGroundSurface();
    }

    void CycleControlMode()
    {
        currentMode = (ControlMode)(((int)currentMode + 1) % 3);
        Debug.Log($"Switched to {currentMode} mode");
        InitializeControlMode();
    }

    void UpdateMobileVRMovement()
    {
        float headTiltAngle = GetNormalizedHeadTilt();
        bool wasWalking = isWalking;

        if (headTiltAngle < deadZoneAngle)
        {
            isWalking = false;
            currentSpeedFactor = 0f;
        }
        else if (headTiltAngle >= deadZoneAngle && headTiltAngle <= 90f)
        {
            isWalking = true;

            if (headTiltAngle < fullSpeedAngle)
            {
                currentSpeedFactor = Mathf.InverseLerp(deadZoneAngle, fullSpeedAngle, headTiltAngle);
            }
            else
            {
                currentSpeedFactor = 1f;
            }
        }
        else
        {
            isWalking = false;
            currentSpeedFactor = 0f;
        }

        UpdateVignette(wasWalking);
    }

    void UpdateWiiMovement()
    {
        if (wiimote == null)
        {
            Debug.LogWarning("Wiimote is null in UpdateWiiMovement");
            return;
        }

        wiimote = InputManager.wiimote;
        if (wiimote == null) return;

        // Skip movement updates if in drawing mode
        if (isDrawingMode)
        {
            // Still allow D-Pad rotation for looking around
            if (wiimote.Button.d_left)
            {
                transform.Rotate(0, -wiiHorizontalRotationSpeed * Time.deltaTime, 0);
            }
            if (wiimote.Button.d_right)
            {
                transform.Rotate(0, wiiHorizontalRotationSpeed * Time.deltaTime, 0);
            }
            if (wiimote.Button.d_up)
            {
                currentVerticalAngle -= wiiVerticalRotationSpeed * Time.deltaTime;
                currentVerticalAngle = Mathf.Clamp(currentVerticalAngle, -wiiMaxVerticalAngle, wiiMaxVerticalAngle);
            }
            if (wiimote.Button.d_down)
            {
                currentVerticalAngle += wiiVerticalRotationSpeed * Time.deltaTime;
                currentVerticalAngle = Mathf.Clamp(currentVerticalAngle, -wiiMaxVerticalAngle, wiiMaxVerticalAngle);
            }
            mainCamera.transform.localRotation = Quaternion.Euler(currentVerticalAngle, 0f, 0f);
            return; // Exit early, no movement
        }

        // Get accelerometer data
        float[] accel = wiimote.Accel.GetCalibratedAccelData();
        wiiAcceleration = new Vector3(accel[0], accel[1], accel[2]);

        bool wasWalking = isWalking;

        // Calculate downward tilt angle
        // When Wiimote is held upright (vertical), Y is ~1.0
        // When tilted down 90 degrees (horizontal pointing forward), Y is ~0.0
        // We use the angle from vertical position
        float downwardTilt = CalculateDownwardTiltAngle(accel);

        // Apply threshold for walking
        if (downwardTilt < wiiDownwardTiltThreshold)
        {
            // Not tilted enough - don't walk
            isWalking = false;
            currentSpeedFactor = 0f;
        }
        else
        {
            // Tilted down enough - calculate speed
            isWalking = true;

            if (downwardTilt >= wiiDownwardTiltMaxSpeed)
            {
                currentSpeedFactor = 1f;
            }
            else
            {
                // Interpolate between threshold and max speed
                currentSpeedFactor = Mathf.InverseLerp(wiiDownwardTiltThreshold, wiiDownwardTiltMaxSpeed, downwardTilt);
            }
        }

        // Smooth the speed changes
        float targetSpeed = isWalking ? currentSpeedFactor : 0f;
        currentSpeedFactor = Mathf.Lerp(currentSpeedFactor, targetSpeed, Time.deltaTime * wiiTiltSmoothing);

        // D-Pad controls for rotation only
        // Left/Right for horizontal rotation (turning)
        if (wiimote.Button.d_left)
        {
            transform.Rotate(0, -wiiHorizontalRotationSpeed * Time.deltaTime, 0);
        }
        if (wiimote.Button.d_right)
        {
            transform.Rotate(0, wiiHorizontalRotationSpeed * Time.deltaTime, 0);
        }

        // Up/Down for camera vertical rotation (looking up/down)
        if (wiimote.Button.d_up)
        {
            currentVerticalAngle -= wiiVerticalRotationSpeed * Time.deltaTime;
            currentVerticalAngle = Mathf.Clamp(currentVerticalAngle, -wiiMaxVerticalAngle, wiiMaxVerticalAngle);
        }
        if (wiimote.Button.d_down)
        {
            currentVerticalAngle += wiiVerticalRotationSpeed * Time.deltaTime;
            currentVerticalAngle = Mathf.Clamp(currentVerticalAngle, -wiiMaxVerticalAngle, wiiMaxVerticalAngle);
        }

        // Apply vertical rotation to camera
        mainCamera.transform.localRotation = Quaternion.Euler(currentVerticalAngle, 0f, 0f);

        UpdateVignette(wasWalking);
    }

    // Calculate the angle of downward tilt in degrees
    float CalculateDownwardTiltAngle(float[] accel)
    {
        float y = accel[1]; // up/down
        float z = accel[2]; // forward/back

        // Ignore upward tilt
        if (z <= 0f)
            return 0f;

        // Angle from horizontal plane
        float angle = Mathf.Atan2(z, y) * Mathf.Rad2Deg;

        return Mathf.Clamp(angle, 0f, 90f);
    }

    void UpdateKeyboardMovement()
    {
        bool wasWalking = isWalking;

        // WASD movement
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        isWalking = (horizontal != 0 || vertical != 0);
        currentSpeedFactor = isWalking ? 1f : 0f;

        if (isWalking)
        {
            Vector3 forward = mainCamera.transform.forward;
            Vector3 right = mainCamera.transform.right;

            forward.y = 0;
            right.y = 0;

            forward.Normalize();
            right.Normalize();

            Vector3 movement = (forward * vertical + right * horizontal) * keyboardSpeed * Time.deltaTime;
            rb.MovePosition(rb.position + movement);
        }

        // Mouse look - only update if we're in keyboard mode
        if (currentMode == ControlMode.KeyboardMouse)
        {
            rotationX += Input.GetAxis("Mouse X") * mouseSensitivity;
            rotationY -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            rotationY = Mathf.Clamp(rotationY, -90f, 90f);

            mainCamera.transform.localRotation = Quaternion.Euler(rotationY, 0f, 0f);
            transform.localRotation = Quaternion.Euler(0f, rotationX, 0f);
        }

        UpdateVignette(wasWalking);
    }

    private void FixedUpdate()
    {
        // Move player in Wii and VR modes (keyboard handles movement in Update)
        if (isWalking && currentMode != ControlMode.KeyboardMouse)
        {
            MovePlayer();
        }

        // Audio handling
        HandleFootstepAudio();
    }

    private void HandleFootstepAudio()
    {
        if (isWalking)
        {
            AudioClip currentFootstepSound = isOnSand ? sandWalkingAudioEffect : walkingAudioEffect;
            float basePitch = isOnSand ? sandSoundSpeed : 1.0f;
            float adjustedPitch = basePitch * Mathf.Max(0.5f, currentSpeedFactor);

            if (!walkingAudioSource.isPlaying || walkingAudioSource.clip != currentFootstepSound)
            {
                walkingAudioSource.clip = currentFootstepSound;
                walkingAudioSource.pitch = adjustedPitch;
                walkingAudioSource.Play();
            }
            else
            {
                walkingAudioSource.pitch = adjustedPitch;
            }
        }
        else
        {
            if (walkingAudioSource.isPlaying)
            {
                walkingAudioSource.Stop();
            }
        }
    }

    private void MovePlayer()
    {
        Vector3 movementVector = new Vector3(mainCamera.transform.forward.x, 0, mainCamera.transform.forward.z);
        Vector3 movement = movementVector.normalized * walkingSpeed * currentSpeedFactor * speedMultiplier * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + movement);
    }

    private void CheckGroundSurface()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, Vector3.down, out hit, groundCheckDistance))
        {
            isOnSand = hit.collider.gameObject.name.Contains("SandPlane") ||
                       hit.collider.gameObject.name.Contains("Sand");
        }
        else
        {
            isOnSand = false;
        }
    }

    private float GetNormalizedHeadTilt()
    {
        float angle = mainCamera.transform.eulerAngles.x;

        if (angle > 180f)
            angle -= 360f;

        return Mathf.Max(0f, angle);
    }

    private void UpdateVignette(bool wasWalking)
    {
        if (vignetteController != null)
        {
            if (isWalking && !wasWalking)
            {
                vignetteController.ShowVignette();
            }
            else if (!isWalking && wasWalking)
            {
                vignetteController.HideVignette();
            }
        }
    }

    public void SetSpeedMultiplier(float multiplier)
    {
        speedMultiplier = multiplier;
    }

    public float GetSpeedMultiplier()
    {
        return speedMultiplier;
    }

    /*void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 16;
        style.normal.textColor = Color.white;

        GUI.Label(new Rect(10, 10, 400, 20), $"Control Mode: {currentMode} (Tab to switch)", style);
        GUI.Label(new Rect(10, 30, 400, 20), $"Walking: {isWalking}", style);
        GUI.Label(new Rect(10, 50, 400, 20), $"Speed Factor: {currentSpeedFactor:F2}", style);

        if (currentMode == ControlMode.WiiRemote && wiimote != null)
        {
            float downwardAngle = CalculateDownwardTiltAngle(new float[] { wiiAcceleration.x, wiiAcceleration.y, wiiAcceleration.z });

            GUI.Label(new Rect(10, 70, 400, 20), $"Downward Tilt: {downwardAngle:F1}° (Threshold: {wiiDownwardTiltThreshold:F0}°)", style);
            GUI.Label(new Rect(10, 90, 400, 20), $"Camera Angle: {currentVerticalAngle:F1}°", style);
            GUI.Label(new Rect(10, 110, 400, 20), $"Wiimote Connected: {wiimote != null}", style);

            // Show instructions
            if (showWiiInstructions)
            {
                GUIStyle instructionStyle = new GUIStyle(style);
                instructionStyle.fontSize = 14;
                instructionStyle.normal.textColor = Color.yellow;
                instructionStyle.wordWrap = true;

                string instructions = "Wii Controls:\n• Tilt Wiimote DOWN to walk (faster tilt = faster walk)\n" +
                                    "• D-Pad LEFT/RIGHT to turn\n• D-Pad UP/DOWN to look up/down\n" +
                                    "• Keep horizontal to stop";
                GUI.Label(new Rect(10, Screen.height - 100, Screen.width - 20, 90), instructions, instructionStyle);
            }
        }
    }*/

    string GetCurrentSchemeInstructions()
    {
        return "Tilt Wiimote down to walk. Use D-Pad to look around (Left/Right to turn, Up/Down to look).";
    }

    private void OnApplicationQuit()
    {
        CleanupWiimote();
    }

    private void OnDisable()
    {
        CleanupWiimote();
    }

    private bool cleanupExecuted = false;

    private void CleanupWiimote()
    {
        if (cleanupExecuted) return;
        cleanupExecuted = true;

        wiimote = null;
    }

    private bool isDrawingMode = false;

    public void SetDrawingMode(bool enabled)
    {
        isDrawingMode = enabled;

        // Disable movement when in drawing mode
        if (enabled)
        {
            isWalking = false;
            currentSpeedFactor = 0f;
        }
    }

    public bool IsDrawingMode()
    {
        return isDrawingMode;
    }
}