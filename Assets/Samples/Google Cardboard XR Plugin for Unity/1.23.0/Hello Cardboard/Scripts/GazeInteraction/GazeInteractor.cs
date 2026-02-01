using UnityEngine;
using WiimoteApi;

public class GazeInteractor : MonoBehaviour
{
    public float gazeTime = 1.5f;
    public LayerMask interactLayer;
    [Header("Raycast Distance")]
    public float maxGazeDistance = 100f;

    private float timer;
    private IGazeInteractable currentInteractable;
    private UnifiedMovementController movementController;

    void Start()
    {
        // Find the UnifiedMovementController
        movementController = FindObjectOfType<UnifiedMovementController>();
        if (movementController == null)
        {
            Debug.LogError("GazeInteractor: Cannot find UnifiedMovementController!");
        }
    }

    void Update()
    {
        Ray ray = new Ray(transform.position, transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, maxGazeDistance, interactLayer))
        {
            IGazeInteractable interactable = hit.collider.GetComponent<IGazeInteractable>();

            // No gaze interactable on this object
            if (interactable == null)
            {
                if (currentInteractable != null)
                {
                    currentInteractable.OnGazeExit();
                    currentInteractable = null;
                }
                timer = 0;
                return;
            }

            // Gaze is temporarily blocked (doors animating)
            if (!interactable.CanGaze)
            {
                if (currentInteractable != null)
                {
                    currentInteractable.OnGazeExit();
                    currentInteractable = null;
                }
                timer = 0;
                return;
            }

            if (interactable != null)
            {
                // New object
                if (currentInteractable != interactable)
                {
                    // Exit previous
                    if (currentInteractable != null)
                        currentInteractable.OnGazeExit();

                    currentInteractable = interactable;
                    currentInteractable.OnGazeEnter();
                    timer = 0;
                }

                // Get current control mode
                UnifiedMovementController.ControlMode currentMode = GetCurrentControlMode();

                // Check for button press activation (Wii or Keyboard mode)
                bool buttonPressed = false;

                if (currentMode == UnifiedMovementController.ControlMode.WiiRemote)
                {
                    // Check if wiimote is connected and B button pressed
                    if (InputManager.inputs != null && WiimoteManager.HasWiimote())
                    {
                        if (InputManager.inputs.GetWiimoteButtonDown(Button.B))
                        {
                            buttonPressed = true;
                            Debug.Log("B Button pressed - activating object!");
                        }
                    }
                }
                else if (currentMode == UnifiedMovementController.ControlMode.KeyboardMouse)
                {
                    // Check for B key or LEFT MOUSE BUTTON
                    if (Input.GetKeyDown(KeyCode.B) || Input.GetMouseButtonDown(0))
                    {
                        buttonPressed = true;
                        if (Input.GetKeyDown(KeyCode.B))
                            Debug.Log("B Key pressed - activating object!");
                        else
                            Debug.Log("Mouse clicked - activating object!");
                    }
                }

                if (buttonPressed)
                {
                    // Instant activation on button press
                    currentInteractable.OnGazeActivate();
                    timer = 0;
                }
                else if (currentMode == UnifiedMovementController.ControlMode.MobileVR)
                {
                    // Mobile VR mode: use gaze timer
                    timer += Time.deltaTime;
                    float progress = Mathf.Clamp01(timer / gazeTime);
                    currentInteractable.UpdateGazeProgress(progress);

                    // Activate when filled
                    if (timer >= gazeTime)
                    {
                        currentInteractable.OnGazeActivate();
                        timer = 0;
                    }
                }
                else
                {
                    // For Wii/Keyboard, show progress but don't auto-activate
                    currentInteractable.UpdateGazeProgress(0f);
                }
            }
            else
            {
                Debug.LogWarning("No IGazeInteractable on " + hit.collider.name);
            }
        }
        else
        {
            // Exit gaze
            if (currentInteractable != null)
            {
                currentInteractable.OnGazeExit();
                currentInteractable = null;
            }
            timer = 0;
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
}