using UnityEngine;
using WiimoteApi;

public class VRGong : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip gongSound;
    [SerializeField] private AudioSource audioSource;

    [Header("Impact Settings")]
    [SerializeField] private float cooldownTime = 0.5f;

    [Header("Vibration Settings")]
    [SerializeField] private float vibrationDuration = 3f;

    private float lastHitTime = -999f;
    private bool pendingVibration = false;

    void Start()
    {
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.clip = gongSound;

        if (GetComponent<Collider>() == null)
        {
            gameObject.AddComponent<BoxCollider>();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        Debug.Log($"Time.timeScale: {Time.timeScale}");
        Debug.Log($"Time.unscaledTime: {Time.unscaledTime}");

        if (Time.time - lastHitTime < cooldownTime)
            return;

        audioSource.Play();
        Debug.Log($"Gong hit - Vibration triggered");

        if (InputManager.inputs != null)
        {
            // Use coroutine instead of async/await
            InputManager.inputs.StartCoroutine(
                InputManager.inputs.RumbleWiimoteForSecondsCoroutine(vibrationDuration)
            );
            Debug.Log("Rumble sent from OnCollisionEnter");
        }

        lastHitTime = Time.time;
    }
}