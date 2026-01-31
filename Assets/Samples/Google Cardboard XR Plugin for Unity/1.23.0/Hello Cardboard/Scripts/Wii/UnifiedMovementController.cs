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
    [SerializeField] private bool autoDetectMode = true;

    [Header("Movement Settings")]
    [SerializeField] private float walkingSpeed = 3.0f;

    [Header("Dead Zone Settings")]
    [SerializeField] private float deadZoneAngle = 30.0f;
    [SerializeField] private float fullSpeedAngle = 45.0f;

    [Header("Wii Movement Settings")]
    [SerializeField] private float wiiTiltSensitivity = 1.5f;
    [SerializeField] private float wiiDeadZone = 0.15f;

    [Header("Wii Rotation Settings")]
    [SerializeField] private float wiiRotationSpeed = 2.0f;
    [SerializeField] private bool invertWiiRotation = false;

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

            // Initialize IR camera for pointer
            wiimote.SetupIRCamera(IRDataType.BASIC);

            Debug.Log("Wii Remote initialized for movement");
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

        // Read Wii Remote data
        int ret;
        do
        {
            ret = wiimote.ReadWiimoteData();
        } while (ret > 0);

        // Get accelerometer data
        float[] accel = wiimote.Accel.GetCalibratedAccelData();
        wiiAcceleration = new Vector3(accel[0], accel[1], accel[2]);

        bool wasWalking = isWalking;

        // Calculate tilt angle for walking (pointing down = walking)
        // accel[1] is the Y-axis (up/down when held vertically)
        float tiltFactor = Mathf.Clamp01(1f - accel[1]);

        // Apply dead zone
        if (tiltFactor < wiiDeadZone)
        {
            isWalking = false;
            currentSpeedFactor = 0f;
        }
        else
        {
            isWalking = true;
            // Map from dead zone to 1.0
            currentSpeedFactor = Mathf.Clamp01((tiltFactor - wiiDeadZone) / (1f - wiiDeadZone));
            currentSpeedFactor *= wiiTiltSensitivity;
            currentSpeedFactor = Mathf.Min(currentSpeedFactor, 1f);
        }

        // Handle rotation with left/right tilt (accel[0] is X-axis)
        float rotationInput = accel[0];
        if (Mathf.Abs(rotationInput) > 0.2f) // Dead zone for rotation
        {
            float rotationAmount = rotationInput * wiiRotationSpeed * (invertWiiRotation ? -1 : 1);
            transform.Rotate(0, rotationAmount, 0);
        }

        // D-Pad for additional rotation control
        if (wiimote.Button.d_left)
        {
            transform.Rotate(0, -wiiRotationSpeed * 2f, 0);
        }
        if (wiimote.Button.d_right)
        {
            transform.Rotate(0, wiiRotationSpeed * 2f, 0);
        }

        UpdateVignette(wasWalking);
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

        // Mouse look - only in keyboard mode
        rotationX += Input.GetAxis("Mouse X") * mouseSensitivity;
        rotationY -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        rotationY = Mathf.Clamp(rotationY, -90f, 90f);

        mainCamera.transform.localRotation = Quaternion.Euler(rotationY, 0f, 0f);
        transform.localRotation = Quaternion.Euler(0f, rotationX, 0f);

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

    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 16;
        style.normal.textColor = Color.white;

        GUI.Label(new Rect(10, 10, 300, 20), $"Control Mode: {currentMode} (Tab to switch)", style);
        GUI.Label(new Rect(10, 30, 300, 20), $"Walking: {isWalking}", style);
        GUI.Label(new Rect(10, 50, 300, 20), $"Speed Factor: {currentSpeedFactor:F2}", style);

        if (currentMode == ControlMode.WiiRemote && wiimote != null)
        {
            GUI.Label(new Rect(10, 70, 300, 20), $"Wii Accel: X:{wiiAcceleration.x:F2} Y:{wiiAcceleration.y:F2} Z:{wiiAcceleration.z:F2}", style);
            GUI.Label(new Rect(10, 90, 300, 20), $"Wiimote Connected: {wiimote != null}", style);
        }
    }

    private void OnApplicationQuit()
    {
        if (wiimote != null)
        {
            WiimoteManager.Cleanup(wiimote);
        }
    }
}