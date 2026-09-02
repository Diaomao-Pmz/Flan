using UnityEngine;

public class BulletPatternBase : ScriptableObject
{
    public float DefaultInterval;
    public virtual void Spawn(BulletSpawnContext ctx) { }
}
