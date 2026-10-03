using UnityEngine;

/// <summary>Camara que orbita lentamente alrededor de la jaula (sin usar input).</summary>
public class CameraOrbit : MonoBehaviour
{
    public Vector3 target = new Vector3(0f, 0f, 5f);
    public float radius = 15f;
    public float height = 1.5f;
    public float degPerSec = 6f;
    float angle;

    void LateUpdate()
    {
        angle += degPerSec * Time.deltaTime;
        float a = angle * Mathf.Deg2Rad;
        transform.position = target + new Vector3(Mathf.Sin(a) * radius, height, -Mathf.Cos(a) * radius);
        transform.LookAt(target);
    }
}
