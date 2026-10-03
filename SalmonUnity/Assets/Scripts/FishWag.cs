using UnityEngine;

/// <summary>Mueve la cola del pez de lado a lado para simular el nado.</summary>
public class FishWag : MonoBehaviour
{
    public Transform tail;
    public float phase;
    public float freq = 6.5f;
    public float amplitude = 28f;

    void Update()
    {
        if (tail == null) return;
        tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * freq + phase) * amplitude, 0f);
    }
}
