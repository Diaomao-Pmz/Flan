using UnityEngine;

/// <summary>占位方块此刻处在挥击的哪一步（由 MeleeSwingRunner 告诉 BossMeleeVisual）</summary>
public enum MeleeSwingPose
{
    /// <summary>前摇：从判定框位置举起 / 往后拉，progress 0→1</summary>
    Windup,

    /// <summary>蓄势：举到最高（拉到最远）停着不动</summary>
    Hold,

    /// <summary>攻击：挥向判定框，progress 0→1（1 = 到达）</summary>
    Swing,

    /// <summary>已到达判定框：判定中 / 后摇，停着不动</summary>
    Arrived,
}

/// <summary>
/// 近战占位视觉。**它显示的就是判定框本身** —— 位置和尺寸直接取自 MeleeSwing，
/// 而不是另外做一个「看起来像手」的东西。
///
/// 这样做的三个理由：
/// 1. 玩家看到的就是会打到他的范围，占位阶段的可读性比半吊子美术更好。
/// 2. 免费的调试工具 —— 不用切 Scene 视图看 Gizmos，Game 视图里直接可见。
/// 3. 换真美术时替换的是本组件，判定逻辑一行不用动。
///
/// 【完整时序】（由 MeleeSwingRunner 每帧调用 Pose 摆出来）
///   前摇 windupTime   手出现在判定位置，逐渐**升高**（举起来蓄力）
///                     水平挥击：改为逐渐**往 Boss 身后拉**（把锤子往后抡）
///   蓄势 holdTime     举在最高处（拉在最远处）**不动**，随机憋一会儿（快慢刀）
///   攻击 attackTime   从举起 / 后拉的位置**挥向**判定框，在判定打开的那一刻正好到达，尺寸放大到 hitboxSize
///   后摇 recoverTime  停在判定位置**不动**（砸完了还没抬起来）
///   结束              消失
/// 颜色只看判定：判定生效（Hitbox Active Range 里）= 红色，其余时间 = 半透明灰。
///
/// 【时长全部来自 SO，本组件不持有任何时长参数】
/// 与「用参数代替动画时长」的项目原则一致：以后美术给了真动画，
/// 是改动画去对齐参数，而不是反过来重调手感。
///
/// 【轴心】方块 Sprite 的轴心应为 Bottom Center：传进来的位置是判定框的【底边中点】，
/// 这样落下时方块正好停在地面上，而不是砸进地里。
///
/// 挂载建议：放在 **不会左右翻转** 的节点下（Boss 根节点或场景根节点）。
/// 若挂在 localScale.x = -1 的物体下，位置会被镜像。
/// </summary>
public class BossMeleeVisual : MonoBehaviour
{
    [Header("--- 渲染 ---")]
    [Tooltip("绘制方块的 SpriteRenderer。留空则自动在本物体上找。")]
    [SerializeField] private SpriteRenderer blockRenderer;

    [Tooltip("尺寸微调倍率。正常情况保持 1。\n" +
             "若贴图四周有透明边距导致方块看起来偏小，可在此补偿。")]
    [SerializeField] private Vector2 sizeMultiplier = Vector2.one;

    [Header("--- 前摇：举起 ---")]
    [Tooltip("举到最高时，比判定框高多少")]
    [SerializeField] private float raiseHeight = 2.5f;

    [Tooltip("水平挥击：拉到最远时，离判定框多远（往 Boss 身后的方向）")]
    [SerializeField] private float pullBackDistance = 2.5f;

    [Tooltip("举起阶段的尺寸倍率。略小于判定框，落下时的放大感更明显。")]
    [SerializeField] private float windupScale = 0.7f;

    [Tooltip("举起 / 后拉的速度曲线。左端=刚出现，右端=举到最高（拉到最远）。\n" +
             "EaseOut（先快后慢）更像蓄力，EaseInOut 更平缓。")]
    [SerializeField]
    private AnimationCurve raiseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("--- 攻击：挥过去 ---")]
    [Tooltip("挥过去的速度曲线。左端=从举起 / 后拉的位置出发，右端=到达判定框。\n" +
             "默认先慢后快，像锤子越挥越快砸下去。")]
    [SerializeField]
    private AnimationCurve swingCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 0f));

    [Header("--- 颜色 ---")]
    [Tooltip("判定没生效时的颜色（前摇、挥动中、判定结束之后）")]
    [SerializeField] private Color inactiveColor = new Color(0.55f, 0.55f, 0.55f, 0.45f);

    [Tooltip("判定生效时的颜色（Hitbox Active Range 里）")]
    [SerializeField] private Color activeColor = new Color(0.95f, 0.15f, 0.15f, 0.85f);

    // scale 为 1 时的实际渲染尺寸（世界单位），Awake 时测一次
    private Vector2 unitSize = Vector2.one;

    private void Awake()
    {
        if (blockRenderer == null) blockRenderer = GetComponent<SpriteRenderer>();

        MeasureUnitSize();
        Hide();
    }

    /// <summary>
    /// 测量 scale=1 时方块实际占多大。
    ///
    /// 【为什么不用 sprite.bounds】
    /// Unity 内置的 Square 等形状贴图四周带透明边距，白色区域并没有填满整张图。
    /// sprite.bounds 算的是整张 Sprite（含透明部分），据此换算出的 scale 会偏小，
    /// 表现为「方块比判定框小一圈」。
    /// renderer.bounds 反映的是实际渲染出来的范围，更接近眼睛看到的东西。
    /// </summary>
    private void MeasureUnitSize()
    {
        if (blockRenderer == null || blockRenderer.sprite == null) return;

        Vector3 originalScale = transform.localScale;
        bool wasEnabled = blockRenderer.enabled;

        transform.localScale = Vector3.one;
        blockRenderer.enabled = true;

        Vector3 measured = blockRenderer.bounds.size;

        transform.localScale = originalScale;
        blockRenderer.enabled = wasEnabled;

        if (measured.x > 0.0001f && measured.y > 0.0001f)
        {
            unitSize = measured;
        }
        else
        {
            // 兜底：renderer 尚未初始化时退回 sprite.bounds
            Vector2 sb = blockRenderer.sprite.bounds.size;
            if (sb.x > 0.0001f && sb.y > 0.0001f) unitSize = sb;
        }
    }

    /// <summary>
    /// 摆一帧（MeleeSwingRunner 每帧调用）。
    /// </summary>
    /// <param name="hitboxAnchor">判定框底边中点（世界坐标）</param>
    /// <param name="hitboxSize">判定框尺寸</param>
    /// <param name="pose">此刻处在哪一步</param>
    /// <param name="progress">这一步走了多少（0→1）。前摇 / 攻击用，蓄势 / 已到达不看</param>
    /// <param name="direction">挥击方向，来自 MeleeSwing</param>
    /// <param name="facing">Boss 这一刀的朝向（+1 右 / -1 左）。水平挥击往它的反方向拉</param>
    /// <param name="hitboxLive">此刻判定是否生效（红 / 灰）</param>
    public void Pose(
        Vector2 hitboxAnchor, Vector2 hitboxSize, MeleeSwingPose pose, float progress,
        MeleeSwingDirection direction, float facing, bool hitboxLive)
    {
        if (blockRenderer == null) return;

        blockRenderer.enabled = true;
        blockRenderer.color = hitboxLive ? activeColor : inactiveColor;

        // 举到最高 / 拉到最远时，相对判定框的偏移
        Vector2 held = direction == MeleeSwingDirection.Horizontal
            ? new Vector2((facing < 0f ? 1f : -1f) * pullBackDistance, 0f)
            : new Vector2(0f, raiseHeight);

        progress = Mathf.Clamp01(progress);

        Vector2 offset;
        float scale;

        switch (pose)
        {
            case MeleeSwingPose.Windup:
                offset = held * raiseCurve.Evaluate(progress);
                scale = windupScale;
                break;

            case MeleeSwingPose.Hold:
                offset = held;
                scale = windupScale;
                break;

            case MeleeSwingPose.Swing:
            {
                float p = swingCurve.Evaluate(progress);
                offset = held * (1f - p);
                scale = Mathf.Lerp(windupScale, 1f, p);
                break;
            }

            default:   // Arrived：停在判定框上（判定中 + 之后的后摇）
                offset = Vector2.zero;
                scale = 1f;
                break;
        }

        transform.position = hitboxAnchor + offset;
        ApplySize(hitboxSize * scale);
    }

    /// <summary>收起。后摇结束时、以及被打断时调用。</summary>
    public void Hide()
    {
        if (blockRenderer != null) blockRenderer.enabled = false;
    }

    /// <summary>把方块缩放到指定的世界尺寸。</summary>
    private void ApplySize(Vector2 worldSize)
    {
        if (unitSize.x <= 0.0001f || unitSize.y <= 0.0001f) return;

        transform.localScale = new Vector3(
            worldSize.x * sizeMultiplier.x / unitSize.x,
            worldSize.y * sizeMultiplier.y / unitSize.y,
            1f);
    }

#if UNITY_EDITOR
    /// <summary>在 Scene 视图画出方块当前的实际范围，方便和判定框 Gizmos 对齐。</summary>
    private void OnDrawGizmosSelected()
    {
        if (blockRenderer == null) blockRenderer = GetComponent<SpriteRenderer>();
        if (blockRenderer == null) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(blockRenderer.bounds.center, blockRenderer.bounds.size);
    }
#endif
}
