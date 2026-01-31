using UnityEngine;
using System;
using Image = UnityEngine.UI.Image;

public class VRMenuButton : MonoBehaviour
{
    [Header("Visual Feedback")]
    [SerializeField] private Image gazeProgressCircle;
    [SerializeField] private Image buttonBackground;

    [Header("Selection Colors")]
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.3f);
    [SerializeField] private Color selectedColor = new Color(1f, 0.8f, 0.2f, 0.6f);
    [SerializeField] private Color activatedColor = new Color(0.2f, 1f, 0.2f, 0.8f);

    public event Action OnButtonActivated;

    private bool isGazing = false;
    private bool isSelected = false;

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
    }

    /// <summary>
    /// Set whether this button is currently selected (for keyboard/Wiimote navigation)
    /// </summary>
    public void SetSelected(bool selected)
    {
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

    private void ResetColor()
    {
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (buttonBackground == null)
        {
            Debug.LogError($"Button Background is NULL on {gameObject.name}!");
            return;
        }

        if (isGazing)
        {
            // Gaze has priority over selection
            buttonBackground.color = selectedColor;
        }
        else if (isSelected)
        {
            // Show selection state
            buttonBackground.color = selectedColor;
        }
        else
        {
            // Normal state
            buttonBackground.color = normalColor;
        }
    }

    public bool IsSelected()
    {
        return isSelected;
    }
}