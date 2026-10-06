using System;
using UnityEngine;

/// <summary>
/// Parámetros de comportamiento de un cardumen simulado (FishSchool).
/// Todos son continuos para poder interpolar entre perfiles con <see cref="Lerp"/>
/// (la fase 4 los cambiará según etapa, estación y hora del día).
/// El sentido de giro del anillo no está aquí porque no se interpola: es de cada jaula.
/// </summary>
[Serializable]
public class BehaviorProfile
{
    [Header("Población")]
    [Tooltip("Número de peces (se redondea; el máximo lo fija FishSchool.maxFish)")]
    [Range(0f, 300f)] public float fishCount = 220f;
    [Tooltip("Largo medio del pez (m)")]
    [Range(0.1f, 1.2f)] public float fishLength = 0.65f;

    [Header("Nado")]
    [Tooltip("Velocidad media de crucero (m/s)")]
    [Range(0.05f, 2f)] public float meanSpeed = 0.55f;
    [Tooltip("Variación individual de la velocidad (fracción, ±)")]
    [Range(0f, 0.5f)] public float speedVariation = 0.15f;
    [Tooltip("Aceleración máxima (m/s²): qué tan bruscos son los giros")]
    [Range(0.1f, 5f)] public float maxAccel = 1.2f;
    [Tooltip("Deambular: aceleración aleatoria por pez (m/s²); más alto = cardumen menos ordenado")]
    [Range(0f, 2f)] public float wander = 0.05f;

    [Header("Vecinos (boids)")]
    [Range(0.3f, 4f)] public float neighborRadius = 1.6f;
    [Range(0.1f, 2f)] public float separationRadius = 0.7f;
    [Range(0f, 5f)] public float separationWeight = 1.6f;
    [Range(0f, 5f)] public float alignmentWeight = 1.0f;
    [Range(0f, 5f)] public float cohesionWeight = 0.5f;

    [Header("Jaula")]
    [Tooltip("Distancia (m) a la red desde la que empieza a esquivarla")]
    [Range(0.3f, 4f)] public float wallMargin = 1.6f;
    [Range(0f, 10f)] public float wallWeight = 4f;
    [Tooltip("Profundidad preferida (m bajo la superficie)")]
    [Range(0.5f, 20f)] public float preferredDepth = 4f;
    [Tooltip("Dispersión de la profundidad preferida entre peces (m, ±)")]
    [Range(0f, 6f)] public float depthSpread = 1.5f;
    [Range(0f, 5f)] public float depthWeight = 0.8f;

    [Header("Giro en anillo")]
    [Tooltip("Qué tanto sigue el cardumen el giro alrededor del centro (0 = nada)")]
    [Range(0f, 5f)] public float circlingWeight = 1.2f;
    [Tooltip("Radio del anillo como fracción del semilado de la jaula")]
    [Range(0f, 1f)] public float ringRadius = 0.55f;

    [Header("Alimentación")]
    [Tooltip("Fracción de peces con hambre durante una comida (0–1); las comidas las maneja FishSchool")]
    [Range(0f, 1f)] public float appetite = 0f;

    public BehaviorProfile Clone() => (BehaviorProfile)MemberwiseClone();

    /// Interpolación lineal campo a campo (t se limita a 0..1).
    public static BehaviorProfile Lerp(BehaviorProfile a, BehaviorProfile b, float t)
    {
        t = Mathf.Clamp01(t);
        return new BehaviorProfile
        {
            fishCount = Mathf.Lerp(a.fishCount, b.fishCount, t),
            fishLength = Mathf.Lerp(a.fishLength, b.fishLength, t),
            meanSpeed = Mathf.Lerp(a.meanSpeed, b.meanSpeed, t),
            speedVariation = Mathf.Lerp(a.speedVariation, b.speedVariation, t),
            maxAccel = Mathf.Lerp(a.maxAccel, b.maxAccel, t),
            wander = Mathf.Lerp(a.wander, b.wander, t),
            neighborRadius = Mathf.Lerp(a.neighborRadius, b.neighborRadius, t),
            separationRadius = Mathf.Lerp(a.separationRadius, b.separationRadius, t),
            separationWeight = Mathf.Lerp(a.separationWeight, b.separationWeight, t),
            alignmentWeight = Mathf.Lerp(a.alignmentWeight, b.alignmentWeight, t),
            cohesionWeight = Mathf.Lerp(a.cohesionWeight, b.cohesionWeight, t),
            wallMargin = Mathf.Lerp(a.wallMargin, b.wallMargin, t),
            wallWeight = Mathf.Lerp(a.wallWeight, b.wallWeight, t),
            preferredDepth = Mathf.Lerp(a.preferredDepth, b.preferredDepth, t),
            depthSpread = Mathf.Lerp(a.depthSpread, b.depthSpread, t),
            depthWeight = Mathf.Lerp(a.depthWeight, b.depthWeight, t),
            circlingWeight = Mathf.Lerp(a.circlingWeight, b.circlingWeight, t),
            ringRadius = Mathf.Lerp(a.ringRadius, b.ringRadius, t),
            appetite = Mathf.Lerp(a.appetite, b.appetite, t),
        };
    }
}
