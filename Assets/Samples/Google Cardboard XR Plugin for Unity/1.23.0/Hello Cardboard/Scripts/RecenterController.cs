using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Integrated RecenterController that works with MenuManager
/// Handles recentering and delegates menu management to MenuManager
/// </summary>
public class RecenterController : MonoBehaviour
{
    [Header("Activation Settings")]
    [SerializeField] private float tiltActivationAngle = 30f;
    [SerializeField] private float tiltActivationTime = 2f;

    [Header("Recenter Settings")]
    [SerializeField] private float holdTime = 2.5f;
    [SerializeField] private float rotationThreshold = 2f;

    [Header("Menu Settings")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private MenuManager menuManager;
    [SerializeField] private float menuDistance = 2f;

    [Header("Visual Feedback")]
    [SerializeField] private Image progressCircle;
    [SerializeField] private Text feedbackText;
    [SerializeField] private Image tiltIndicator;

    [Header("Debug Panel (Mobile)")]
    [SerializeField] private Text debugText;

    [Header("Editor Testing")]
    [SerializeField] private bool enableEditorSimulation = true;
    [SerializeField] private KeyCode tiltLeftKey = KeyCode.L;
    [SerializeField] private KeyCode tiltRightKey = KeyCode.R;

    private float currentHoldTime = 0f;
    private float currentTiltTime = 0f;
    private Quaternion lastRotation;
    private bool isHolding = false;
    private bool isRecenterActivated = false;
    private bool isMenuActivated = false;
    private Camera mainCamera;
    private float simulatedTilt = 0f;

    private float initialYaw = 0f;
    private bool isInitialized = false;

    void Start()
    {
        mainCamera = Camera.main;

        // Get MenuManager reference if not assigned
        if (menuManager == null)
        {
            menuManager = FindObjectOfType<MenuManager>();
        }

        // Check if gyroscope is available
        if (SystemInfo.supportsGyroscope)
        {
            Input.gyro.enabled = true;
            Input.gyro.updateInterval = 0.01f;
            Debug.Log("Gyroscope enabled!");
        }
        else
        {
            Debug.LogWarning("Gyroscope not supported on this device!");
            if (debugText != null)
                debugText.text = "Gyroscope NOT supported!";
        }

        lastRotation = mainCamera.transform.rotation;

        HideFeedback();
        HideTiltIndicator();

        Invoke("InitializeRecenter", 0.5f);
    }

    private void InitializeRecenter()
    {
        initialYaw = mainCamera.transform.eulerAngles.y;
        isInitialized = true;
        Debug.Log($"Recenter initialized. Initial Yaw: {initialYaw}");
    }

    void Update()
    {
        if (!isInitialized)
            return;

        // Only check for tilt activation if menu is not open
        bool isMenuOpen = menuManager != null && menuManager.IsMenuOpen();

        if (!isMenuOpen)
        {
            // Check for tilt activation
            if (!isRecenterActivated && !isMenuActivated)
            {
                CheckTiltActivation();
            }
            else if (isRecenterActivated)
            {
                CheckRecenter();
            }
            else if (isMenuActivated)
            {
                CheckMenuHold();
            }
        }
    }

    private void CheckTiltActivation()
    {
        // Check head angle
        float rollAngle = GetRollAngle();

        // Left tilt - recenter
        if (rollAngle < -tiltActivationAngle)
        {
            currentTiltTime += Time.deltaTime;
            ShowTiltIndicator(currentTiltTime / tiltActivationTime, "left");

            if (currentTiltTime >= tiltActivationTime)
            {
                ActivateRecenter();
            }
        }
        // Right tilt - menu
        else if (rollAngle > tiltActivationAngle)
        {
            currentTiltTime += Time.deltaTime;
            ShowTiltIndicator(currentTiltTime / tiltActivationTime, "right");

            if (currentTiltTime >= tiltActivationTime)
            {
                ActivateMenu();
            }
        }
        else
        {
            currentTiltTime = 0f;
            HideTiltIndicator();
        }
    }

    private float GetRollAngle()
    {
#if UNITY_EDITOR
        if (enableEditorSimulation)
        {
            if (Input.GetKey(tiltLeftKey))
                simulatedTilt = Mathf.Lerp(simulatedTilt, -45f, Time.deltaTime * 3f);
            else if (Input.GetKey(tiltRightKey))
                simulatedTilt = Mathf.Lerp(simulatedTilt, 45f, Time.deltaTime * 3f);
            else
                simulatedTilt = Mathf.Lerp(simulatedTilt, 0f, Time.deltaTime * 5f);

            return simulatedTilt;
        }
        else
        {
            float roll = mainCamera.transform.localEulerAngles.z;
            return roll > 180f ? roll - 360f : roll;
        }
#else
        // Use camera rotation
        float roll = mainCamera.transform.localEulerAngles.z;
        if (roll > 180f) roll -= 360f;
        return roll;
#endif
    }

    private void ActivateRecenter()
    {
        isRecenterActivated = true;
        currentTiltTime = 0f;
        simulatedTilt = 0f;

        HideTiltIndicator();
        ShowFeedback("recenter");

        Debug.Log("Recenter activated! Hold head still...");
    }

    private void ActivateMenu()
    {
        isMenuActivated = true;
        currentTiltTime = 0f;
        simulatedTilt = 0f;

        HideTiltIndicator();
        ShowFeedback("menu");

        Debug.Log("Menu opening activated! Hold head still...");
    }

    private void CheckRecenter()
    {
        // Check movement since last frame
        float rotationDelta = Quaternion.Angle(lastRotation, mainCamera.transform.rotation);

        // Check if still enough
        if (rotationDelta < rotationThreshold)
        {
            // Still - increase the timer
            if (!isHolding)
            {
                isHolding = true;
            }

            currentHoldTime += Time.deltaTime;
            UpdateFeedback(currentHoldTime / holdTime);

            if (currentHoldTime >= holdTime)
            {
                RecenterView();
                ResetActivation();
            }
        }
        else
        {
            if (isHolding)
            {
                currentHoldTime = 0f;
                isHolding = false;
                UpdateFeedback(0f);
            }
        }

        lastRotation = mainCamera.transform.rotation;
    }

    private void CheckMenuHold()
    {
        float rotationDelta = Quaternion.Angle(lastRotation, mainCamera.transform.rotation);

        if (rotationDelta < rotationThreshold)
        {
            if (!isHolding)
            {
                isHolding = true;
            }

            currentHoldTime += Time.deltaTime;
            UpdateFeedback(currentHoldTime / holdTime);

            // Has time passed
            if (currentHoldTime >= holdTime)
            {
                OpenMenu();
                ResetActivation();
            }
        }
        else
        {
            // Reset timer
            if (isHolding)
            {
                currentHoldTime = 0f;
                isHolding = false;
                UpdateFeedback(0f);
            }
        }

        // Remember rotation for next frame
        lastRotation = mainCamera.transform.rotation;
    }

    private void RecenterView()
    {
        float currentYaw = mainCamera.transform.eulerAngles.y;

        // Rotate Player GameObject to compensate
        transform.Rotate(0, -currentYaw, 0, Space.World);

        Debug.Log($"Recentered! Yaw was: {currentYaw}");

        if (feedbackText != null)
        {
            feedbackText.text = "Recentered!";
        }

        Invoke("HideFeedbackDelayed", 1f);
    }

    private void OpenMenu()
    {
        if (menuPanel == null) return;

        // Position menu in front of player
        Vector3 menuPosition = mainCamera.transform.position + mainCamera.transform.forward * menuDistance;
        menuPanel.transform.position = menuPosition;

        // Make menu face the camera
        menuPanel.transform.LookAt(mainCamera.transform);
        menuPanel.transform.Rotate(0, 180, 0);

        // Use MenuManager to open the menu
        if (menuManager != null)
        {
            menuManager.ForceOpenMenu();
        }
        else
        {
            // Fallback if no MenuManager
            menuPanel.SetActive(true);
        }

        Debug.Log("Menu opened!");
    }

    private void ResetActivation()
    {
        currentHoldTime = 0f;
        isHolding = false;
        isRecenterActivated = false;
        isMenuActivated = false;

        HideFeedback();
    }

    private void ShowTiltIndicator(float progress, string direction)
    {
        if (tiltIndicator != null)
        {
            tiltIndicator.gameObject.SetActive(true);

            if (tiltIndicator.type == Image.Type.Filled)
            {
                tiltIndicator.fillAmount = progress;
            }
        }

        if (feedbackText != null)
        {
            feedbackText.gameObject.SetActive(true);
            feedbackText.text = direction == "left" ?
                "Tilt left to recenter..." :
                "Tilt right to open menu...";
        }
    }

    private void HideTiltIndicator()
    {
        if (tiltIndicator != null)
        {
            tiltIndicator.gameObject.SetActive(false);
        }

        if (feedbackText != null && !isRecenterActivated && !isMenuActivated)
        {
            feedbackText.gameObject.SetActive(false);
        }
    }

    private void ShowFeedback(string action)
    {
        if (progressCircle != null)
            progressCircle.gameObject.SetActive(true);

        if (feedbackText != null)
        {
            feedbackText.gameObject.SetActive(true);
            feedbackText.text = action == "recenter" ?
                "Hold still to recenter..." :
                "Hold still to open menu...";
        }
    }

    private void UpdateFeedback(float progress)
    {
        if (progressCircle != null)
        {
            progressCircle.fillAmount = progress;
        }
    }

    private void HideFeedback()
    {
        if (progressCircle != null)
            progressCircle.gameObject.SetActive(false);

        if (feedbackText != null)
            feedbackText.gameObject.SetActive(false);
    }

    private void HideFeedbackDelayed()
    {
        HideFeedback();
    }
}