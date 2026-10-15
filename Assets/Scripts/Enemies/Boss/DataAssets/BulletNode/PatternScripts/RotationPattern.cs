<<<<<<< Updated upstream
using UnityEngine;

[CreateAssetMenu(fileName = "NewRotationPattern", menuName = "ScriptableObjects/BulletPattern/Rotation")]
public class RotationPattern : BulletPatternBase
{
    [Header("--- Rotation ---")]
    public float rotationBulletSpeed = 12f;
    public float rotationAngleIncrement = 15f;
    public bool enableAccerlation = true;
    public float accelerationRate = 5f;
    public float coef = 0.3f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        // ¡¾¶à¹ì»¯¹Ø¼ü¡¿Ğı×ªÀÛ¼ÓÆ÷¸Ä´Ó¹ìµÀÉÏÈ¡¡£
        // Ô­ÏÈÊÇ Emitter µÄ¹²Ïí×Ö¶Î£¬Á½Ìõ Rotation ¹ìµÀ²¢·¢Ê±»á»¥ÏàÇÀ£¬½Ç¶È»áÂÒÌø¡£
        float currentAngle = ctx.activeTrack != null ? ctx.activeTrack.angle : 0f;

        Quaternion rotation = Quaternion.Euler(0, 0, currentAngle);
        Vector2 dir = rotation * new Vector2(1, 0);
        GameObject bullet = ctx.host.SpawnProjectile(dir, ctx.firePoint.position, ctx.projectileKey, rotationBulletSpeed);

        // ¡¾ĞŞ¸Ä¡¿£ºÊ¹ÓÃĞÂµÄÎ»ÔËËã¼ì²éÊÇ·ñĞèÒª¹ÒÔØ¼ÓËÙÆ÷
        if (enableAccerlation)
        {
            BulletAcceleration acc = bullet.GetComponent<BulletAcceleration>();
            if (acc != null)
            {
                acc.Configure(accelerationRate, coef);
            }
            else
            {
                Debug.LogWarning($"[Emitter] {bullet.name} È±ÉÙ BulletAcceleration ×é¼ş£¬¼ÓËÙÎ´ÉúĞ§¡£", bullet);
            }
        }

        if (ctx.activeTrack != null) ctx.activeTrack.angle += rotationAngleIncrement;
=======
ï»¿using System;
using UnityEngine;

/// <summary>æ—‹è½¬ï¼šå‘å°„æ–¹å‘ä»èµ·å§‹è§’åº¦åŒ€é€Ÿè½¬åˆ°ç»“æŸè§’åº¦ï¼Œè½¬å®Œä»å¤´å†æ¥ï¼ˆèºæ—‹å¼¹å¹•ï¼‰</summary>
[Serializable, BulletPatternInfo("æ—‹è½¬ Rotation")]
public class RotationPattern : BulletPatternBase
{
    [Header("--- æ—‹è½¬ä¸“å± ---")]
    [Tooltip("å¼€å§‹å‘å°„çš„è§’åº¦ï¼ˆåº¦ï¼‰ã€‚0 = æ­£å³ï¼Œ90 = æ­£ä¸Š")]
    public float initialAngle = 0f;

    [Tooltip("è½¬åˆ°è¿™ä¸ªè§’åº¦åï¼Œé‡æ–°ä»èµ·å§‹è§’åº¦å¼€å§‹ã€‚æ¯”èµ·å§‹è§’åº¦å° = é¡ºæ—¶é’ˆè½¬")]
    public float endAngle = 360f;

    [Tooltip("ä»èµ·å§‹è§’åº¦è½¬åˆ°ç»“æŸè§’åº¦ç”¨å¤šä¹…ï¼ˆç§’ï¼‰")]
    [Min(0f)]
    public float rotationDuration = 3f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        EmitterTrack track = ctx.activeTrack;
        if (track == null) return;

        float currentAngle = float.IsNaN(track.angle) ? initialAngle : track.angle;

        Vector2 dir = Quaternion.Euler(0f, 0f, currentAngle) * Vector2.right;
        ctx.host.SpawnBullet(this, dir, ctx.firePoint.position);

        // ---- ä¸‹ä¸€å‘çš„è§’åº¦ ----
        float totalAngle = endAngle - initialAngle;
        if (rotationDuration <= 0f || Mathf.Approximately(totalAngle, 0f))
        {
            track.angle = initialAngle;
            return;
        }

        // æ¯å‘è½¬å¤šå°‘åº¦ = æ€»è§’åº¦ Ã— (å‘å°„é—´éš” / è½¬ä¸€åœˆç”¨æ—¶)
        float next = currentAngle + totalAngle * (track.interval / rotationDuration);
        bool passed = totalAngle > 0f ? next >= endAngle : next <= endAngle;
        track.angle = passed ? initialAngle : next;
>>>>>>> Stashed changes
    }
}
