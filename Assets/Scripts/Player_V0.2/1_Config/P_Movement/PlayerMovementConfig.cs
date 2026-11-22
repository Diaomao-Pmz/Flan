using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【位移出厂说明书】
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerMovementConfig", menuName = "Flandre/Player/Movement Config")]
    public class PlayerMovementConfig : ScriptableObject
    {
        [Header("Run Settings")]
        [Tooltip("基础跑动速度。注意：这只是「基础值」，最终生效值请读 PlayerStats.moveSpeed.Value")]
        public float moveSpeed = 6f;

        [Header("Fly Settings (按 Alt 起飞，再按一次解除；飞行中 Shift 八向冲刺，Ctrl 短暂不可选中)")]
        // 飞行耗蓝已搬到 PlayerStateConfig（资源类数值统一放那里）
        [Tooltip("飞行时的八方向移动速度")]
        public float flySpeed = 5f;

        [Header("Jump Settings")]
        [Tooltip("基础最大跳跃次数。目前仍会被 GemActionProcessor 在运行时覆写，阶段2 修复")]
        public int maxJumps = 1;

        // 【2026-11-21】跳跃改成填高度：起跳给满跳速度；松手时还没升到最低高度 → 补足到正好最低高度，
        // 已经过了 → 削速度、很快到顶（按得越久跳得越高）。起跳速度由满跳高度和重力自动算出。
        [Tooltip("满跳高度（格）：一直按住空格能跳多高。起跳速度由它和重力自动算出（重力 = Physics2D 重力 × 角色的 Gravity Scale）")]
        [Min(0.1f)]
        public float maxJumpHeight = 2.87f;

        [Tooltip(
            "最低跳跃高度（格）：轻点一下空格跳多高。\n" +
            "在升到这个高度之前松手，都会正好停在这个高度（和手多快无关）；过了这个高度再松手，按得越久跳得越高。\n" +
            "比满跳高度还大时按满跳高度算")]
        [Min(0f)]
        public float minJumpHeight = 1f;

        [Tooltip("松手后剩余上升速度：已经升过最低高度后松手，上升速度被削到这个值。越小停得越利落，0 = 松手立刻开始下落")]
        [Min(0f)]
        public float minJumpVelocity = 2f;

        [Tooltip("下落重力倍率：往下掉时重力乘以这个数（跳跃下落段和普通下落都算）。1 = 和上升一样；大于 1 = 落得更快，跳起来不飘")]
        [Min(0.1f)]
        public float fallGravityMultiplier = 1f;

        [Tooltip("跳跃力属性的基准值。现在只用来算蓄力折损 / 加成的比例（属性值 ÷ 基准值），跳多高看上面的高度")]
        [HideInInspector]
        public float jumpForce = 13f;

        [Tooltip(
            "空中按 Ctrl 之后的下落加速倍率（普通状态）。\n" +
            "1 = 不加速，和平常下落一样；2 = 下落速度是平常的两倍。\n" +
            "实现方式是这段下落期间把重力乘以本倍率，落地 / 离开下落即恢复。")]
        [Min(0f)]
        public float ctrlDropSpeedMultiplier = 1f;

        // ==========================================================
        // 【正式版整改 阶段4】冲刺 / 滑铲的参数口径：填【距离 + 时长】，速度由它们反推
        // 症状：试验版填「速度 + 距离」，想让动作"走多远、用多久"得自己换算
        // 根因：策划 / 动画关心的是位移和时长（要和动画长度对齐），速度只是结果
        // ==========================================================
        [Header("Dash Settings (地面 / 空中 · 八方向)")]
        [Tooltip("冲刺距离")]
        [Min(0.01f)]
        public float dashDistance = 1.2f;

        [Tooltip("冲刺时长（秒）。冲刺速度 = 距离 / 时长。有冲刺速度加成时，实际用时会按比例缩短")]
        [Min(0.01f)]
        public float dashDuration = 0.15f;

        /// <summary>冲刺速度的基础值（未套加成）= 距离 / 时长</summary>
        public float DashBaseSpeed => dashDistance / Mathf.Max(0.01f, dashDuration);

        [Tooltip(
            "冲刺冷却（秒）。会吃 PlayerStats 的冷却缩减。\n" +
            "【注意】填在这里的值会覆盖 PlayerStateMachine 上 Dash Skill 的 Total CD；\n" +
            "连段数、连按间隔、派生窗口仍在 PlayerStateMachine 上配。")]
        [Min(0f)]
        public float dashCooldown = 1f;

        [Header("Slide Settings (地面滑铲)")]
        [Tooltip("滑铲距离：整段滑铲一共滑多远")]
        [Min(0.01f)]
        public float slideDistance = 3f;

        [Tooltip("滑铲时长（秒）：滑满这么久后转入蹲下。初速度由距离和时长反推")]
        [Min(0.01f)]
        public float slideDuration = 0.59f;

        [Tooltip(
            "滑铲结束、转入蹲下那一刻的速度 = moveSpeed * 本倍率。\n" +
            "滑铲从初速度匀减速到这个速度。调大 = 全程更匀速；调小 = 开头更猛、结尾更慢。\n" +
            "如果「距离 / 时长」比这个速度还慢，滑铲会改成全程匀速。")]
        public float slideToCrouchSpeedMultiplier = 0.3f;

        [Tooltip("滑铲全程无敌（蹲下不无敌）")]
        public bool slideInvulnerable = true;

        [Tooltip(
            "【空中 Ctrl 下落 → 落地接滑铲】的方向键窗口（秒）。\n" +
            "空中按 Ctrl 下落后，落地前这么多秒内按过左右方向键 → 落地直接接滑铲；没按 → 落地蹲下。\n" +
            "这次滑铲不看也不消耗滑铲冷却：下落时冷却是好的（下落无敌）→ 滑铲也全程无敌；\n" +
            "下落时冷却没好（普通下落）→ 滑铲照常滑，但不无敌。")]
        [Min(0f)]
        public float landingSlideInputWindow = 0.1f;

        [Tooltip(
            "滑铲冷却（秒）。会吃 PlayerStats 的冷却缩减。\n" +
            "【注意】填在这里的值会覆盖 PlayerStateMachine 上 Slide Skill 的 Total CD。")]
        [Min(0f)]
        public float slideCooldown = 2f;

        [Header("Air Slide (普通状态在空中按 Ctrl：收掉上升速度往下落)")]
        [Tooltip("空中按 Ctrl 之后的下落全程无敌（落地即解除）。还要滑铲冷却已好才会生效")]
        [UnityEngine.Serialization.FormerlySerializedAs("flightDropInvulnerable")]
        public bool airDropInvulnerable = true;

        [Header("Flight Dash / Slide (飞行中的 Shift / Ctrl)")]
        [Tooltip(
            "勾上（默认）= 飞行中的冲刺 / Ctrl 直接用上面普通状态的参数（距离、时长、冷却），下面四项不生效。\n" +
            "取消勾选 = 两种状态的参数分开，飞行中用下面这四项。")]
        public bool flightUsesNormalActionParams = true;

        [Tooltip("【仅在取消勾选上面那项时生效】飞行冲刺距离")]
        [Min(0.01f)]
        public float flyDashDistance = 1.5f;

        [Tooltip("【仅在取消勾选时生效】飞行冲刺时长（秒）。速度 = 距离 / 时长，冲刺速度加成按比例套用")]
        [Min(0.01f)]
        public float flyDashDuration = 0.1f;

        /// <summary>飞行冲刺速度的基础值（未套加成）= 距离 / 时长</summary>
        public float FlyDashBaseSpeed => flyDashDistance / Mathf.Max(0.01f, flyDashDuration);

        [Tooltip("【仅在取消勾选时生效】飞行冲刺冷却（秒）")]
        [Min(0f)]
        public float flyDashCooldown = 1f;

        [Tooltip("【仅在取消勾选时生效】飞行中按 Ctrl 的冷却（秒）。勾选时用 Slide Cooldown")]
        [Min(0f)]
        public float flySlideCooldown = 2f;

        [Tooltip(
            "飞行中按 Ctrl：变为不可选中状态持续这么多秒（人继续飞，不会掉下去）。\n" +
            "这一项只有飞行状态有，不受上面的勾选框影响。")]
        [Min(0f)]
        public float flySlideUntargetableDuration = 0.5f;

        [Header("Pass Through (冲刺 / 滑铲穿过敌人，与无敌无关)")]
        [Tooltip("冲刺 / 滑铲期间可以穿过的图层。勾上 Boss、敌人【本体】所在的层。\n" +
                 "不要勾地面、墙（会穿墙掉出地图），也不要勾敌方子弹的层（子弹会直接穿过玩家）。\n" +
                 "选 Nothing = 关闭此功能")]
        public LayerMask passThroughLayers;
        [Tooltip("Shift 冲刺（地面、空中、飞行八向）期间可以穿过敌人")]
        public bool dashPassThrough = true;
        [Tooltip("地面滑铲期间可以穿过敌人（滑铲结束转入蹲下后不再穿过）")]
        public bool slidePassThrough = true;

        [Header("位移终点判定 (冲刺 / 飞行冲刺 / 滑铲 / 招式位移不落进 Boss 身体)")]
        [Tooltip("勾上 = 位移期间每个物理帧检查：按剩下的路走完，会不会停在 Boss 身体里。\n" +
                 "会的话：已穿过一定比例且这个动作能穿身 → 送到 Boss 身后；否则停在 Boss 面前（两个碰撞箱边贴边）。\n" +
                 "Boss = 上面 Pass Through Layers 里那些图层（没配图层时本功能不生效）。")]
        public bool endpointGuard = true;

        [Tooltip("穿过比例：终点要穿进 Boss 身体多深才算「穿过去了」（0.5 = 过了 Boss 中线）。\n" +
                 "调高 = 更难穿过去（更常停在面前）；调低 = 更容易穿过去。只对能穿身的动作有意义")]
        [Range(0f, 1f)]
        public float endpointPassRatio = 0.5f;

        [Tooltip("重叠容差（格）：终点和 Boss 只重叠这么浅时当作没撞上，照常走完")]
        [Min(0f)]
        public float endpointOverlapTolerance = 0.1f;

        [Header("Crouch Settings")]
        [Tooltip("蹲行移动速度 = moveSpeed * 本倍率（原先在 CrouchState 里硬编码为 0.5）")]
        public float crouchMoveSpeedMultiplier = 0.5f;
        [Tooltip("蹲下时碰撞体高度压缩比例（原先在 SetColliderHeight 里硬编码为 0.6）")]
        public float crouchColliderHeightMultiplier = 0.6f;

        [Header("Sensor Settings (原先是散落在代码里的魔法数字)")]
        [Tooltip("地面检测盒尺寸，原 IsGrounded() 中硬编码 (0.5, 0.2)")]
        public Vector2 groundCheckBoxSize = new Vector2(0.5f, 0.2f);
        [Tooltip("地面检测射线距离，原 IsGrounded() 中硬编码 0.1")]
        public float groundCheckDistance = 0.1f;
        [Tooltip("头顶检测盒尺寸，原 CanStand() 中硬编码 (0.4, 0.1)")]
        public Vector2 ceilingCheckBoxSize = new Vector2(0.4f, 0.1f);
        [Tooltip("头顶检测射线距离，原 CanStand() 中硬编码 0.1")]
        public float ceilingCheckDistance = 0.1f;
    }
}