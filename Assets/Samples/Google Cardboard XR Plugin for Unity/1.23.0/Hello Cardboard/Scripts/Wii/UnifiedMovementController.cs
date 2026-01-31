using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WiimoteApi;

public class UnifiedMovementController : MonoBehaviour
{
    public enum ControlMode
    {
        MobileVR,      // Original head-tilt based
        WiiRemote,     // Wii accelerometer based
        KeyboardMouse  // Fallback
    }

    [Header("Control Mode")]
    [SerializeField] private ControlMode currentMode = ControlMode.WiiRemote;
    [SerializeField] private bool autoDetectMode = true;

    [Header("Movement Settings")]
    [SerializeField] private float walkingSpeed = 3.0f;
    [SerializeField] private float minimumAngleTreshold = 35.0f;
    [SerializeField] private float maximumAngleTreshold = 90.0f;

    [Header("Dead Zone Settings")]
    [SerializeField] private float deadZoneAngle = 30.0f;
    [SerializeField] private float fullSpeedAngle = 45.0f;

    [Header("Wii Movement Settings")]
    [SerializeField] private float wiiTiltSensitivity = 1.5f;
    [SerializeField] private float wiiDeadZone = 0.15f;

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
        // Check for Wii Remote
        if (WiimoteManager.HasWiimote())
        {
            currentMode = ControlMode.WiiRemote;
            Debug.Log("Wii Remote detected - using Wii control mode");
        }
        // Check if running on mobile VR (you can add your VR SDK check here)
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
                break;
            case ControlMode.MobileVR:
                // Your existing VR setup
                break;
            case ControlMode.KeyboardMouse:
                Cursor.lockState = CursorLockMode.Locked;
                break;
        }
    }

    void InitializeWii()
    {
        WiimoteManager.FindWiimotes();

        if (WiimoteManager.HasWiimote())
        {
            wiimote = WiimoteManager.Wiimotes[0];
            wiimote.SendPlayerLED(true, false, false, false);
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

    void UpdateMobileVRMovement()
    {
        // Your original look-to-walk logic
        float headTiltAngle = GetNormalizedHeadTilt();
        bool wasWalking = isWalking;

        if (headTiltAngle < deadZoneAngle)
        {
            isWalking = false;
            currentSpeedFactor = 0f;
        }
        else if (headTiltAngle >= deadZoneAngle && headTiltAngle <= maximumAngleTreshold)
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
        if (wiimote == null) return;

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

        // Calculate tilt angle (pointing down = walking)
        // When Wii Remote points down, accel[1] (Y) decreases
        float tiltFactor = 1f - accel[1]; // 0 = horizontal, 1 = pointing down

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

        UpdateVignette(wasWalking);

        // Optional: D-Pad for strafing
        if (wiimote.Button.d_left)
        {
            Vector3 leftMove = -mainCamera.transform.right * walkingSpeed * Time.deltaTime;
            rb.MovePosition(rb.position + leftMove);
        }
        if (wiimote.Button.d_right)
        {
            Vector3 rightMove = mainCamera.transform.right * walkingSpeed * Time.deltaTime;
            rb.MovePosition(rb.position + rightMove);
        }
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

        // Mouse look
        rotationX += Input.GetAxis("Mouse X") * mouseSensitivity;
        rotationY -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        rotationY = Mathf.Clamp(rotationY, -90f, 90f);

        mainCamera.transform.localRotation = Quaternion.Euler(rotationY, 0f, 0f);
        transform.localRotation = Quaternion.Euler(0f, rotationX, 0f);

        UpdateVignette(wasWalking);
    }

    private void FixedUpdate()
    {
        if (isWalking && currentMode != ControlMode.KeyboardMouse)
        {
            MovePlayer();
        }

        // Audio handling
        if (isWalking)
        {
            AudioClip currentFootstepSound = isOnSand ? sandWalkingAudioEffect : walkingAudioEffect;
            float basePitch = isOnSand ? sandSoundSpeed : 1.0f;
            float adjustedPitch = basePitch * currentSpeedFactor;

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

    // Debug info
    void OnGUI()
    {
        if (currentMode == ControlMode.WiiRemote && wiimote != null)
        {
            GUI.Label(new Rect(10, 10, 300, 20), "Control Mode: Wii Remote");
            GUI.Label(new Rect(10, 30, 300, 20), $"Walking: {isWalking}");
            GUI.Label(new Rect(10, 50, 300, 20), $"Speed Factor: {currentSpeedFactor:F2}");
            GUI.Label(new Rect(10, 70, 300, 20), $"Wii Accel Y: {wiiAcceleration.y:F2}");
        }
    }
}