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

        [Header("Crouch Settings")]
        [Tooltip("蹲行移动速度 = moveSpeed * 本倍率（原先在 CrouchState 里硬编码为 0.5）")]
        public float crouchMoveSpeedMultiplier = 0.5f;
        [Tooltip("蹲下时碰撞体高度压缩比例（原先在 SetColliderHeight 里硬编码为 0.6）")]
        public float crouchColliderHeightMultiplier = 0.6f;

        [Header("Fly Settings")]
        [Tooltip("长按飞行键多少秒后进入飞行状态")]
        public float hoverChargeTime = 0.7f;
        public float flyManaCostPerSecond = 5f;
        [Tooltip("主动取消飞行时给予的向上冲力")]
        public float flyCancelJumpForce = 8f;
        public float flySpeed = 5f;

        [Header("Charge Settings")]
        [Tooltip(
            "蓄力期间的移动速度倍率。\n" +
            "0.2 = 只有平时的两成，1 = 蓄力不影响移动速度。\n" +
            "原先硬编码在 ChargeState 里，现在挪出来方便调手感。")]
        public float chargeMoveSpeedMultiplier = 0.2f;

        // ==========================================================
        // 弱化蓄力僵直 · 预付逃逸冲劲
        //
        // 【为什么填的是「距离」而不是「初速度」】
        //
        // 原先这里给的是 escapeDashSpeed / escapeSlideSpeed（初速度），
        // 而实际位移是三个参数乘出来的副产品：
        //     总距离 = 初速度 × 总时长 × 指数/(指数+1)
        //
        // 于是「想挪多远」这个唯一的设计意图，被摊进了三个旋钮里 ——
        // 想把 6.4 格调成 1.4 格得先解方程，而且调完衰减指数（那是手感）
        // 距离又跟着变了。两件各管各的事被焊在了一起。
        //
        // 这和 ComboNode.aimDashBaseDistance 曾经踩的坑是同一类：
        // 当时是「方向」和「距离」混在一个数里被 Mathf.Max 吃掉负号，
        // 这次是「走多远」和「怎么走」互相牵制。
        //
        // 现在拆开：填距离，初速度由 ChargeStunState 反推 ——
        //     初速度 = 距离 × (指数+1) / (指数 × 总时长)
        //
        // 三个旋钮从此各管一件事：
        //   距离 → 挪多远（所见即所得）
        //   时长 → 这一下多快做完
        //   指数 → 飘着出去还是一顿就停
        // 改任何一个都不会动到另外两个。
        //
        // 做法与 ComboState.SetupAimDash 一致（那边也是填距离、速度反推），
        // 只是那边是线性衰减、这边是指数曲线，积分公式不同而已。
        // ==========================================================
        // ---- 冲刺兑换（Shift）----
        //
        // 【定位：空中动作】整段位移期间重力是关掉的（和 DashState 同一个做法），
        // 所以在空中按 Shift 是「先平着冲出去，冲完再垂直掉下来」，
        // 而不是一边掉一边飘。冲刺本来就是这个游戏里唯一的空中位移手段。
        [Header("弱化蓄力僵直 · 逃逸冲劲 —— 冲刺 (Shift)")]
        [Tooltip(
            "按【冲刺】兑换到的位移距离（单位：格）。\n\n" +
            "⚠️ 这里填的是【距离】不是速度 —— 填 1.4 就是挪 1.4 格，所见即所得。\n" +
            "改下面的时长或衰减指数都不会改变这个距离。\n\n" +
            "定位是「预判式的紧急避险」：够躲一次擦边判定，但明显换不来一次真正的脱离。\n" +
            "所以刻意配得比普通冲刺短 —— 普通冲刺是 dashSpeed × dashDuration = 2.4 格。")]
        public float escapeDashDistance = 1.4f;

        [Tooltip(
            "这一下冲劲从满速衰减到 0 要花多久（秒）。\n\n" +
            "⚠️ 改这个【不会】改变位移距离，只改变「这一窜有多急」。\n" +
            "填小 = 又快又短促；填大 = 同样的距离拖得更久，看起来更飘。\n\n" +
            "建议远小于僵直时长：大rt 本身有 1 秒，冲劲若占掉大半，\n" +
            "观感会变成「一边罚站一边慢慢往旁边溜」，避险和惩罚糊在一起。")]
        public float escapeDashDuration = 0.25f;

        [Tooltip(
            "冲刺逃逸的衰减曲线形状。速度 = 初速度 × (1 - (已过时间/总时长) ^ 本值)\n\n" +
            "  1   = 匀减速（直线）—— 全程都在掉速\n" +
            "  2   = 开口向下抛物线右半段：起步几乎【不】掉速，到后面才急刹\n" +
            "  3~4 = 更极端的「先飘一段、然后急刹」\n" +
            "  0.5 = 反过来，起步猛掉、尾巴拖很长\n\n" +
            "⚠️ 改本值【不会】改变位移距离 —— 初速度会自动反推，总距离恒等于上面那个数。")]
        [Range(0.2f, 5f)]
        public float escapeDashDecayExponent = 1f;

        // ---- 滑铲兑换（Ctrl）----
        //
        // 【定位：地面动作】和真滑铲一样，空中放不出来 ——
        // 所以在空中按 Ctrl 会先攒着、垂直坠落，脚一沾地才弹射起步。
        // 这条规则和路由器里「空中按 Ctrl 转去空中指令」是同一个道理。
        [Header("弱化蓄力僵直 · 逃逸冲劲 —— 滑铲 (Ctrl)")]
        [Tooltip(
            "按【滑铲】兑换到的位移距离（格）。一般比冲刺短一些。\n\n" +
            "与冲刺同理，填的是距离，下面两个参数不会影响它。")]
        public float escapeSlideDistance = 0.9f;

        [Tooltip("滑铲逃逸的冲劲持续多久（秒）。只改「有多急」，不改距离")]
        public float escapeSlideDuration = 0.25f;

        [Tooltip(
            "滑铲逃逸的衰减曲线形状。含义与冲刺那一栏完全相同。\n\n" +
            "【为什么和冲刺分开配】两者的定位不一样：\n" +
            "冲刺是空中脱离，要的是干脆；滑铲是贴地溜走，还要接一个蹲姿收尾。\n" +
            "共用一条曲线的话，调好了一个另一个必然被带歪。")]
        [Range(0.2f, 5f)]
        public float escapeSlideDecayExponent = 1f;

        [Header("Fly 衍生 (打出蓄力招后点按 Q 跃起进飞行)")]
        [Tooltip("跃起阶段的速度")]
        public float flyLeapSpeed = 14f;

        [Tooltip("跃起阶段持续多久，之后转为正常飞行操控")]
        public float flyLeapDuration = 0.25f;

        [Tooltip("没有按方向键时，跃起默认朝哪个方向。(0,1)=正上方")]
        public Vector2 flyLeapDefaultDirection = new Vector2(0f, 1f);

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