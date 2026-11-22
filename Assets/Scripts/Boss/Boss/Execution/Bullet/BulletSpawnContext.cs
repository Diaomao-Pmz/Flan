using UnityEngine;

public struct BulletSpawnContext
{
    public Transform firePoint;
    public Transform player;
    public Vector2 playerTargetPosition;
    public string projectileKey;
    public EmitterTrack activeTrack;      // 拿 angle / formationDuration
    public BAE_BulletEmitter host;  // 需要建阵型父物体时用

    public void SetUp(Transform firePoint, Transform player, Vector2 playerTargetPosition, string projectileKey, EmitterTrack activeTrack, BAE_BulletEmitter host)
    {
        this.firePoint = firePoint;
        this.player = player;
        this.playerTargetPosition = playerTargetPosition;
        this.projectileKey = projectileKey;
        this.activeTrack = activeTrack;
        this.host = host;
    }
}
