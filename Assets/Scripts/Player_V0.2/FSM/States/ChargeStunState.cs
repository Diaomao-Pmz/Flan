using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 【强行打出的僵直】—— 弱化版蓄力的代价。
///
/// ==========================================================
/// 【为什么必须是一个 State，而不是把后摇调长】
///
/// 上一版把惩罚做成「加长该手的 recoveryTime」，有三个问题：
///
///   ① 粒度错了 —— handBusyUntil 只锁【一只手】。另一只手照常出招，
///      而设计意图是"这段时间你什么都做不了"。
///   ② 空中漏了 —— 后摇不改变状态，角色还待在 FallState 里，
///      于是掉落途中方向键照常生效，落地才被 Idle/Run 的后摇检查拦住。
///      倒计时却是从出招那一刻就开始走的，等于空中那段惩罚白送。
///   ③ 打断没有出口 —— 后摇是个时间戳，没有"被别的事情中止"的概念。
///
/// 做成状态之后三个问题一起消失：状态天然是全局的、天然管住空中，
/// 而"被打断"就是状态机本来就在做的事 —— 受击时 HandlePlayerHit
/// 会无条件 ChangeState(hitState)，僵直自然被接管，不需要任何额外代码。
/// ==========================================================
///
/// 【什么时候进来】
/// 只在弱化版蓄力【自然演完】时进入（动画事件结束 / 兜底超时）。
/// 被冲刺、滑铲主动取消掉的不进 —— 那种情况玩家已经付了一次位移 CD，
/// 而且从 Exit 里发起状态切换会和正在进行的切换打架。
///
/// ==========================================================
/// 【僵直期间的表现跟随"进来之前按的那个键"】
///
/// 本状态禁止一切操作，但它【不该看起来像什么都没发生】——
/// 玩家在弱化招的动画里按下的那一次冲刺/滑铲是一个真实的选择，
/// 表现上必须认得出来，否则那次选择就白做了。
///
///   进来前按了 Shift → 拖尾亮起，冲出去（和普通冲刺的视觉完全一致）
///   进来前按了 Ctrl  → 滑铲动画 + 压低碰撞箱，减速到阈值切成蹲姿
///   进来前什么都没按 → 站着不动
///
/// 地面上三者的【物理】是同一套（一记推力，然后全程减速），
/// 区别只在动画、碰撞箱和拖尾这三样表现上 —— 见 RefreshEscapePose。
///
/// 但在【空中】两者是真的不一样，依据是它们在游戏别处本来的性质：
///
///   冲刺 = 空中动作 → 当场释放，整段位移期间关掉重力
///                     （和 DashState 一样），冲完才垂直下坠
///   滑铲 = 地面动作 → 空中先攒着不放、垂直坠落，脚一沾地才弹射起步
///
/// 详见 TryReleaseEscape。这条分野不是为了做两套逻辑，恰恰相反 ——
/// 是为了让玩家不用额外记规则：那个键在别处什么脾气，这里就什么脾气。
/// ==========================================================
/// </summary>
public class ChargeStunState : PlayerStateBase
{
    private float timer;
    private float duration;

    // ---- 预付的逃逸冲劲 ----
    private bool pendingEscape;
    private bool pendingEscapeIsDash;
    private float pendingEscapeDirection = 1f;

    // ---- 本次僵直里冲劲的运行时状态 ----
    private float escapeSpeed;
    private float escapeDirection;
    private float escapeElapsed;
    private float escapeDuration;

    /// <summary>
    /// 本次僵直有没有预付冲劲。
    /// 【不能用 escapeSpeed &gt; 0 代替】冲劲跑完之后速度归零，但"这次是滑铲兑换的"
    /// 这个事实必须一直留到僵直结束 —— 否则滑铲逃逸减速完会突然从蹲姿弹回站姿。
    /// </summary>
    private bool hasEscape;

    /// <summary>
    /// 这股冲劲是冲刺还是滑铲兑换的 —— 决定僵直期间玩家【长什么样】。
    ///
    /// 【为什么要留到 Enter 之后】pendingEscapeIsDash 在 Enter 里就被消费掉了，
    /// 但表现要跟着整段僵直走：滑铲逃逸得一路滑着、减速到阈值再切蹲下。
    /// 这是「进大rt 前按的是哪个键」这一事实的延续，属于本次僵直的性质，
    /// 和 ComboState 拍 ownerCmd 快照是同一个套路。
    /// </summary>
    private bool escapeIsDash;

    /// <summary>
    /// 冲劲已经备好，但还【没开始跑】。
    ///
    /// 只有滑铲会停在这个阶段：它是地面动作，空中放不出来（和真滑铲一样，
    /// 路由器里空中按 Ctrl 也是转去空中指令、不进 SlideState）。
    /// 所以空中打出弱蓄再按 Ctrl 的话，冲劲先攒在手里、人垂直坠落，
    /// 脚一沾地才弹射起步 —— 之后和地面滑铲逃逸走完全同一条路。
    ///
    /// 冲刺不会停在这里：它在 Enter 那一刻就直接释放。
    /// </summary>
    private bool escapeArmed;

    /// <summary>冲劲已经开始跑了。计时从这一刻算，不是从进僵直那一刻算</summary>
    private bool escapeReleased;

    /// <summary>
    /// 本状态把重力关掉了吗（冲刺逃逸期间会关）。
    ///
    /// 【为什么要记一个布尔而不是每帧无脑写】关重力是"我借用了一下"，
    /// 必须知道自己有没有借过才能准确还回去。而且还的时候要还
    /// sm.defaultGravityScale（出厂值），不是还当前值 —— 那正是文档 6.2
    /// 「gravityScale 快照污染」踩过的坑：读当前值会把别人临时置的 0 固化下来。
    /// </summary>
    private bool escapeGravitySuspended;

    /// <summary>
    /// 本次逃逸用的衰减指数。
    ///
    /// 【为什么在 Enter 拍快照，而不是每帧去问配置】
    /// 初速度是【按这个指数反推出来的】—— 两者必须来自同一个值，
    /// 否则走出来的距离就不等于面板上填的那个数。
    /// 每帧现问的话，只要配置在僵直期间被改（编辑器里调参就会发生），
    /// 速度和曲线立刻对不上。这和 ComboState.Enter 拍 ownerCmd / isWeakenedAttack
    /// 是同一个套路：一次行为的性质，在开始那一刻定死。
    /// </summary>
    private float escapeExponent;

    private PlayerMovementConfig MoveCfg
        => sm.playerState != null ? sm.playerState.movementConfig : null;

    public ChargeStunState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    /// <summary>设定本次僵直时长。必须在 ChangeState 之前调用</summary>
    public void SetDuration(float seconds)
    {
        duration = Mathf.Max(0f, seconds);
    }

    /// <summary>
    /// 【预付冲刺 / 滑铲换来的初速度】
    ///
    /// 由路由器在【弱化蓄力动画期间】收到冲刺/滑铲时写入。
    ///
    /// 那一下不会真的进入 DashState / SlideState —— 否则僵直直接被跳过，
    /// 惩罚形同虚设。它只兑换成一股初速度，在僵直里自行衰减：
    /// 玩家依然全程不能操作，但能带着这股冲劲滑出去一段。
    ///
    /// 于是"想脱离"这件事仍然要付代价（一次冲刺 CD），
    /// 拿到的却不是自由，而是一个【方向在按下那一刻就定死】的位移。
    /// </summary>
    /// <param name="isDash">true = 冲刺兑换的，false = 滑铲兑换的。决定读哪个初速度</param>
    /// <param name="direction">1 = 右，-1 = 左。按下那一刻定死，僵直里改不了</param>
    public void SetEscapeMomentum(bool isDash, float direction)
    {
        pendingEscape = true;
        pendingEscapeIsDash = isDash;
        pendingEscapeDirection = direction >= 0f ? 1f : -1f;
    }

    /// <summary>剩余僵直秒数。供 UI 显示</summary>
    public float Remaining => Mathf.Max(0f, timer);

    public override void Enter()
    {
        timer = duration;

        // 重力恢复出厂值：可能是从空中蓄力的"钉住"状态一路过来的
        sm.rb.gravityScale = sm.defaultGravityScale;

        // 只清水平速度。Y 轴留给重力 —— 空中僵直应该是【直着掉下去】，
        // 而不是浮在半空，那看起来像卡死。
        sm.rb.linearVelocity = new Vector2(0f, sm.rb.linearVelocity.y);

        // 把已经攒下的输入全部倒掉。
        // 僵直期内的新输入会被入口挡住（见 ComboInputBuffer.AcceptsAttackInput
        // 与 PlayerCommandRouter.OnCommand），但【进来之前】按下的那些还在缓存里，
        // 不清的话僵直一结束就会集体兑现，角色自己动起来。
        sm.commandRouter?.ClearBuffer();
        sm.inputBuffer?.ClearHandRecovery();   // 顺带清掉两只手的后摇与长按意图
        sm.inputBuffer?.ResetCombo();

        // 【正在进行中的蓄力也要掐掉】—— 缓存清得再干净也管不到它。
        //
        // 弱蓄的动画还在演时，另一只手是自由的，长按会真的开始蓄力：
        // 那不是待兑现的"预输入"，而是一个每帧都在涨的活体进程。
        // 进僵直只清缓存的话，它照常蓄满，松手照常打出 ——
        // 表现出来就是「交替长按左右键，在僵直里无限复读 AA1/BB1」。
        //
        // 后果与被受击打断一致：什么都不放（HandlePlayerHit 也是这么做的）。
        sm.chargeSystem?.CancelAll();

        // 取出预付的冲劲。
        //
        // 【不复用 slideMomentum】那套的减速率是写死的 slideDeceleration，
        // 距离由它和初速度自己算出来，调不了。而逃逸要的是"距离说了算、
        // 曲线形状单独调"，而且冲刺和滑铲各要一套。两边的调参目标根本不同，
        // 硬共用会互相牵制 —— 想把逃逸调短就会改到真滑铲的手感。
        escapeElapsed = 0f;
        escapeSpeed = 0f;
        escapeDuration = 0f;
        escapeExponent = 1f;
        hasEscape = false;
        escapeIsDash = false;
        escapeArmed = false;
        escapeReleased = false;
        escapeGravitySuspended = false;

        if (pendingEscape)
        {
            SetupEscape(pendingEscapeIsDash, pendingEscapeDirection);
            pendingEscape = false;
        }

        // 冲刺当场就放；滑铲要脚沾地才放（空中先攒着）
        TryReleaseEscape();

        // 表现按预付的动作类型走 —— 见 RefreshEscapePose。
        // 这里只是定初值，之后每帧重算（地面/空中、速度都会变）。
        RefreshEscapePose(restartAnim: true);
    }

    /// <summary>冲劲已经释放、并且还没衰减完</summary>
    private bool IsEscapeRunning
        => hasEscape && escapeReleased && escapeDuration > 0f && escapeElapsed < escapeDuration;

    /// <summary>
    /// 这一轮已经拿到过一份冲劲了。
    ///
    /// 【一次大rt 只兑换一个逃逸动作，先来的算数】
    /// 路由器与本状态都靠它去重 —— 否则"演出中点了 Shift + 落地时还按着 Ctrl"
    /// 会花掉两个位移 CD，而玩家只看得到一次位移。
    ///
    /// 【必须把 pendingEscape 也算进来】冲劲有两个存放阶段：
    ///   演出期间预付的  → 存在 pendingEscape 里，Enter 时才转成 hasEscape
    ///   僵直中临时授予的 → 直接进 hasEscape
    /// 只看 hasEscape 的话，同一次演出里连按 Shift 和 Ctrl 会各扣一次 CD ——
    /// 因为那时 Enter 还没跑，hasEscape 还是 false。
    /// </summary>
    public bool HasEscape => hasEscape || pendingEscape;

    /// <summary>
    /// 丢掉还没兑现的预付冲劲。
    ///
    /// 【为什么必须有】预付是在弱化招的【演出期间】记下的，而僵直要等演出
    /// 自然结束才会开始。如果这中间玩家被打断（受击），僵直永远不会发生 ——
    /// 那股冲劲就一直挂在这里，等到【下一次】弱蓄进僵直时被白送出去。
    ///
    /// 表现会是"我这次明明什么都没按，人却自己滑出去了"，
    /// 而且要隔好几分钟、要恰好挨过一次打才复现 —— 典型的脏数据残留。
    /// 这正是军规里「进状态拍快照、退状态彻底重置」要防的东西。
    /// </summary>
    public void CancelPendingEscape()
    {
        pendingEscape = false;
    }

    /// <summary>
    /// 【僵直进行中临时授予冲劲】由路由器在「角色第一次真的能滑的那一帧，
    /// Ctrl 与方向键都还按着」时调用。
    ///
    /// ==========================================================
    /// 【为什么需要这条路，而不是全靠演出期间的预付】
    ///
    /// 预付走的是按键【脉冲】（PlayerController 只在 ctx.started 时发指令）。
    /// 而"空中长按蓄力时就按住了 Ctrl"这个操作，脉冲发生在【空中蓄力姿态】
    /// 那会儿 —— 那时路由器按规则把它拒了（空中蓄力禁止滑铲），而且不进缓存。
    /// 之后玩家一直按着不放，就再没有第二个脉冲了，预付永远不会发生。
    ///
    /// 这正是 HandleLandingFrame 当初解决过的同一个问题：
    /// "按住 Ctrl 下落"这个意图可能持续好几秒，0.15 秒的缓存表达不了它。
    /// 所以那里的做法是【不看缓存、不看状态，只看事实：Ctrl 还按着吗】。
    /// 本方法是同一条教义在大rt 里的延伸。
    ///
    /// 【为什么判定点是"能滑的那一刻"而不是"演出开始"】
    /// 演出开始就判定太早：玩家可能还要在空中飘很久，中途松开 Ctrl 改主意，
    /// 而 CD 已经被扣掉了。放到真能滑的那一刻，玩家的手是什么样，结果就是什么样。
    /// ==========================================================
    /// </summary>
    /// <param name="isDash">true = 冲刺兑换，false = 滑铲兑换</param>
    /// <param name="direction">1 = 右，-1 = 左</param>
    public void GrantEscapeNow(bool isDash, float direction)
    {
        if (hasEscape) return;   // 已经有一份了，不重复给

        SetupEscape(isDash, direction);
        TryReleaseEscape();
    }

    /// <summary>
    /// 配好这一份冲劲的方向与曲线。预付（Enter）和临时授予（GrantEscapeNow）共用，
    /// 保证两条路算出来的冲劲完全一样 —— 规则只有一份。
    /// </summary>
    private void SetupEscape(bool isDash, float direction)
    {
        var cfg = MoveCfg;

        hasEscape = true;
        escapeIsDash = isDash;
        escapeDirection = direction >= 0f ? 1f : -1f;

        // 【冲刺与滑铲各读各的一套参数】两者定位不同：
        // 冲刺是空中脱离（要干脆），滑铲是贴地溜走（还要接蹲姿收尾）。
        // 共用一条曲线的话，调好了一个另一个必然被带歪。
        float distance = isDash
            ? (cfg != null ? cfg.escapeDashDistance : 1.4f)
            : (cfg != null ? cfg.escapeSlideDistance : 0.9f);

        escapeDuration = Mathf.Max(0.01f, isDash
            ? (cfg != null ? cfg.escapeDashDuration : 0.25f)
            : (cfg != null ? cfg.escapeSlideDuration : 0.25f));

        escapeExponent = Mathf.Max(0.01f, isDash
            ? (cfg != null ? cfg.escapeDashDecayExponent : 1f)
            : (cfg != null ? cfg.escapeSlideDecayExponent : 1f));

        // 面板上填的是【距离】，初速度在这里反推。
        //
        // 曲线是 v(t) = v0 × (1 - (t/T)^n)，对时间积分得总位移：
        //     S = v0 × T × n/(n+1)
        // 反解出初速度：
        //     v0 = S × (n+1) / (n × T)
        //
        // 于是「挪多远」只由 distance 决定，T 和 n 只影响这段距离怎么走完。
        escapeSpeed = Mathf.Max(0f, distance)
                      * (escapeExponent + 1f)
                      / (escapeExponent * escapeDuration);

        escapeArmed = true;
        escapeReleased = false;
        escapeElapsed = 0f;
    }

    /// <summary>
    /// 【释放冲劲的唯一入口】—— 决定"什么时候开始跑"。
    ///
    /// ==========================================================
    /// 两种逃逸在这里分道扬镳，依据是它们在游戏里本来的性质：
    ///
    ///   冲刺 = 空中动作。真冲刺在空中就能用，而且 DashState 期间重力是关的。
    ///          所以这里也当场释放 + 关重力 —— 表现是「先平着冲出去，
    ///          冲完再垂直掉下来」，而不是一边掉一边飘。
    ///
    ///   滑铲 = 地面动作。真滑铲在空中放不出来（路由器里空中按 Ctrl
    ///          会转去空中指令，根本不进 SlideState）。所以空中先攒着，
    ///          人垂直坠落，脚一沾地才弹射起步。
    ///
    /// 【为什么不给滑铲也做一套空中版本】那等于凭空多出一个"空中滑铲"，
    /// 玩家在别的任何地方都没见过它。逃逸的表现跟着玩家按的那个键走，
    /// 那个键在别处是什么脾气，这里就该是什么脾气 —— 否则规则就得单独记一条。
    /// ==========================================================
    ///
    /// 本方法每帧都会被调用（Enter 一次、FixedUpdate 每帧），
    /// 内部靠 escapeReleased 保证只真正释放一次。
    /// </summary>
    private void TryReleaseEscape()
    {
        if (!escapeArmed || escapeReleased) return;

        // 滑铲是地面动作：还在空中就继续攒着
        if (!escapeIsDash && !sm.IsGrounded()) return;

        escapeReleased = true;

        // 【计时从释放那一刻算起】不是从进僵直那一刻。
        // 否则空中攒了 0.5 秒的滑铲，落地时冲劲已经"衰减"掉大半了。
        escapeElapsed = 0f;

        if (escapeIsDash)
        {
            // 冲刺逃逸期间关掉重力 —— 与 DashState.Enter 同一个做法。
            // 顺带把下落速度清零，否则会带着进入僵直前的坠速斜着冲出去。
            sm.rb.gravityScale = 0f;
            sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, 0f);
            escapeGravitySuspended = true;
        }
    }

    /// <summary>
    /// 把借走的重力还回去。
    ///
    /// 【必须还出厂值，不能还"进来时读到的值"】见文档 6.2：
    /// 读当前值会把别人临时置的 0 当成原值固化下来，角色从此永远飘着。
    /// 本状态可能正是从"空中蓄力钉住（gravityScale = 0）"那里过来的。
    /// </summary>
    private void RestoreEscapeGravity()
    {
        if (!escapeGravitySuspended) return;

        sm.rb.gravityScale = sm.defaultGravityScale;
        escapeGravitySuspended = false;
    }

    /// <summary>
    /// 【僵直期间玩家长什么样】跟随「进大rt 之前按下的那个键」。
    ///
    /// ==========================================================
    /// 设计意图：这一下逃逸不该看起来像"站着被平移出去"，
    /// 而该看起来就是玩家选的那个动作 ——
    ///
    ///   按了 Shift → 冲刺的样子：拖尾亮起，姿态不变
    ///                （项目里冲刺本来就没有专属动画，DashState 也只开拖尾，
    ///                  拖尾就是冲刺唯一的视觉标志。这里和它保持一致，
    ///                  以后补了 Dash 动画两处一起改。）
    ///   按了 Ctrl  → 滑铲的样子：滑铲动画 + 压低碰撞箱 + 拖尾，
    ///                减速到蹲下阈值时切成蹲姿（和 SlideState 转 CrouchState 同一个阈值）
    ///   什么都没按 → 站着不动（原行为）
    ///
    /// 【为什么每帧重算而不是 Enter 里定一次】
    /// 环境是会变的：空中僵直会落地、滑铲冲劲会衰减过阈值。
    /// 只在进入时判断一次的话，空中打出的滑铲逃逸落地后会一直停在下落姿势。
    /// 这和 ComboState.TickBodyAdjust 当初踩的是同一个坑 ——
    /// "按下时判断一次"碰上"环境后来变了"，必然停在过期的形态上。
    ///
    /// 【注意这里只管表现，不管物理】速度全部由 TickEscapeMomentum 负责。
    /// 滑铲的减速逻辑没有被复制过来一行 —— 本状态用的是自己的曲线，
    /// 只是借用了 SlideState 的【动画与阈值】，读的还是同一个真相源。
    /// ==========================================================
    /// </summary>
    private void RefreshEscapePose(bool restartAnim)
    {
        // ---- 拖尾：冲劲还在跑就亮着 ----
        bool wantTrail = IsEscapeRunning;
        if (sm.dashTrail != null && sm.dashTrail.emitting != wantTrail)
            sm.dashTrail.emitting = wantTrail;

        // ---- 动画与碰撞箱 ----
        int animHash;
        PlayerSensor.ColliderShape shape = PlayerSensor.ColliderShape.Normal;

        if (!sm.IsGrounded())
        {
            // 空中没有滑铲这回事，一律下落姿势。落地后本方法会自动切过去。
            animHash = PlayerAnimHash.JumpFall;
        }
        else if (hasEscape && !escapeIsDash)
        {
            // 滑铲兑换的冲劲：滑着出去，慢下来就蹲着。
            //
            // 【读当前实际速度而不是曲线算出来的值】撞墙停住时也该跟着切蹲姿 ——
            // 玩家看到的是"我停下来了"，姿态就该对上。
            float speed = Mathf.Abs(sm.rb.linearVelocity.x);

            // 与 SlideState 转入 CrouchState 用的是同一个阈值表达式，
            // 不是另抄一份参数 —— 想调"多慢算停下"只有一个地方要改。
            float crouchThreshold = sm.moveSpeed * sm.crouchSpeedMultiplier;

            animHash = speed > crouchThreshold ? PlayerAnimHash.Slide : PlayerAnimHash.Crouch;
            shape = PlayerSensor.ColliderShape.Crouch;
        }
        else
        {
            // 冲刺逃逸 与 没有逃逸：都是站姿。
            // 两者的区别全在上面那条拖尾上 —— 和普通冲刺的表现完全一致。
            animHash = PlayerAnimHash.Idle;
        }

        sm.animDriver.SetBase(animHash, restartAnim);

        if (sm.sensor != null && sm.sensor.CurrentShape != shape)
            sm.sensor.SetColliderShape(shape);
    }

    public override void Update()
    {
        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
            else HandleLanding();
            return;
        }

        // 每帧重算表现：空中落地、滑铲冲劲衰减过阈值，都要跟着换姿态。
        // 僵直倒计时不受这些影响，也不因为落地而缩短 ——
        // 否则"在高处放"就成了逃避惩罚的技巧。
        RefreshEscapePose(restartAnim: false);
    }

    public override void FixedUpdate()
    {
        // 滑铲的冲劲攒在手里等落地 —— 每帧问一次脚沾地没有
        TryReleaseEscape();

        // 预付的冲劲优先。玩家在这段时间里【依然不能操作】——
        // 方向、跳跃、冲刺全部无效，只是身体带着按下那一刻定死的方向滑出去。
        if (TickEscapeMomentum()) return;

        // 冲劲跑完了（或者压根没有）：把借走的重力还回去。
        // 对冲刺逃逸来说，这一刻正是"位移结束、开始垂直下坠"的转折点。
        RestoreEscapeGravity();

        // 每帧按住水平速度为 0。
        // 方向键在僵直期间完全无效，地面空中一视同仁 ——
        // 这正是空中那个 bug 的修复处：以前角色留在 FallState 里，
        // 掉落途中 ApplyHorizontalMove 照常响应方向键。
        //
        // 滑铲逃逸在空中攒着的时候也走这条：水平速度按死，Y 交给重力，
        // 于是表现为【垂直坠落】，落地那一帧才弹射出去。
        sm.rb.linearVelocity = new Vector2(0f, sm.rb.linearVelocity.y);
    }

    /// <summary>
    /// 【逃逸冲劲的衰减曲线】开口向下抛物线的右半部分。
    ///
    ///     速度 = 初速度 × (1 - (t / 总时长) ^ 指数)
    ///
    /// 指数 = 2 时，t=0 处斜率为 0（起步几乎不掉速），
    /// 越接近尾声掉得越快，末端一口气归零 —— 就是"先飘一段再急刹"的手感。
    /// 指数 = 1 退化成匀减速；调大则飘得更久、刹得更狠。
    ///
    /// 【为什么写成"按已过时间求值"而不是"每帧减一点"】
    /// 逐帧累减的话，实际曲线会受帧率影响，而且改指数要重推减速率。
    /// 直接对时间求值则曲线形状是确定的，参数所见即所得。
    ///
    /// 【总距离由 Enter 里的反推保证】初速度已经按 距离/时长/指数 算好，
    /// 所以这条曲线走完，位移必然等于面板上填的 escapeDashDistance。
    /// 本方法只负责"怎么走"，不再参与"走多远"。
    /// </summary>
    /// <returns>本帧是否由冲劲接管了水平速度</returns>
    private bool TickEscapeMomentum()
    {
        if (!escapeReleased) return false;          // 还攒在手里（空中的滑铲）
        if (escapeDuration <= 0f || escapeSpeed <= 0f) return false;
        if (escapeElapsed >= escapeDuration) return false;

        escapeElapsed += Time.fixedDeltaTime;

        float t = Mathf.Clamp01(escapeElapsed / escapeDuration);

        // 用 Enter 时拍下的指数，不现问配置 —— 必须和反推初速度时用的是同一个值
        float factor = 1f - Mathf.Pow(t, escapeExponent);

        sm.rb.linearVelocity = new Vector2(
            escapeDirection * escapeSpeed * factor,
            sm.rb.linearVelocity.y);

        return true;
    }

    public override void Exit()
    {
        timer = 0f;
        duration = 0f;

        // 没用掉的预付冲劲不留到下一次僵直去
        pendingEscape = false;
        escapeSpeed = 0f;
        escapeDuration = 0f;
        escapeElapsed = 0f;
        escapeExponent = 1f;
        hasEscape = false;
        escapeIsDash = false;
        escapeArmed = false;
        escapeReleased = false;

        // ---- 重力必须还原 ----
        //
        // 【这条不还会是灾难性的】冲刺逃逸期间重力是关着的。
        // 如果这时被受击打断（HitState.Enter 只写速度、不碰 gravityScale），
        // 角色会带着 gravityScale = 0 离开本状态 —— 从此永远浮在空中，
        // 而且看不出是谁干的。这正是文档 6.2 那类 bug 的翻版。
        RestoreEscapeGravity();

        // ---- 表现必须还原，否则会把姿态带出这个状态 ----
        //
        // 【这是「警惕状态残留」那条军规的正面例子】
        // 滑铲逃逸会压低碰撞箱、点亮拖尾。若不在这里撕干净，
        // 玩家出了僵直还顶着一个矮碰撞箱到处跑 —— 而且因为精灵图完全正常，
        // 这种 bug 只会表现为"有时候莫名其妙钻得过某些缝"，极难定位。
        //
        // 无条件还原（不判断"是不是我开的"）：和 DashState / SlideState 的
        // Exit 做法一致，下一个状态要用会自己再打开。
        if (sm.dashTrail != null) sm.dashTrail.emitting = false;
        sm.sensor?.SetColliderShape(PlayerSensor.ColliderShape.Normal);
    }
}
