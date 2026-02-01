using UnityEngine;
using System;
using System.Collections;
using Image = UnityEngine.UI.Image;
using WiimoteApi;

public class VRMenuButton : MonoBehaviour
{
    [Header("Visual Feedback")]
    [SerializeField] private Image gazeProgressCircle;
    [SerializeField] private Image buttonBackground;

    [Header("Selection Colors")]
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.3f);
    [SerializeField] private Color selectedColor = new Color(1f, 0.8f, 0.2f, 0.6f);
    [SerializeField] private Color activatedColor = new Color(0.2f, 1f, 0.2f, 0.8f);

    [Header("Vibration Settings")]
    [SerializeField] private bool enableVibration = true;
    [SerializeField] private float selectionVibrationDuration = 0.05f;
    [SerializeField] private float activationVibrationDuration = 0.1f;

    public event Action OnButtonActivated;

    private bool isGazing = false;
    private bool isSelected = false;
    private bool wasSelected = false;

    private static MonoBehaviour vibrationHandler;

    void Start()
    {
        if (gazeProgressCircle != null)
        {
            gazeProgressCircle.gameObject.SetActive(false);
            gazeProgressCircle.fillAmount = 0f;
        }

        // Set initial button color
        if (buttonBackground != null)
        {
            buttonBackground.color = normalColor;
        }

        if (vibrationHandler == null)
        {
            vibrationHandler = this;
        }
    }

    public void SetSelected(bool selected)
    {
        if (selected && !wasSelected && enableVibration)
        {
            VibrateWiimote(selectionVibrationDuration);
        }

        wasSelected = selected;
        isSelected = selected;
        UpdateVisuals();
    }

    public void StartGaze()
    {
        isGazing = true;

        if (gazeProgressCircle != null)
        {
            gazeProgressCircle.gameObject.SetActive(true);
        }
    }

    public void UpdateGazeProgress(float progress)
    {
        if (gazeProgressCircle != null)
        {
            gazeProgressCircle.fillAmount = progress;
        }
    }

    public void ResetGaze()
    {
        isGazing = false;

        if (gazeProgressCircle != null)
        {
            gazeProgressCircle.gameObject.SetActive(false);
            gazeProgressCircle.fillAmount = 0f;
        }

        UpdateVisuals();
    }

    public void ActivateButton()
    {
        OnButtonActivated?.Invoke();

        // Show activation feedback
        if (buttonBackground != null)
        {
            buttonBackground.color = activatedColor;
        }

        Invoke("ResetColor", 0.3f);
    }

    private void VibrateWiimote(float duration)
    {
        Wiimote wiimote = InputManager.wiimote;
        if (wiimote != null)
        {
            Debug.Log($"Vibrating Wiimote for {duration} seconds");
            MonoBehaviour handler = FindActiveHandler();
            if (handler != null)
            {
                handler.StartCoroutine(VibrateCoroutine(wiimote, duration));
            }
            else
            {
                Debug.LogWarning("No active handler found for vibration coroutine");
            }
        }
        else
        {
            Debug.LogWarning("Wiimote is null - cannot vibrate");
        }
    }

    private MonoBehaviour FindActiveHandler()
    {
        if (vibrationHandler != null && vibrationHandler.gameObject.activeInHierarchy)
        {
            return vibrationHandler;
        }

        MenuManager menuManager = FindObjectOfType<MenuManager>();
        if (menuManager != null)
        {
            return menuManager;
        }

        VRMenuButton[] buttons = FindObjectsOfType<VRMenuButton>();
        foreach (var button in buttons)
        {
            if (button.gameObject.activeInHierarchy)
            {
                return button;
            }
        }

        return null;
    }

    private static IEnumerator VibrateCoroutine(Wiimote wiimote, float duration)
    {
        if (wiimote == null) yield break;

        Debug.Log("Rumble ON");
        wiimote.RumbleOn = true;
        wiimote.SendStatusInfoRequest(); // Required to actually start vibration

        // LEDs work the same way, SendPlayerLED internally sends the packet
        wiimote.SendPlayerLED(true, false, false, false);

        yield return new WaitForSecondsRealtime(duration);

        Debug.Log("Rumble OFF");
        wiimote.RumbleOn = false;
        wiimote.SendStatusInfoRequest(); // Required to actually stop vibration
    }

    private void ResetColor()
    {
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (buttonBackground == null) return;

        if (isGazing)
        {
            buttonBackground.color = selectedColor;
        }
        else if (isSelected)
        {
            buttonBackground.color = selectedColor;
        }
        else
        {
            buttonBackground.color = normalColor;
        }
    }

    public bool IsSelected()
    {
        return isSelected;
    }
}