using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem;
using Unity.VisualScripting;

/// <summary>
/// Boss 激光执行器。认领 LaserNode。
/// 方向默认在预警开始瞬间锁定；LaserNode 的追踪时间可以让预警线 / 光束先追玩家一段时间再锁定。
///
/// 【绘制方式：缩放拉伸，而非 Tiled 平铺】
/// 激光贴图的颜色沿光束长度方向是均匀的（只在宽度方向有渐变），所以拉伸是无损的。
/// 反过来说 Tiled 模式**两个轴都会平铺** —— 宽度方向也会重复，
/// 会把那条渐变复制成好几道。需要真正的流动纹理时再打开 tileTexture。
/// </summary>
public class BAE_LaserAttacker : MonoBehaviour, IBossActionExecutor
{
    [Header("--- 判定目标 ---")]
    [Tooltip("哪些图层会被激光命中。通常只勾 Player。")]
    [SerializeField] private LayerMask targetLayers;

    [Tooltip("是否把触发器（Trigger）也纳入判定")]
    [SerializeField] private bool detectTriggers = true;

    [Header("--- 遮挡 ---")]
    [Tooltip("不勾 = 激光永远保持直线打满 maxLength，不被任何东西截断（推荐）。\n" +
             "勾上 = 被 Obstacle Layers 里的东西挡住时截断。")]
    [SerializeField] private bool stopAtObstacle = false;

    [Tooltip("哪些图层会挡住激光。\n" +
             "【注意】千万别把玩家所在图层勾进来 —— 那会让激光打到玩家身上就停下。\n" +
             "同理也别勾 Boss 自己的图层，否则射线在枪口处就被自己挡住，光束长度会变成 0。")]
    [SerializeField] private LayerMask obstacleLayers;

    [Header("--- 表现层 ---")]
    [Tooltip("激光的发射原点。建议拖入 Boss 下的 Firepoint。留空则用 Boss 自身位置。")]
    [SerializeField] private Transform laserOrigin;

    [Tooltip("勾选 = 贴图竖着画（光束沿 +Y，Pivot 设 Bottom·Center）。东方系激光素材属于这种。")]
    [SerializeField] private bool spriteIsVertical = true;

    [Tooltip("勾选后沿长度方向平铺纹理而非拉伸。纯色渐变的激光素材保持不勾。")]
    [SerializeField] private bool tileTexture = false;

    [Header("--- Debug ---")]
    [SerializeField] private bool showHitbox = true;

    private Animator animator;
    private ContactFilter2D contactFilter;

    private readonly List<Collider2D> hitBuffer = new List<Collider2D>(16);
    private readonly HashSet<Collider2D> tickBlacklist = new HashSet<Collider2D>();

    private bool beamActive;
    private Vector2 gizmoCenter;
    private Vector2 gizmoSize;
    private float gizmoAngle;

    //存储正在运行的激光，用于打断
    private List<GameObject> currentActiveLasers = new List<GameObject>();

    private void Awake()
    {
        animator = GetComponent<Animator>();
        contactFilter = HitboxUtility.BuildFilter(targetLayers, detectTriggers);
    }

    public System.Type NodeType => typeof(LaserNode);

    public IEnumerator Execute(ActionNode node, BossContext ctx)
    {
        LaserNode laser = node as LaserNode;
        if (laser == null) yield break;

        Vector2 origin = OriginPos;

        // 预警开始时先对准玩家一次。
        // 追踪时间为 0 时这就是最终方向 —— 「可躲」的来源：玩家有 telegraphTime 秒离开这条线。
        Vector2 direction = ResolveDirection(ctx, origin, laser.aimAngleOffset);

        SpriteRenderer beamRenderer = ObjectPoolManager.Instance.Get("Laser").GetComponent<SpriteRenderer>();
        currentActiveLasers.Add(beamRenderer.gameObject);

        PrepareRenderer(beamRenderer, laser);

        // ---- 预警 ----
        PlayAnim(laser.chargeAnimName);

        float elapsed = 0f;
        while (elapsed < laser.telegraphTime)
        {
            // 追踪期内：预警线跟着玩家转；过了追踪期：方向锁定，玩家开始读招闪避
            if (elapsed < laser.telegraphTrackTime)
                direction = TrackDirection(direction, ctx, laser);

            DrawBeam(beamRenderer, OriginPos, direction, laser, true);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // ---- 开火 ----
        PlayAnim(laser.activeAnimName);

        elapsed = 0f;
        float tickTimer = laser.damageTickInterval; // 开火瞬间立刻打第一跳

        while (elapsed < laser.fireTime)
        {
            // 开火追踪：光束继续朝玩家扫，判定框用同一个方向，所以会跟着一起转
            if (elapsed < laser.fireTrackTime)
                direction = TrackDirection(direction, ctx, laser);

            float length = DrawBeam(beamRenderer, OriginPos, direction, laser, false);

            tickTimer += Time.deltaTime;
            if (tickTimer >= laser.damageTickInterval)
            {
                tickTimer = 0f;
                DamageTick(OriginPos, direction, length, laser, ctx);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // ---- 收招 ----
        if (currentActiveLasers.Contains(beamRenderer.gameObject))
        {
            currentActiveLasers.Remove(beamRenderer.gameObject);
        }
        ObjectPoolManager.Instance.Recycle(beamRenderer.gameObject);
        PlayAnim(laser.recoverAnimName);

        if (laser.recoverTime > 0f)
            yield return new WaitForSeconds(laser.recoverTime);
    }

    public void Cancel()
    {
        HideBeam();
        tickBlacklist.Clear();
    }

    // ==========================================================
    //  判定
    // ==========================================================

    private void DamageTick(Vector2 origin, Vector2 direction, float length, LaserNode laser, BossContext ctx)
    {
        // 每一跳都重新清空黑名单 —— 与近战相反：近战要「只打一次」，激光要「每跳都打」。
        // 光束整条都参与判定，命中玩家后也不会截断，后面的目标照打。
        tickBlacklist.Clear();

        Vector2 center = origin + direction * (length * 0.5f);
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        Physics2D.OverlapBox(center, new Vector2(length, laser.beamWidth), angle, contactFilter, hitBuffer);

        HitboxUtility.ApplyDamage(
            hitBuffer,
            tickBlacklist,
            laser.damagePerTick,
            DamageType.Ranged,
            origin,
            gameObject,
            ctx.controller,
            laser.overrideKnockback,
            laser.knockbackForce);
    }

    // ==========================================================
    //  表现
    // ==========================================================

    private void PrepareRenderer(SpriteRenderer beamRenderer, LaserNode laser)
    {
        if (beamRenderer == null) return;

        if (laser.beamSprite != null) beamRenderer.sprite = laser.beamSprite;

        if (tileTexture)
        {
            beamRenderer.drawMode = SpriteDrawMode.Tiled;
            beamRenderer.tileMode = SpriteTileMode.Continuous;
        }
        else
        {
            beamRenderer.drawMode = SpriteDrawMode.Simple;
        }
    }

    /// <summary>画出光束并返回实际长度。</summary>
    private float DrawBeam(SpriteRenderer beamRenderer, Vector2 origin, Vector2 direction, LaserNode laser, bool isTelegraph)
    {
        // 默认不截断：激光保持直线打满全长，穿过一切目标。
        float length = stopAtObstacle
            ? HitboxUtility.RaycastLength(origin, direction, laser.maxLength, obstacleLayers)
            : laser.maxLength;

        float width = isTelegraph ? laser.telegraphWidth : laser.beamWidth;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        beamActive = true;

        // Gizmos 数据每帧更新，保证预警阶段也能看到当前这次施法的判定框
        gizmoCenter = origin + direction * (length * 0.5f);
        gizmoSize = new Vector2(length, width);
        gizmoAngle = angle;

        if (beamRenderer != null)
        {
            beamRenderer.enabled = true;
            beamRenderer.color = isTelegraph ? laser.telegraphColor : laser.fireColor;

            Transform t = beamRenderer.transform;
            t.position = origin;

            // 竖版贴图的 +Y 是射击方向，需要额外转 -90°
            t.rotation = Quaternion.Euler(0f, 0f, spriteIsVertical ? angle - 90f : angle);

            ApplyBeamSize(beamRenderer, length, width);
        }

        return length;
    }

    /// <summary>把光束调整到 length × width 的实际世界尺寸。</summary>
    private void ApplyBeamSize(SpriteRenderer beamRenderer, float length, float width)
    {
        Vector2 target = spriteIsVertical
            ? new Vector2(width, length)
            : new Vector2(length, width);

        if (tileTexture)
        {
            beamRenderer.size = target;
            return;
        }

        // Simple 模式下 size 不生效，改用缩放。除以贴图原生世界尺寸得到放大倍数。
        Sprite sprite = beamRenderer.sprite;
        if (sprite == null) return;

        Vector2 native = sprite.bounds.size;
        if (native.x <= 0.0001f || native.y <= 0.0001f) return;

        beamRenderer.transform.localScale = new Vector3(
            target.x / native.x,
            target.y / native.y,
            1f);
    }

    private void HideBeam()
    {
        beamActive = false;

        // 先全部回收，再清空列表。
        // 原写法在 foreach 里边遍历边 Remove，激光放到一半被打断时会抛 InvalidOperationException。
        for (int i = 0; i < currentActiveLasers.Count; i++)
        {
            if (currentActiveLasers[i] != null)
                ObjectPoolManager.Instance.Recycle(currentActiveLasers[i]);
        }
        currentActiveLasers.Clear();
    }

    // ==========================================================
    //  辅助
    // ==========================================================

    private Vector2 OriginPos => laserOrigin != null ? (Vector2)laserOrigin.position : (Vector2)transform.position;

    /// <summary>
    /// 追踪一帧：把当前方向转向"此刻对准玩家的方向"。
    /// trackTurnSpeed 为 0 时直接对准；否则每秒最多转这么多度。
    /// </summary>
    private Vector2 TrackDirection(Vector2 current, BossContext ctx, LaserNode laser)
    {
        Vector2 target = ResolveDirection(ctx, OriginPos, laser.aimAngleOffset);
        if (laser.trackTurnSpeed <= 0f) return target;

        float maxRadians = laser.trackTurnSpeed * Mathf.Deg2Rad * Time.deltaTime;
        Vector3 turned = Vector3.RotateTowards(current, target, maxRadians, 0f);
        return ((Vector2)turned).normalized;
    }

    private Vector2 ResolveDirection(BossContext ctx, Vector2 origin, float angleOffset)
    {
        Vector2 dir = Vector2.left;

        if (ctx.player != null)
        {
            // 【改动】瞄 Hurtbox_Core 而不是 player.position。
            // 玩家轴心点在脚底，直接瞄 transform 会让激光打在地面上。
            Vector2 delta = ctx.PlayerAimPoint - origin;
            if (delta.sqrMagnitude > 0.0001f) dir = delta.normalized;
        }

        if (Mathf.Abs(angleOffset) > 0.01f)
            dir = (Vector2)(Quaternion.Euler(0, 0, angleOffset) * dir);

        return dir;
    }

    private void PlayAnim(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;
        animator.Play(stateName);
    }

    private void OnDrawGizmos()
    {
        if (!showHitbox || !Application.isPlaying || !beamActive) return;

        Gizmos.color = new Color(1f, 0.1f, 0.1f, 1f);

        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(gizmoCenter, Quaternion.Euler(0, 0, gizmoAngle), Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, gizmoSize);
        Gizmos.matrix = old;
    }
}