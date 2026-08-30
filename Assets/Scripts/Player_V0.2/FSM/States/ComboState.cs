using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 连招执行状态。
///
/// ==========================================================
/// 【批次L 新增】动画事件兜底超时
///
/// 攻击的退出【只】依赖动画事件 OnAttackAnimationEnd。
/// 漏配一个事件 = 玩家永久卡在攻击动作里，而且【没有任何报错】——
/// 你只能靠猜是哪个招式、哪个动画出的问题。
///
/// 这次动画重做后 A1/B1 全部卡死，就是这个隐患的实证。
/// 后面还有十几个招式要配事件，漏一次就是一次卡死。
///
/// 比喻：办公室的门锁原本只靠"下班铃响了才开"。
///       现在加了个定时熄灯的总闸 —— 铃坏了人照样能走，
///       而且第二天保安会告诉你哪间屋的铃坏了。
///
/// 这和批次F 修连招指针泄漏是同一个思路：
/// 【动画事件应该是"锦上添花的时机点"，不能是"唯一的生命线"】。
/// 当时修的是连招指针，状态退出这条还留着同样的隐患，现在一并堵上。
/// ==========================================================
/// </summary>
public class ComboState : PlayerStateBase
{
    /// <summary>超时倍率：实际上限 = 动画长度 × 本值</summary>
    private const float TimeoutMultiplier = 1.5f;

    /// <summary>动画剪辑缺失时的兜底上限（秒）</summary>
    private const float FallbackTimeout = 2.0f;

    public bool isCancelable = false;

    private float originalGravity;
    private bool wasAirborneOnEnter;

    // ---- 出招位移 ----
    private Vector2 thrustVelocity;
    private float thrustTimer;
    private float thrustDuration;

    // 碰撞箱微调是否生效中
    private bool isBodyAdjusting;

    /// <summary>
    /// 本招是哪只手打出的。
    ///
    /// 必须在 Enter 时就记下来 —— 因为换手抢拍时，
    /// 新招式的 SetCurrentNode 已经把 Buffer.LastTriggerCmd 改成新的那只手了，
    /// 等到旧招式 Exit 时再去问就问到错的手上，
    /// 后摇会记到刚出招的那只手头上，抢拍直接失效。
    /// </summary>
    private InputCmd ownerCmd;

    /// <summary>本次出招的编号。Exit 时用它核对"我还是不是最新的那一次"</summary>
    private int ownerAttackId;

    /// <summary>
    /// 本招是不是「强行打出」的缩水版蓄力（CD 没走完就放出来的 AA1 / BB1）。
    /// 和 ownerCmd 一样必须在 Enter 时拍快照 —— 招式演到一半被位移打断时
    /// Buffer 会 ResetCombo，Exit 再去问就问不到了。
    /// </summary>
    private bool isWeakenedAttack;

    /// <summary>
    /// 本招用的武器。同样在 Enter 时拍快照 ——
    /// Exit 里要读它的 CD 配置算后摇，而那时 Buffer.ActiveWeapon
    /// 可能已经被 ResetCombo 清成 null（位移打断就会）。
    /// </summary>
    private WeaponMoveSet ownerWeapon;

    // 兜底超时
    private float timeoutLimit;
    private float elapsed;
    private bool hasWarnedTimeout;
    private ComboNode activeNode;

    // 缓存引用，避免每帧 GetComponent
    private ComboInputBuffer buffer;
    private PlayerHitDetection hitDetection;
    private PlayerWeaponEmitter weaponEmitter;
    private PlayerAimProvider aimProvider;
    private PlayerMomentum momentum;
    private WeaponLoadout loadout;

    public ComboState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    private ComboInputBuffer Buffer
        => buffer != null ? buffer : (buffer = sm.GetComponent<ComboInputBuffer>());

    private PlayerHitDetection HitDetection
        => hitDetection != null ? hitDetection : (hitDetection = sm.GetComponent<PlayerHitDetection>());

    private PlayerWeaponEmitter WeaponEmitter
        => weaponEmitter != null ? weaponEmitter : (weaponEmitter = sm.GetComponent<PlayerWeaponEmitter>());

    private PlayerAimProvider AimProvider
        => aimProvider != null ? aimProvider : (aimProvider = sm.GetComponent<PlayerAimProvider>());

    private PlayerMomentum Momentum
        => momentum != null ? momentum : (momentum = sm.GetComponent<PlayerMomentum>());

    private WeaponLoadout Loadout
        => loadout != null ? loadout : (loadout = sm.GetComponent<WeaponLoadout>());

    public override void Enter()
    {
        isCancelable = false;
        originalGravity = sm.defaultGravityScale;   // 读出厂值，不读当前值（见 PlayerStateMachine.defaultGravityScale）
        wasAirborneOnEnter = !sm.IsGrounded();

        isBodyAdjusting = false;
        activeNode = Buffer.currentNode;
        ownerCmd = Buffer.LastTriggerCmd;
        ownerAttackId = Buffer.GetHandAttackId(ownerCmd);
        isWeakenedAttack = Buffer.IsCurrentAttackWeakened;
        ownerWeapon = Buffer.ActiveWeapon;

        // 空中连段反重力悬停：把角色钉在空中，防止挥剑时诡异下滑
        if (wasAirborneOnEnter)
        {
            sm.rb.gravityScale = 0f;
            sm.rb.linearVelocity = Vector2.zero;
        }

        // ---- 兜底超时：按动画长度算上限 ----
        elapsed = 0f;
        hasWarnedTimeout = false;

        float clipLength = (activeNode != null && activeNode.attackClip != null)
            ? activeNode.attackClip.length
            : 0f;

        timeoutLimit = (clipLength > 0f)
            ? clipLength * TimeoutMultiplier
            : FallbackTimeout;

        if (activeNode != null)
        {
            if (string.IsNullOrEmpty(activeNode.animName))
            {
                // 这条以前是静默失败 —— anim.Play("") 什么都不做，角色卡在上一个动画上
                Debug.LogWarning(
                    $"[ComboState] 招式「{activeNode.nodeName}」没有指定 Attack Clip，" +
                    "动画不会播放。请检查 ComboNode 资产。", sm);
            }
            else
            {
                sm.animDriver.SetBase(activeNode.animName, restart: true);
            }

            SetupThrust();
        }
    }

    public override void Update()
    {
        // ---- 兜底超时检查 ----
        elapsed += Time.deltaTime;
        if (elapsed > timeoutLimit)
        {
            WarnAndExitOnTimeout();
            return;
        }

        // 后摇取消检测
        if (isCancelable)
        {
            if (Buffer.TryAdvanceCombo()) return;
        }

        // 【P1b 改动】攻击动画中【不再】响应方向键转向。
        //
        // 原先允许用 a/d 微调朝向，但那会让玩家在挥刀途中来回转身，
        // 判定框跟着左右横跳，看起来像 bug。
        //
        // 新规则：攻击中只有【带方向键的冲刺/滑铲】能改变朝向 ——
        // 想转身就得付出一次位移的代价，不能白嫖。

        // ---- 碰撞箱微调（不打断攻击）----
        TickBodyAdjust();
    }

    public override void FixedUpdate()
    {
        // ---- 携带动量优先：滑铲途中出招时，滑行继续 ----
        //
        // 减速逻辑在 SlideMomentum 里，本状态只是"接手让它继续跑"，
        // 没有复制任何一行滑铲的物理参数。
        // ---- 出招位移优先 ----
        //
        // 【顺序修正】蓄力位移必须排在滑铲动量【之前】。
        // 否则滑铲后接 AA1 时，滑铲的余速会一直接管速度，
        // 蓄力位移永远轮不到执行 —— 而蓄力位移是玩家主动打出的、
        // 有明确方向意图的动作，理应压过被动的残余冲劲。
        if (TickThrust()) return;

        // ---- 携带动量：滑铲途中出招时，滑行继续 ----
        //
        // 减速逻辑在 SlideMomentum 里，本状态只是"接手让它继续跑"，
        // 没有复制任何一行滑铲的物理参数。
        bool wantsMomentum = activeNode == null || activeNode.inheritMomentum;

        if (wantsMomentum && sm.slideMomentum.Tick(sm, Time.fixedDeltaTime))
        {
            return;
        }

        // ---- 冲量结束后锁死移动 ----
        //
        // 【为什么必须主动写 0】
        // 原先地面上 ComboState 从来不写速度 —— 于是进入攻击那一刻的残留速度
        // （比如蓄力架势允许的 0.2 倍微移）会一路保持到攻击结束，
        // 表现出来就像"攻击期间还能走动"。
        //
        // 设计要求是「玩家攻击时默认都不能走动」，所以这里明确按住不放。
        if (sm.IsGrounded())
        {
            if (activeNode == null || activeNode.lockMovementDuringAttack)
            {
                sm.rb.linearVelocity = new Vector2(0f, sm.rb.linearVelocity.y);
            }
            return;
        }

        // 空中连招期间锁死速度，不接受重力叠加
        sm.rb.linearVelocity = Vector2.zero;
        // 注意：位移期间 TickThrust 已经 return 了，走不到这里 ——
        // 所以空中的蓄力位移不会被这句抹掉
    }

    /// <summary>
    /// 【P1b 新增】攻击动画中按住 Ctrl 微调身位。
    ///
    /// 不打断攻击、不影响连段，只是把碰撞箱挪一点，用来躲擦边的攻击。
    ///   地面 → 蹲下，碰撞箱向下（躲头顶）
    ///   空中 → 缩腿，碰撞箱向上（躲脚下）
    ///
    /// 【为什么放在 ComboState 而不是路由器】
    /// 路由器负责"要不要切状态"，而这件事根本不切状态 ——
    /// 它是攻击状态内部的一个持续行为，跟着攻击一起开始、一起结束。
    /// 放路由器里的话，攻击结束时谁来还原碰撞箱就成了问题。
    /// </summary>
    private void TickBodyAdjust()
    {
        if (sm.sensor == null) return;

        // 带方向键的 Ctrl 是滑铲，不走这条路（由路由器处理）
        bool wantsAdjust = sm.playerController.isCrouchHeld
                           && Mathf.Abs(sm.playerController.moveInput.x) <= 0.1f;

        // 【修正】每帧重算目标形态，而不是只在按键状态变化时算一次。
        //
        // 原先是"按下时判断一次地面还是空中"，于是：
        //   按住 Ctrl 在地面攻击 → 应用 Crouch
        //   保持按住跳到空中     → 按键状态没变 → 不重新判断 → 还停在 Crouch
        // 反过来空中按住落地也一样，会停在 TuckUp。
        //
        // 环境是会变的，所以判断也必须跟着每帧走。
        PlayerSensor.ColliderShape desired = PlayerSensor.ColliderShape.Normal;

        if (wantsAdjust)
        {
            desired = sm.IsGrounded()
                ? PlayerSensor.ColliderShape.Crouch
                : PlayerSensor.ColliderShape.TuckUp;
        }

        isBodyAdjusting = wantsAdjust;

        // 只有和当前形态不同才写，避免每帧重复设置碰撞体
        if (sm.sensor.CurrentShape != desired)
        {
            sm.sensor.SetColliderShape(desired);
        }
    }

    /// <summary>出招冲量是否仍在生效</summary>
    private bool IsThrustActive => thrustDuration > 0f && thrustTimer < thrustDuration;

    /// <summary>
    /// 准备本招的位移冲量。
    ///
    /// 【为什么要衰减，而不是设一次速度就不管】
    /// 原先是 rb.linearVelocity = dir * forwardThrust.x 设完就走，
    /// 而 ComboState 期间没有任何东西会把它降下来 ——
    /// 结果是整个攻击动画期间角色匀速滑行，出招 0.5 秒就滑 0.5 秒，
    /// 像踩在冰上。
    ///
    /// 想要的是"往前一顿然后停下"，所以给一个持续时长，
    /// 速度在这段时间内线性衰减到 0。
    /// </summary>
    private void SetupThrust()
    {
        thrustTimer = 0f;
        thrustDuration = 0f;
        thrustVelocity = Vector2.zero;

        if (activeNode == null) return;

        // 蓄力位移优先：它有自己的方向解析与动量加成
        if (activeNode.useAimDash)
        {
            SetupAimDash();
            return;
        }

        if (activeNode.forwardThrust.sqrMagnitude < 0.0001f) return;

        // 带着滑铲动量进来时不覆盖它 ——
        // 否则"滑铲中出招"会在出招瞬间被拽回招式自己的速度，滑行断掉
        if (activeNode.inheritMomentum && sm.slideMomentum.IsActive) return;

        if (wasAirborneOnEnter && !activeNode.applyThrustInAir) return;

        float dir = sm.playerController.facingDirection;

        // X 乘朝向：填「前进多少」即可，不用管角色朝左朝右
        thrustVelocity = new Vector2(
            dir * activeNode.forwardThrust.x,
            activeNode.forwardThrust.y);

        thrustDuration = activeNode.thrustDuration;

        if (thrustDuration <= 0f)
        {
            // 兼容旧行为：设一次速度就不再管，整招匀速滑行
            ApplyThrustVelocity(1f);
        }
    }

    /// <summary>
    /// 【蓄力位移】AA1 / BB1 这类"带位移的蓄力攻击"。
    ///
    /// 与普攻的 forwardThrust 有两点本质不同：
    ///   ① 方向来自【瞄准】而不是角色朝向 —— 远程跟鼠标任意角度，近战四向吸附
    ///   ② 距离受【动量池】影响 —— 冲刺/滑铲后放能挪得更远
    ///
    /// 第二点是对"保持机动"的奖励：站桩蓄力只能拿低保距离，
    /// 而带着速度进蓄力再放，能挪出明显更远的身位。
    /// </summary>
    private void SetupAimDash()
    {
        Vector2 dir = ResolveAimDashDirection();

        // ==========================================================
        // 【负数 = 反向位移（后坐力）】
        //
        // 旧写法直接 Mathf.Max(baseDistance, momentum * scale)，把负数吃掉了：
        //   baseDistance = -2、动量 0 → Mathf.Max(-2, 0) = 0 → 完全不位移
        // 所以枪的后坐力填了负数没反应，而且填多少都一样。
        //
        // 病根是把【方向】和【距离】混在同一个数里，然后拿只认大小的 Max 去比。
        // 现在拆开：符号决定朝前还是朝后，绝对值参与低保与动量的比较。
        //
        // 于是 BB1 填 -3 就是「朝鼠标反方向弹开 3 格」，
        // 动量加成照常按绝对值生效，方向始终是后退。
        // ==========================================================
        float signedBase = activeNode.aimDashBaseDistance;
        float sign = signedBase < 0f ? -1f : 1f;

        float momentumValue = Momentum != null ? Momentum.Value : 0f;
        float distance = Mathf.Max(
            Mathf.Abs(signedBase),
            momentumValue * Mathf.Abs(activeNode.aimDashMomentumScale));

        // 朝向跟【瞄准】走，不跟位移走 —— 打后坐力时角色仍然面朝鼠标，
        // 只是身体被弹开。若跟着位移方向翻，开一枪人就背过身去了。
        Vector2 facingRef = dir;

        dir *= sign;

        // 贴着地面往下冲是没有意义的 —— 地面就在那儿，冲不进去
        if (IsGroundedDownwardDash(dir))
        {
            HandleGroundedDownwardDash(facingRef);
            return;
        }

        thrustDuration = Mathf.Max(0.01f, activeNode.aimDashDuration);

        // 位移速度按线性衰减积分反推：
        // 速度从 v 线性降到 0、历时 t，走过的距离是 v*t/2，
        // 所以要走 distance 就得从 2*distance/t 起步。
        float speed = 2f * distance / thrustDuration;

        thrustVelocity = dir * speed;
        thrustTimer = 0f;

        // 诊断：看不到这条日志 = 本文件没有被导入到工程里
        if (Buffer != null && Buffer.verboseLog)
        {
            Debug.Log(
                $"[蓄力位移] {activeNode.nodeName} 方向 {dir}，" +
                $"距离 {distance:F2}（低保 {activeNode.aimDashBaseDistance}，" +
                $"动量 {momentumValue:F1}×{activeNode.aimDashMomentumScale}），" +
                $"耗时 {thrustDuration:F2}s");
        }

        // 朝向对齐瞄准方向，避免"往左冲却面朝右"。
        // 正数位移时 facingRef == dir；负数（后坐力）时它仍指向鼠标。
        if (Mathf.Abs(facingRef.x) > 0.01f)
            sm.playerController.SetFacingDirection(facingRef.x > 0f ? 1 : -1);
    }

    /// <summary>
    /// 按节点配置的自由度解析瞄准方向。
    ///
    /// 【方向来源刻意绕开 AimProvider.AimDirection】
    /// 那条路带 Auto 降级（鼠标 2 秒没动就改用方向键，方向键没按又回退到角色朝向），
    /// 而蓄力时"瞄好了不动"恰恰是常态 —— 于是四向/八向几乎永远吸附到角色正面。
    /// 位移方向是一次性决策，必须问不会自作主张的来源。
    /// 详见 PlayerAimProvider.TryGetPointerDirection。
    /// </summary>
    private Vector2 ResolveAimDashDirection()
    {
        Vector2 facing = new Vector2(sm.playerController.facingDirection, 0f);

        // 明确要求只朝正面
        if (activeNode.aimDashMode == AimDashMode.FacingOnly)
        {
            LogAimDash("FacingOnly", "面朝方向", Vector2.zero, facing);
            return facing;
        }

        // ==========================================================
        // 【方向来源由武器类型决定，AimDashMode 只管吸附精度】
        //
        // 这两件事以前混在一起，所以近战也去读了鼠标 —— 但近战的攻击方向
        // 本来就不读鼠标，它跟人物朝向和方向键走。方向来源跟着武器的
        // 交互方式走，吸附精度跟着招式配置走，两个维度各自独立。
        //
        //   远程 → 鼠标（BB1 配负数距离就是朝鼠标反方向弹开，做后坐力）
        //   近战 → 方向键
        // ==========================================================
        bool isRanged = ownerWeapon != null && ownerWeapon.IsRanged;

        Vector2 raw;
        string sourceName;

        if (isRanged)
        {
            if (AimProvider == null || !AimProvider.TryGetPointerDirection(out raw))
            {
                LogAimDash(activeNode.aimDashMode.ToString(), "远程·拿不到鼠标→面朝方向", Vector2.zero, facing);
                return facing;
            }
            sourceName = "远程·鼠标";
        }
        else
        {
            raw = sm.playerController.moveInput;
            sourceName = "近战·方向键";

            // 没按方向键 → 朝当前面朝方向。
            // 这是最常见的情况（专心蓄力时手指往往已经离开方向键），
            // 必须有确定的落点，不能让这招时灵时不灵。
            if (raw.sqrMagnitude < DirectionDeadzone * DirectionDeadzone)
            {
                LogAimDash(activeNode.aimDashMode.ToString(), "近战·无方向键→面朝方向", Vector2.zero, facing);
                return facing;
            }
        }

        Vector2 result;

        switch (activeNode.aimDashMode)
        {
            case AimDashMode.Free: result = raw.normalized; break;
            case AimDashMode.EightWay: result = SnapDirection(raw, 45f); break;
            case AimDashMode.FourWay: result = SnapDirection(raw, 90f); break;
            default: result = facing; break;
        }

        LogAimDash(activeNode.aimDashMode.ToString(), sourceName, raw, result);
        return result;
    }

    /// <summary>方向键按到多大才算「有方向」。与路由器的 directionThreshold 同量级</summary>
    private const float DirectionDeadzone = 0.1f;

    /// <summary>本次位移是不是「站在地上还要往下冲」</summary>
    private bool IsGroundedDownwardDash(Vector2 dir)
        => sm.IsGrounded() && dir.y < GroundedDownwardThreshold;

    /// <summary>
    /// 【扩展点】贴地向下冲的处理。
    ///
    /// 当前实现：本次不位移。招式照常打出（动画、判定、伤害全都正常），
    /// 只是没有位移分量 —— 因为往地里冲是无效指令，不该因此让整招落空。
    ///
    /// 【以后要补的反馈】现在玩家按了向下却什么都没挪，
    /// 分不清是"这个方向不能冲"还是"我的输入没被识别"。设计上说好的表现是：
    ///     朝面朝方向挪一点点 + 拖尾朝上
    /// 也就是把向下的意图转译成一个明确可见的小动作，告诉玩家"收到了，但地面挡着"。
    ///
    /// 要补的时候把下面那段注释放开即可，方向已经算好传进来了：
    ///     thrustVelocity = new Vector2(facingRef.x >= 0f ? 1f : -1f, 0f) * 小速度;
    ///     thrustDuration = 短时长;
    ///     然后让拖尾组件朝上播一次
    /// 本方法之外的任何地方都不用改。
    /// </summary>
    private void HandleGroundedDownwardDash(Vector2 facingRef)
    {
        thrustVelocity = Vector2.zero;
        thrustDuration = 0f;
        thrustTimer = 0f;

        // 朝向仍然对齐瞄准，保持和正常位移一致
        if (Mathf.Abs(facingRef.x) > 0.01f)
            sm.playerController.SetFacingDirection(facingRef.x > 0f ? 1 : -1);

        if (Buffer != null && Buffer.verboseLog)
            Debug.Log("[蓄力位移] 在地面上向下冲 → 本次不位移（招式照常打出）");
    }

    /// <summary>
    /// 「贴地向下冲」的判定阈值。
    ///
    /// 用 -0.9 而不是 -0.5：四向的正下方是 (0,-1) 会被拦住，
    /// 而八向的左下 / 右下（y ≈ -0.707）不拦 —— 那两个方向的水平分量是有意义的位移，
    /// 玩家要的是"往斜下方冲"，贴着地面滑过去完全成立。
    /// 想连斜下也一并拦掉，把这个值调到 -0.5 左右即可。
    /// </summary>
    private const float GroundedDownwardThreshold = -0.9f;

    /// <summary>
    /// 【诊断】把吸附前后的角度都打出来。
    ///
    /// 肉眼分不清「四向吸附」和「自由跟随鼠标」——
    /// 因为鼠标大致水平时两者结果完全一样，只有把鼠标放在 40° 与 50° 这种
    /// 跨过吸附分界线的位置才看得出区别。与其反复试，不如让它自己报数。
    ///
    /// 判读方法：把鼠标放在角色右上方约 40°，再放到约 50°，各放一次 AA1。
    ///   FourWay 正常 → 吸附后应分别是 0° 和 90°
    ///   若两次吸附后都等于原始角度 → 资产上的 Aim Dash Mode 其实是 Free
    /// </summary>
    private void LogAimDash(string mode, string source, Vector2 raw, Vector2 snapped)
    {
        if (Buffer == null || !Buffer.verboseLog) return;

        float rawAngle = raw.sqrMagnitude > 0.0001f
            ? Mathf.Atan2(raw.y, raw.x) * Mathf.Rad2Deg
            : float.NaN;
        float snappedAngle = Mathf.Atan2(snapped.y, snapped.x) * Mathf.Rad2Deg;

        Debug.Log(
            $"[蓄力位移·方向] 来源={source}　模式={mode}　" +
            $"原始角度={rawAngle:F1}°　吸附后={snappedAngle:F1}°");
    }

    /// <summary>把任意角度吸附到最近的 stepDegrees 倍数</summary>
    private static Vector2 SnapDirection(Vector2 dir, float stepDegrees)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        angle = Mathf.Round(angle / stepDegrees) * stepDegrees;

        return new Vector2(
            Mathf.Cos(angle * Mathf.Deg2Rad),
            Mathf.Sin(angle * Mathf.Deg2Rad));
    }

    /// <returns>本帧是否由位移接管了速度</returns>
    private bool TickThrust()
    {
        if (thrustDuration <= 0f) return false;
        if (thrustTimer >= thrustDuration) return false;

        thrustTimer += Time.fixedDeltaTime;

        // 线性衰减：1 → 0
        float t = Mathf.Clamp01(1f - thrustTimer / thrustDuration);
        ApplyThrustVelocity(t);

        return true;
    }

    private void ApplyThrustVelocity(float scale)
    {
        float vy = Mathf.Abs(thrustVelocity.y) > 0.0001f
            ? thrustVelocity.y * scale
            : sm.rb.linearVelocity.y;   // 没配 Y 就别干扰重力

        // 空中招式如果不带 Y 位移，保持原来的"锁死"语义，避免自由落体
        if (wasAirborneOnEnter && Mathf.Abs(thrustVelocity.y) <= 0.0001f) vy = 0f;

        sm.rb.linearVelocity = new Vector2(thrustVelocity.x * scale, vy);
    }

    public override void Exit()
    {
        // 招式结束 → 这只手开始算后摇。
        // 无论是正常收招、被换手抢拍、还是被打断，都走这一条路，
        // 所以不存在"某种结束方式漏了没开始后摇"的可能。
        float recovery = ResolveRecoveryTime();
        Buffer?.BeginHandRecovery(ownerCmd, recovery, ownerAttackId);

        // 【蓄力 CD 第二步：用真实硬直把终点延长到位】
        //
        // 第一步在 OnAttackReleased —— 打出的那一刻先用 delay=0 占位，
        // 焊死"动画期间另一只手起手蓄力查不到 CD"的时间缝。
        // 这里补上完整语义：CD 要【接在收招硬直后面】——
        //     动画 → 收招硬直 → 蓄力 CD → 可以再蓄
        // 硬直多长只有演完才知道，所以终点必须在 Exit 里定。
        // StartChargeCooldown 内部取 Max，只延不缩，两步不会互相踩。
        //
        // 【普攻不会碰它】这是"打一发普攻垫一下再蓄"绕不过 CD 的原因：
        // 计时器只被这两处推后，普攻那条路径根本不认识它。
        if (activeNode != null && activeNode.isChargeSkill
            && WeaponMoveSet.TryCommandToSlot(ownerCmd, out WeaponSlot ownerSlot))
        {
            // 弱化版不加这段延迟：它的后摇会被 ChargeStunState.Enter 直接清掉，
            // 由僵直全盘接管。CD 与僵直同起同长，一起到期 ——
            // 否则会多出一小段"能动了但还是只能放弱化版"的夹缝。
            float cdDelay = isWeakenedAttack ? 0f : recovery;
            Loadout?.StartChargeCooldown(ownerSlot, activeNode.chargeLevel, cdDelay);
        }

        // 攻击结束必须把碰撞箱还原，否则会带着蹲姿/缩腿姿态离开攻击状态
        if (isBodyAdjusting)
        {
            sm.sensor?.SetColliderShape(PlayerSensor.ColliderShape.Normal);
            isBodyAdjusting = false;
        }

        HitDetection?.ForceStopHitbox();

        // 中断连射：否则玩家已经被击飞了，枪还在原地继续突突
        WeaponEmitter?.CancelBurst();

        sm.rb.gravityScale = originalGravity;
        activeNode = null;
        ownerWeapon = null;
        isWeakenedAttack = false;
    }

    /// <summary>
    /// 本招的后摇时长 —— 就是节点上配的值，不再做任何加工。
    ///
    /// 【强行打出的惩罚已经不在这里了】
    /// 上一版把弱化版的惩罚做成「把后摇撑到 chargeCooldownLv1」，那是错的：
    /// 后摇（handBusyUntil）只锁【一只手】，另一只手照常出招；
    /// 而且它不改变状态，空中掉落时方向键依然生效。
    /// 现在改由 ChargeStunState 承担，那是个真正管住全身的状态。
    /// </summary>
    private float ResolveRecoveryTime()
        => activeNode != null ? activeNode.recoveryTime : 0f;

    /// <summary>
    /// 本招演完后是否要接一段僵直（只有强行打出的弱化蓄力才要）。
    ///
    /// 【为什么由状态机在动画结束时来问，而不是本状态在 Exit 里自己切】
    /// Exit 会在所有离开攻击的路径上跑 —— 包括被冲刺/滑铲主动取消。
    /// 那时状态机正在切去 DashState，若从 Exit 里再发起一次切换，
    /// 会把玩家刚花掉的那次冲刺顶掉。所以"要不要僵直"只是一个查询，
    /// 由知道自己是不是"自然演完"的调用方来决定。
    /// </summary>
    public bool TryGetPendingStun(out float seconds)
    {
        seconds = 0f;

        if (!isWeakenedAttack || ownerWeapon == null) return false;

        // 默认取 Lv1 的 CD —— 僵直与 CD 同长同起，两者一起到期：
        // 僵直一解除就能正常蓄力，不会出现"能动了但还是只能放弱化版"的夹缝。
        // 想让僵直独立于 CD，去武器资产上填 Weakened Stun Duration。
        seconds = ownerWeapon.GetWeakenedStunDuration();
        return seconds > 0f;
    }

    /// <summary>
    /// 超时未收到 OnAttackAnimationEnd。
    ///
    /// 警告里【点名是哪个招式】—— 这是本机制最大的价值：
    /// 不用再一个个试，Console 直接告诉你哪个动画漏了事件。
    /// </summary>
    private void WarnAndExitOnTimeout()
    {
        if (!hasWarnedTimeout)
        {
            hasWarnedTimeout = true;

            string nodeName = activeNode != null ? activeNode.nodeName : "(未知招式)";
            string clipName = (activeNode != null && activeNode.attackClip != null)
                ? activeNode.attackClip.name
                : "(无剪辑)";

            Debug.LogWarning(
                $"[ComboState] 招式「{nodeName}」超过 {timeoutLimit:F2} 秒仍未收到 " +
                $"OnAttackAnimationEnd 事件，已强制退出。\n" +
                $"请检查动画「{clipName}」是否在最后一帧配置了该 Animation Event，" +
                "并确认它没有勾选 Loop Time。", sm);
        }

        // 走和正常收招一样的流程，保证连招宽恕期照常开始
        Buffer.StartGracePeriod();

        // 兜底超时也算「自然演完」—— 弱化版照样要吃僵直，
        // 否则漏配一个动画事件就等于免掉了惩罚
        if (TryGetPendingStun(out float stunSeconds))
        {
            sm.chargeStunState.SetDuration(stunSeconds);
            sm.ChangeState(sm.chargeStunState);
            return;
        }

        if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
        else HandleLanding();
    }
}