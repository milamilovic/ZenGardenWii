using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

public class MenuManager : MonoBehaviour
{
    public enum InputMode
    {
        VRGaze,      // Mobile VR - gaze selection
        Wiimote,     // Wiimote controls
        KeyboardMouse // Keyboard/Mouse controls
    }

    [Header("Menu Settings")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private VRMenuButton[] menuButtons;

    [Header("Control Mode")]
    [SerializeField] private InputMode currentInputMode = InputMode.VRGaze;
    [SerializeField] private bool autoDetectMode = true;

    [Header("VR Gaze Settings")]
    [SerializeField] private Camera vrCamera;
    [SerializeField] private float gazeActivationTime = 1.5f;
    [SerializeField] private float gazeRayDistance = 5f;

    private int currentSelectedIndex = 0;
    private bool isMenuOpen = false;

    [SerializeField] private VRMenuButton pauseButton;
    private bool isPaused = false;

    // Input tracking to prevent double presses
    private bool wasAPressed = false;
    private bool wasBPressed = false;
    private bool wasMPressed = false;

    // VR Gaze tracking
    private VRMenuButton currentGazedButton = null;
    private float currentGazeTime = 0f;
    private UnifiedMovementController movementController;

    void Start()
    {

        // Find the UnifiedMovementController
        movementController = FindObjectOfType<UnifiedMovementController>();
        if (movementController == null)
        {
            Debug.LogError("GazeInteractor: Cannot find UnifiedMovementController!");
        }

        // Get camera reference if not assigned
        if (vrCamera == null)
        {
            vrCamera = Camera.main;
        }

        // Start with menu closed
        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
        }

        // Auto-detect control mode
        if (autoDetectMode)
        {
            DetectControlMode();
        }

        // Validate menu buttons
        if (menuButtons == null || menuButtons.Length == 0)
        {
            Debug.LogError("MenuManager: No menu buttons assigned!");
            return;
        }

        // Subscribe to button events
        for (int i = 0; i < menuButtons.Length; i++)
        {
            int index = i; // Capture for closure
            if (menuButtons[i] != null)
            {
                menuButtons[i].OnButtonActivated += () => OnButtonSelected(index);
            }
        }
    }

    void Update()
    {
        if (autoDetectMode)
        {
            DetectControlMode();
        }
        if (!isMenuOpen)
        {
            HandleMenuToggle();
        }
        else
        {
            // Handle navigation based on input mode
            switch (currentInputMode)
            {
                case InputMode.VRGaze:
                    HandleGazeSelection();
                    break;
                case InputMode.Wiimote:
                    HandleWiimoteNavigation();
                    break;
                case InputMode.KeyboardMouse:
                    HandleKeyboardNavigation();
                    break;
            }
        }
    }

    void DetectControlMode()
    {
        UnifiedMovementController.ControlMode currentMode = GetCurrentControlMode();
        if (currentMode == UnifiedMovementController.ControlMode.KeyboardMouse && currentInputMode != InputMode.KeyboardMouse)
        {
            currentInputMode = InputMode.KeyboardMouse;
            Debug.Log("MenuManager: Desktop platform - using Keyboard/Mouse controls");
        }
        else if (currentMode == UnifiedMovementController.ControlMode.MobileVR && currentInputMode != InputMode.VRGaze)
        {
            currentInputMode = InputMode.VRGaze;
            Debug.Log("MenuManager: Mobile platform - using VR Gaze controls");
        }
        else if (currentMode == UnifiedMovementController.ControlMode.WiiRemote && currentInputMode != InputMode.Wiimote)
        {
            currentInputMode = InputMode.Wiimote;
            Debug.Log("MenuManager: Wiimote detected - using Wiimote controls");
        }
    }

    private UnifiedMovementController.ControlMode GetCurrentControlMode()
    {
        if (movementController != null)
        {
            // Use reflection to get the current mode since it's private
            var field = typeof(UnifiedMovementController).GetField("currentMode",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                return (UnifiedMovementController.ControlMode)field.GetValue(movementController);
            }
        }

        // Default to MobileVR if can't determine
        return UnifiedMovementController.ControlMode.MobileVR;
    }

    void HandleMenuToggle()
    {
        bool shouldToggle = false;

        switch (currentInputMode)
        {
            case InputMode.KeyboardMouse:
                // M key toggles menu
                if (Input.GetKeyDown(KeyCode.M))
                {
                    shouldToggle = true;
                }
                break;

            case InputMode.Wiimote:
                // A button opens menu when closed
                if (InputManager.inputs != null)
                {
                    bool aPressed = InputManager.inputs.GetWiimoteButtonDown(Button.A);

                    if (aPressed && !wasAPressed)
                    {
                        shouldToggle = true;
                    }

                    wasAPressed = aPressed;
                }
                break;

            case InputMode.VRGaze:
                // VR gaze mode: menu is opened by RecenterController
                // Don't toggle from here
                break;
        }

        if (shouldToggle)
        {
            OpenMenu();
        }
    }

    void HandleGazeSelection()
    {
        if (vrCamera == null) return;

        Ray ray = new Ray(vrCamera.transform.position, vrCamera.transform.forward);
        RaycastHit hit;

        VRMenuButton hitButton = null;

        // Check if looking at a button
        if (Physics.Raycast(ray, out hit, gazeRayDistance))
        {
            hitButton = hit.collider.GetComponent<VRMenuButton>();
        }

        // If looking at a button
        if (hitButton != null)
        {
            if (currentGazedButton == hitButton)
            {
                // Continue gazing at same button
                currentGazeTime += Time.unscaledDeltaTime;

                // Update button progress
                hitButton.UpdateGazeProgress(currentGazeTime / gazeActivationTime);

                // Activate button after gaze time
                if (currentGazeTime >= gazeActivationTime)
                {
                    hitButton.ActivateButton();
                    ResetGaze();
                }
            }
            else
            {
                // Started looking at new button
                if (currentGazedButton != null)
                {
                    currentGazedButton.ResetGaze();
                }

                currentGazedButton = hitButton;
                currentGazeTime = 0f;
                hitButton.StartGaze();
            }
        }
        else
        {
            // Not looking at any button
            if (currentGazedButton != null)
            {
                currentGazedButton.ResetGaze();
            }
            ResetGaze();
        }
    }

    void HandleWiimoteNavigation()
    {
        if (menuButtons == null || menuButtons.Length == 0) return;
        if (InputManager.inputs == null) return;

        bool bPressed = InputManager.inputs.GetWiimoteButtonDown(Button.B);
        bool aPressed = InputManager.inputs.GetWiimoteButtonDown(Button.A);

        // Navigate with B button
        if (bPressed && !wasBPressed)
        {
            SelectNextButton();
        }

        // Select with A button
        if (aPressed && !wasAPressed)
        {
            ActivateCurrentButton();
        }

        wasBPressed = bPressed;
        wasAPressed = aPressed;
    }

    void HandleKeyboardNavigation()
    {
        if (menuButtons == null || menuButtons.Length == 0) return;

        // Navigation
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.Tab))
        {
            SelectNextButton();
        }
        else if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            SelectPreviousButton();
        }

        // Selection
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
        {
            ActivateCurrentButton();
        }

        // Close menu
        else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.M))
        {
            CloseMenu();
        }
    }

    public void OpenMenu()
    {
        isMenuOpen = true;

        if (menuPanel != null)
        {
            menuPanel.SetActive(true);
        }

        // For non-gaze modes, select first button
        if (currentInputMode != InputMode.VRGaze)
        {
            currentSelectedIndex = 0;
            UpdateButtonSelection();
            UpdateWiimoteLEDs();
            pauseButton.SetSelected(true);
        }

        Debug.Log($"Menu opened in {currentInputMode} mode");
    }

    public void CloseMenu()
    {
        isMenuOpen = false;

        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
        }

        UpdateWiimoteLEDs();

        // Reset all button states
        ResetGaze();
        foreach (var button in menuButtons)
        {
            if (button != null)
            {
                button.SetSelected(false);
                button.ResetGaze();
            }
        }

        Debug.Log("Menu closed");
    }

    void SelectNextButton()
    {
        if (menuButtons.Length == 0) return;

        currentSelectedIndex = (currentSelectedIndex + 1) % menuButtons.Length;
        UpdateButtonSelection();
    }

    void SelectPreviousButton()
    {
        if (menuButtons.Length == 0) return;

        currentSelectedIndex--;
        if (currentSelectedIndex < 0)
        {
            currentSelectedIndex = menuButtons.Length - 1;
        }
        UpdateButtonSelection();
    }

    void UpdateButtonSelection()
    {
        // Deselect all buttons
        for (int i = 0; i < menuButtons.Length; i++)
        {
            if (menuButtons[i] != null)
            {
                menuButtons[i].SetSelected(i == currentSelectedIndex);
            }
        }

        UpdateWiimoteLEDs();

        Debug.Log($"Selected button {currentSelectedIndex}");
    }

    void ActivateCurrentButton()
    {
        if (currentSelectedIndex >= 0 && currentSelectedIndex < menuButtons.Length)
        {
            if (menuButtons[currentSelectedIndex] != null)
            {
                menuButtons[currentSelectedIndex].ActivateButton();
                Debug.Log($"Activated button {currentSelectedIndex}");
            }
        }
    }

    void ResetGaze()
    {
        currentGazedButton = null;
        currentGazeTime = 0f;
    }

    void OnButtonSelected(int index)
    {
        switch (index)
        {
            case 0: // First button
                PauseGame();
                break;
            case 1: // Second button
                ResumeGame();
                break;
            case 2: // Third button
                QuitGame();
                break;
        }
    }

    public void ResumeGame()
    {
        CloseMenu();
        if (isPaused)
        {
            Time.timeScale = 1f;
            isPaused = false;
        }
        Debug.Log("Game resumed!");
    }

    public void PauseGame()
    {
        isPaused = !isPaused;
        Time.timeScale = isPaused ? 0f : 1f;

        if (pauseButton != null)
        {
            Text buttonText = pauseButton.GetComponentInChildren<Text>();
            if (buttonText != null)
            {
                buttonText.text = isPaused ? "Resume" : "Pause";
            }
        }

        Debug.Log(isPaused ? "Game paused!" : "Game resumed!");
    }

    public void QuitGame()
    {
        Debug.Log("Quitting game...");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Public API
    public void SetInputMode(InputMode mode)
    {
        currentInputMode = mode;
        Debug.Log($"MenuManager: Input mode set to {mode}");

        // Reset selection when changing modes
        if (isMenuOpen)
        {
            ResetGaze();
            if (mode != InputMode.VRGaze)
            {
                currentSelectedIndex = 0;
                UpdateButtonSelection();
            }
        }
    }

    public InputMode GetInputMode()
    {
        return currentInputMode;
    }

    public bool IsMenuOpen()
    {
        return isMenuOpen;
    }

    public void ForceCloseMenu()
    {
        CloseMenu();
    }

    public void ForceOpenMenu()
    {
        OpenMenu();
    }

    private void UpdateWiimoteLEDs()
    {
        if (InputManager.wiimote == null) return;

        if (!isMenuOpen)
        {
            // Default state: Only LED 1
            InputManager.wiimote.SendPlayerLED(true, false, false, false);
            return;
        }

        // Map selection index to LEDs
        switch (currentSelectedIndex)
        {
            case 0: // First Button
                InputManager.wiimote.SendPlayerLED(true, false, false, false);
                break;
            case 1: // Second Button
                InputManager.wiimote.SendPlayerLED(false, true, false, false);
                break;
            case 2: // Third Button
                InputManager.wiimote.SendPlayerLED(false, false, true, false);
                break;
            default: // Any other buttons (if any) loop back to LED 1
                InputManager.wiimote.SendPlayerLED(true, false, false, false);
                break;
        }
    }
}