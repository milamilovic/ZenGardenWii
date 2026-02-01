using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using WiimoteApi;

public class KanjiCanvas : MonoBehaviour, IGazeInteractable
{
    [Header("Canvas References")]
    public Canvas gazeCanvas;
    public Image fillCircle;

    [Header("Visual Effects")]
    [SerializeField] private GazeVisualEffect visualEffects;

    [Header("Drawing Settings")]
    public Material drawingMaterial;
    public float brushSize = 0.02f;
    public Color brushColor = Color.black;
    public LayerMask canvasLayer;

    [Header("Kanji Information")]
    public List<KanjiInfo> kanjiList = new List<KanjiInfo>();

    private bool isActive = false;
    private bool isDrawing = false;
    private UnifiedMovementController movementController;
    private LineRenderer currentStroke;
    private List<LineRenderer> strokes = new List<LineRenderer>();
    private Vector3 lastDrawPoint;

    // Changed: CanGaze should be true when NOT active (like the rake)
    public bool CanGaze => !isActive;

    private RectTransform cachedPointerRect;
    private float lastProgress = -1f;
    private RectTransform pointerRect;

    [System.Serializable]
    public class KanjiInfo
    {
        public string character;
        public string meaning;
        public string pronunciation;
        [TextArea(3, 6)]
        public string description;
    }

    void Start()
    {
        if (gazeCanvas != null)
            gazeCanvas.enabled = false;
        if (fillCircle != null)
            fillCircle.fillAmount = 0f;

        if (visualEffects == null)
        {
            visualEffects = GetComponent<GazeVisualEffect>();
        }

        movementController = FindFirstObjectByType<UnifiedMovementController>();

        // Initialize some sample kanji
        InitializeSampleKanji();
        GameObject pointerObj = GameObject.Find("WiimotePointer");
        if (pointerObj != null) pointerRect = pointerObj.GetComponent<RectTransform>();

        // DEBUG: Check layer setup
        Debug.Log($"Canvas Layer Mask: {canvasLayer.value}");
        Debug.Log($"Canvas Layer Mask includes layer {LayerMask.LayerToName(LayerMask.NameToLayer("Canvas"))}: {((canvasLayer.value & (1 << LayerMask.NameToLayer("Canvas"))) != 0)}");

        // Check all children
        foreach (Transform child in transform)
        {
            Debug.Log($"Child '{child.name}' is on layer: {LayerMask.LayerToName(child.gameObject.layer)}");
            BoxCollider col = child.GetComponent<BoxCollider>();
            if (col != null)
            {
                Debug.Log($"  - Has BoxCollider, isTrigger: {col.isTrigger}");
            }
        }
    }

    void InitializeSampleKanji()
    {
        kanjiList.Add(new KanjiInfo
        {
            character = "愛",
            meaning = "Love",
            pronunciation = "ai",
            description = "This kanji represents love and affection. It combines elements meaning 'to receive' and 'heart'."
        });

        kanjiList.Add(new KanjiInfo
        {
            character = "平和",
            meaning = "Peace",
            pronunciation = "heiwa",
            description = "Combines 'flat/level' (平) and 'harmony' (和) to mean peace."
        });

        kanjiList.Add(new KanjiInfo
        {
            character = "道",
            meaning = "Way/Path",
            pronunciation = "dō",
            description = "Represents a path or way, both physical and philosophical. Used in words like 'karate-dō'."
        });
    }

    void Update()
    {
        // Only handle drawing controls when active
        if (isActive)
        {
            HandleDrawing();
            HandleDeselection();
        }
    }

    void HandleDrawing()
    {
        if (InputManager.inputs == null || !WiimoteManager.HasWiimote() || pointerRect == null) return;

        Wiimote wiimote = WiimoteManager.Wiimotes[0];

        // OPTIMIZATION: Only raycast if B button is actually held
        if (!wiimote.Button.b)
        {
            if (isDrawing) { isDrawing = false; currentStroke = null; }

            // Still check buttons for clearing/switching
            if (InputManager.inputs.GetWiimoteButtonDown(Button.A)) ClearCanvas();
            if (InputManager.inputs.GetWiimoteButtonDown(Button.Plus)) ShowNextKanji();
            if (InputManager.inputs.GetWiimoteButtonDown(Button.Minus)) ShowPreviousKanji();
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(pointerRect.position);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 10f, canvasLayer))
        {
            // Simple check: is it this object or a child?
            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
            {
                if (!isDrawing) { StartNewStroke(); isDrawing = true; }
                AddPointToStroke(hit.point);
            }
        }
    }

    void HandleDeselection()
    {
        // Press Home button or 1 button to exit drawing mode
        if (InputManager.inputs != null && WiimoteManager.HasWiimote())
        {
            if (InputManager.inputs.GetWiimoteButtonDown(Button.Home) ||
                InputManager.inputs.GetWiimoteButtonDown(Button.One))
            {
                DeactivateCanvas();
            }
        }
    }

    void StartNewStroke()
    {
        GameObject strokeObj = new GameObject("Stroke");
        strokeObj.transform.parent = transform;

        currentStroke = strokeObj.AddComponent<LineRenderer>();
        currentStroke.material = drawingMaterial;
        currentStroke.startWidth = brushSize;
        currentStroke.endWidth = brushSize;
        currentStroke.startColor = brushColor;
        currentStroke.endColor = brushColor;
        currentStroke.positionCount = 0;
        currentStroke.useWorldSpace = true;

        strokes.Add(currentStroke);
    }

    void AddPointToStroke(Vector3 point)
    {
        if (currentStroke == null) return;

        // Only add point if it's far enough from the last point
        if (currentStroke.positionCount == 0 ||
            Vector3.Distance(point, lastDrawPoint) > 0.005f)
        {
            currentStroke.positionCount++;
            currentStroke.SetPosition(currentStroke.positionCount - 1, point + transform.forward * 0.001f);
            lastDrawPoint = point;
        }
    }

    void ClearCanvas()
    {
        foreach (LineRenderer stroke in strokes)
        {
            if (stroke != null)
                Destroy(stroke.gameObject);
        }
        strokes.Clear();
        currentStroke = null;
        isDrawing = false;

        Debug.Log("Canvas cleared!");
    }

    int currentKanjiIndex = 0;

    void ShowNextKanji()
    {
        if (kanjiList.Count == 0) return;
        currentKanjiIndex = (currentKanjiIndex + 1) % kanjiList.Count;
        DisplayKanjiInfo();
    }

    void ShowPreviousKanji()
    {
        if (kanjiList.Count == 0) return;
        currentKanjiIndex--;
        if (currentKanjiIndex < 0) currentKanjiIndex = kanjiList.Count - 1;
        DisplayKanjiInfo();
    }

    void DisplayKanjiInfo()
    {
        KanjiInfo kanji = kanjiList[currentKanjiIndex];
        Debug.Log($"Kanji: {kanji.character} ({kanji.pronunciation}) - {kanji.meaning}\n{kanji.description}");
        // TODO: Display this on a UI panel near the canvas
    }

    // IGazeInteractable Implementation - NOW WORKS LIKE RAKE
    public void OnGazeEnter()
    {
        // Don't show UI when already active
        if (isActive) return;

        if (gazeCanvas != null)
            gazeCanvas.enabled = true;
        if (fillCircle != null)
            fillCircle.fillAmount = 0f;

        // Show visual effects when looking at canvas
        if (visualEffects != null)
        {
            visualEffects.ShowEffects();
        }

        Debug.Log("Gazing at canvas - visual effects shown");
    }

    public void OnGazeExit()
    {
        // Don't modify UI when already active
        if (isActive) return;

        if (gazeCanvas != null)
            gazeCanvas.enabled = false;
        if (fillCircle != null)
            fillCircle.fillAmount = 0f;

        // Hide visual effects when not looking
        if (visualEffects != null)
        {
            visualEffects.HideEffects();
        }

        Debug.Log("Stopped gazing at canvas - visual effects hidden");
    }

    public void UpdateGazeProgress(float progress)
    {
        // Don't update progress when already active
        if (isActive) return;

        // Only update if the change is visible (prevents constant UI rebuilding)
        if (fillCircle != null && Mathf.Abs(progress - lastProgress) > 0.01f)
        {
            fillCircle.fillAmount = progress;
            lastProgress = progress;
        }
    }

    public void OnGazeActivate()
    {
        // Toggle between active and inactive
        if (isActive)
        {
            DeactivateCanvas();
        }
        else
        {
            ActivateCanvas();
        }

        // Reset fill circle
        if (fillCircle != null)
            fillCircle.fillAmount = 0f;
    }

    void ActivateCanvas()
    {
        isActive = true;

        // Hide the gaze UI elements
        if (gazeCanvas != null)
            gazeCanvas.enabled = false;

        // Hide visual effects during drawing mode
        if (visualEffects != null)
        {
            visualEffects.HideEffects();
        }

        // Disable movement
        if (movementController != null)
        {
            movementController.SetDrawingMode(true);
        }

        // Show instructions
        Debug.Log("=== DRAWING MODE ACTIVATED ===");
        Debug.Log("B Button (hold) = Draw");
        Debug.Log("A Button = Clear Canvas");
        Debug.Log("+/- Buttons = Change Kanji");
        Debug.Log("Home or 1 Button = Exit Drawing Mode");
        Debug.Log("D-Pad = Look Around");

        DisplayKanjiInfo();
    }

    void DeactivateCanvas()
    {
        isActive = false;
        isDrawing = false;
        currentStroke = null;

        // Re-enable movement
        if (movementController != null)
        {
            movementController.SetDrawingMode(false);
        }

        Debug.Log("=== DRAWING MODE DEACTIVATED ===");
        Debug.Log("Movement re-enabled");
    }
}