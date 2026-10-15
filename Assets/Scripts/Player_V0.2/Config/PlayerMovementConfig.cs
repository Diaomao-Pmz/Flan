using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【位移出厂说明书】
    ///
    /// 铁律：运行时只读！任何代码都不许往这上面写值。
    /// 想在运行时改数值 —— 去 PlayerStats 贴一张 StatModifier「便利贴」，
    /// 而不是涂改这本说明书。
    ///
    /// 为什么？因为 ScriptableObject 是磁盘上的资产。
    /// 运行时往它字段上写值，在编辑器里会污染资产文件（退出 Play Mode 后残留），
    /// 在打包后会被所有实例共享。这正是 readme 里「警惕易失性数据与状态残留」的另一种死法。
    ///
    /// 所有默认值 = 2026-08 时 PlayerStateMachine Inspector 上的实际值。
    /// 右键 Create 出来的资产开箱即用，不需要手抄。
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerMovementConfig", menuName = "Flandre/Player/Movement Config")]
    public class PlayerMovementConfig : ScriptableObject
    {
        [Header("Run Settings")]
        [Tooltip("基础跑动速度。注意：这只是「基础值」，最终生效值请读 PlayerStats.moveSpeed.Value")]
        public float moveSpeed = 6f;

        [Header("Jump Settings")]
        [Tooltip("基础最大跳跃次数。目前仍会被 GemActionProcessor 在运行时覆写，阶段2 修复")]
        public int maxJumps = 1;
        public float jumpForce = 13f;
        [Tooltip("松开跳跃键时，向上速度被削减到的值（实现「按多久跳多高」）")]
        public float minJumpVelocity = 2f;

        [Header("Dash Settings")]
        public float dashSpeed = 12f;
        public float dashDuration = 0.2f;

        [Header("Slide Settings")]
        [Tooltip("滑铲初速度 = moveSpeed * 本倍率")]
        public float slideStartSpeedMultiplier = 1.2f;
        [Tooltip("滑铲减速率，数值越大减速越快")]
        public float slideDeceleration = 8f;
        [Tooltip("滑铲速度衰减到 moveSpeed * 本倍率 时，自动转入蹲下")]
        public float slideToCrouchSpeedMultiplier = 0.3f;
        [Tooltip("滑铲全程无敌（蹲下不无敌）")]
        public bool slideInvulnerable = true;

        [Header("Pass Through (冲刺 / 滑铲穿过敌人，与无敌无关)")]
        [Tooltip("冲刺 / 滑铲期间可以穿过的图层。勾上 Boss、敌人【本体】所在的层。\n" +
                 "不要勾地面、墙（会穿墙掉出地图），也不要勾敌方子弹的层（子弹会直接穿过玩家）。\n" +
                 "选 Nothing = 关闭此功能")]
        public LayerMask passThroughLayers;
        [Tooltip("Shift 冲刺（地面、空中、飞行八向）期间可以穿过敌人")]
        public bool dashPassThrough = true;
        [Tooltip("地面滑铲期间可以穿过敌人（滑铲结束转入蹲下后不再穿过）")]
        public bool slidePassThrough = true;

        [Header("Crouch Settings")]
        [Tooltip("蹲行移动速度 = moveSpeed * 本倍率（原先在 CrouchState 里硬编码为 0.5）")]
        public float crouchMoveSpeedMultiplier = 0.5f;
        [Tooltip("蹲下时碰撞体高度压缩比例（原先在 SetColliderHeight 里硬编码为 0.6）")]
        public float crouchColliderHeightMultiplier = 0.6f;

        [Header("Fly Settings (按 Alt 起飞，再按一次解除；飞行中 Shift 八向冲刺，Ctrl 下坠)")]
        // 飞行耗蓝已搬到 PlayerStateConfig（资源类数值统一放那里）
        [Tooltip("飞行时的八方向移动速度")]
        public float flySpeed = 5f;
        [Tooltip("飞行中按 Ctrl 解除飞行后，下落全程无敌（落地即解除）")]
        public bool flightDropInvulnerable = true;

        [Header("Charge Settings")]
        [Tooltip(
            "蓄力时是否给移动打折（移速、跳跃力分别乘下面两个倍率）。\n\n" +
            "默认关闭：蓄力时正常跑跳。关闭时两个倍率参数保留不动，\n" +
            "以后想恢复折损，或给 boss 的减速 debuff 参考数值，直接打开即可。\n" +
            "「冲刺后接蓄力的速度继承」是配合折损的补偿，也跟着本开关一起开关。")]
        public bool chargeSlowsMovement = false;

        [Tooltip(
            "蓄力期间的移动速度倍率。\n" +
            "0.2 = 只有平时的两成，1 = 蓄力不影响移动速度。\n" +
            "原先硬编码在 ChargeState 里，现在挪出来方便调手感。")]
        public float chargeMoveSpeedMultiplier = 0.2f;

        [Tooltip(
            "蓄力期间的跳跃力度倍率（同样只在 Charge Slows Movement 打开时生效）。\n" +
            "0.7 = 跳得比平时低三成，1 = 蓄力不影响跳跃。")]
        [Range(0.1f, 1f)]
        public float chargeJumpForceMultiplier = 0.7f;

        [Tooltip(
            "【蓄力准备窗口】的地面移速倍率。\n\n" +
            "窗口 = 攻击键已判定为长按（按住满 0.15 秒），但人还在攻击动画 / 后摇里、蓄力还没真正开始。\n" +
            "这段时间本来按「攻击时不能走动」锁住，现在允许以 移速 × 本倍率 移动。\n" +
            "0 = 仍然不能动，1 = 不打折。不按住攻击键时攻击照常锁移动。")]
        [Range(0f, 1f)]
        public float preChargeMoveSpeedMultiplier = 0.5f;

        // 【已删除】弱化蓄力僵直的逃逸冲劲参数（escapeDash* / escapeSlide*），已随连放惩罚一并移除。

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