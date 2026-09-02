using UnityEngine;

/// <summary>
/// 近战占位视觉。**它显示的就是判定框本身** —— 位置和尺寸直接取自 MeleeSwing，
/// 而不是另外做一个「看起来像手」的东西。
///
/// 这样做的三个理由：
/// 1. 玩家看到的就是会打到他的范围，占位阶段的可读性比半吊子美术更好。
/// 2. 免费的调试工具 —— 不用切 Scene 视图看 Gizmos，Game 视图里直接可见。
/// 3. 换真美术时替换的是本组件，判定逻辑一行不用动。
///
/// 【完整时序】
///   前摇 windupTime    手出现在判定位置，逐渐**升高**（举起来蓄力）
///   判定 hitboxDuration 瞬间**落回**判定框，尺寸=hitboxSize，颜色转实
///   后摇 recoverTime   停在判定位置**不动**（砸完了还没抬起来）
///   结束               消失
///
/// 【时长全部来自 SO，本组件不持有任何时长参数】
/// 与「用参数代替动画时长」的项目原则一致：以后美术给了真动画，
/// 是改动画去对齐参数，而不是反过来重调手感。
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

    [Tooltip("举起阶段的尺寸倍率。略小于判定框，落下时的放大感更明显。")]
    [SerializeField] private float windupScale = 0.7f;

    [Tooltip("举起阶段的颜色。建议半透明，表示「还没生效」。")]
    [SerializeField] private Color windupColor = new Color(0.1f, 0.1f, 0.1f, 0.45f);

    [Tooltip("举起的速度曲线。左端=刚出现，右端=举到最高。\n" +
             "EaseOut（先快后慢）更像蓄力，EaseInOut 更平缓。")]
    [SerializeField]
    private AnimationCurve raiseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("--- 判定 / 后摇：落下 ---")]
    [Tooltip("判定生效及后摇期间的颜色。建议不透明，和举起阶段明确区分。")]
    [SerializeField] private Color strikeColor = new Color(0.05f, 0.05f, 0.05f, 1f);

    private bool active;

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
    /// 前摇：手出现在判定位置，逐渐举高。
    /// duration 来自 swing.windupTime —— 由本协程 yield 掉这段时间，
    /// 因此视觉与判定时序天然一致，不会出现「举到一半判定就来了」。
    /// </summary>
    public System.Collections.IEnumerator Windup(Vector2 hitboxCenter, Vector2 hitboxSize, float duration)
    {
        if (blockRenderer == null) yield break;

        active = true;
        blockRenderer.enabled = true;
        blockRenderer.color = windupColor;

        ApplySize(hitboxSize * windupScale);

        // duration 为 0 时也要摆一帧，否则瞬发挥击完全看不到举手
        float elapsed = 0f;
        do
        {
            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            float eased = raiseCurve.Evaluate(t);

            // 从判定框位置往上升，升到 raiseHeight
            transform.position = hitboxCenter + new Vector2(0f, raiseHeight * eased);

            if (duration <= 0f) break;

            yield return null;
            elapsed += Time.deltaTime;

        } while (elapsed < duration);
    }

    /// <summary>
    /// 判定生效瞬间：从高处落回判定框，贴合真实的位置与尺寸。
    /// 后摇期间保持这个状态不动，直到 Hide() 被调用。
    /// </summary>
    public void Strike(Vector2 hitboxCenter, Vector2 hitboxSize)
    {
        if (blockRenderer == null) return;

        active = true;
        blockRenderer.enabled = true;
        blockRenderer.color = strikeColor;

        transform.position = hitboxCenter;
        ApplySize(hitboxSize);
    }

    /// <summary>判定窗口内每帧跟随（支持边移动边挥砍）。</summary>
    public void Follow(Vector2 hitboxCenter)
    {
        if (!active || blockRenderer == null) return;
        transform.position = hitboxCenter;
    }

    /// <summary>收起。后摇结束时、以及被打断时调用。</summary>
    public void Hide()
    {
        active = false;
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
