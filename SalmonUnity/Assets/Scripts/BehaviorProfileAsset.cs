using UnityEngine;

/// <summary>Perfil guardado como asset (Create → Salmon Sim → Behavior Profile), para armar presets.</summary>
[CreateAssetMenu(menuName = "Salmon Sim/Behavior Profile", fileName = "BehaviorProfile")]
public class BehaviorProfileAsset : ScriptableObject
{
    public BehaviorProfile profile = new();
}
