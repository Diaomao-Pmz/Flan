using Flandre.CombatSystem;
using UnityEngine;
using UnityEngine.UIElements;

public class PlayerStateMachine : MonoBehaviour
{
    public IState currentState; 

    public IdleState idleState;
    public RunState runState;
    public JumpState jumpState;
    public FallState fallState;
    public DashState dashState; 
    public CrouchState crouchState;
    public SlideState slideState;
    public FlyState flyState;

    public ComboState comboState;

    public ChargeState chargeState;
    public HitState hitState;


    public Animator anim;
    public Rigidbody2D rb;
    public TrailRenderer dashTrail;         // 尾迹
    public PlayerController playerController;
    public PlayerState playerState;

    /*    
    //技能相关
    [Header("Skill")]
    [SerializeField] SkillTree skillTree;
    */


    //跑步
    [Header("Run Settings")]
    public float moveSpeed = 6f; // 跑动速度

    //跳跃
    [Header("Jump Settings")]
    public int maxJumps = 2;   // 最大跳跃次数
    public int jumpCount = 0;
    public float jumpForce = 10f;
    public float minJumpVelocity = 4f;
    public Transform groundCheck; // 脚底的空物体（传感器）
    public LayerMask groundLayer; // 什么图层算是地面

    //dash
    [Header("Dash Settings")]
    public float dashSpeed = 12f;           // 冲刺速度
    public float dashDuration = 0.2f;       // 冲刺持续时间
    public ComboSkill dashSkill = new ComboSkill();

    [Header("Slide Settings")]
    public float slideStartSpeedMultiplier = 1.2f; // 滑铲初速度倍率（留给你在Inspector里调 1.1~1.2）
    public float slideDeceleration = 8f;          // 滑铲速度减速率（数值越大减速越快，方便你调试）
    public float crouchSpeedMultiplier = 0.3f;     // 蹲下速度倍率（用来计算什么时候停下切Crouch）
    public ComboSkill slideSkill = new ComboSkill();

    //蹲下
    [Header("Crouch Settings")] // 新增：蹲下相关设置
    public Transform ceilingCheck; // 头顶雷达
    [HideInInspector] public BoxCollider2D coll;
    [HideInInspector] public Vector2 originalColliderSize;
    [HideInInspector] public Vector2 originalColliderOffset;
    public Transform hurtboxCore; // 把你的 Hurtbox_Core 拖到这里
    private Vector3 originalHurtboxPos; // 记录它站立时的初始本地坐标

    //被击飞
    [Header("Hit settings")]
    public float hitStunDuration = 0.35f; // 玩家失去控制的硬直时间
    public Vector2 hitKnockbackForce = new Vector2(8f, 5f); // 默认击飞力度 (X水平, Y垂直)
    public float blinkInterval = 0.1f; // 闪烁频率（多少秒闪一次）

    [Header("Shoot settings")]
    [Tooltip("远程子弹预制体")]
    public GameObject projectilePrefab;
    [Tooltip("子弹发射的枪口位置")]
    public Transform firePoint;
    [Tooltip("常规射击子弹的速度")]
    public float projectileSpeed = 12f;

    [Header("Fly Settings")]
    public float hoverChargeTime = 1.0f;       // 长按多少秒后进入飞行
    public float flyManaCostPerSecond = 10f;   // 每秒耗蓝 (n)
    public float flyCancelJumpForce = 8f;      // 取消飞行时的向上冲力 (x)
    public float flySpeed = 5f;                // 飞行时的平移速度

    //Objects
    [HideInInspector] public Vector2 dashAnchorPos;
    [HideInInspector] public Vector2 slideAnchorPos;
    [HideInInspector] public Vector2 jumpAnchorPos;

    [Header("Relay Visuals")]
    public GameObject anchorPrefab; // 待会儿在 Unity 里把你做的图片预制体拖进来
    [HideInInspector] public GameObject activeDashAnchor;
    [HideInInspector] public GameObject activeSlideAnchor;
    [HideInInspector] public GameObject activeJumpAnchor;

    //relay相关
    [HideInInspector] public bool isDashRelay = false;
    [HideInInspector] public bool isSlideRelay = false;
    [HideInInspector] public bool isJumpRelay = false;
    [HideInInspector] public bool hasJumpAnchor = false;

    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerState = GetComponent<PlayerState>();
        anim = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        coll = GetComponent<BoxCollider2D>();

        // 新增：记录原始碰撞体的尺寸，以便站起时恢复
        if (coll != null)
        {
        originalColliderSize = coll.size;
        originalColliderOffset = coll.offset;
        }

        idleState = new IdleState(this);
        runState = new RunState(this);
        jumpState = new JumpState(this);
        fallState = new FallState(this);
        dashState = new DashState(this);
        crouchState = new CrouchState(this);
        slideState = new SlideState(this);
        flyState = new FlyState(this);

        comboState = new ComboState(this);

        chargeState = new ChargeState(this);
        hitState = new HitState(this);

        if (dashTrail == null) dashTrail = GetComponentInChildren<TrailRenderer>(); // 尾迹

        coll = GetComponent<BoxCollider2D>();
        originalColliderSize = coll.size;
        originalColliderOffset = coll.offset;

        if (hurtboxCore != null)
        {
            originalHurtboxPos = hurtboxCore.localPosition;
        }
    }

    void Start()
        {
        // 游戏开始，强行进入待机状态
        ChangeState(idleState);
        if (playerState != null && playerState.health != null)
        {
            playerState.health.OnPlayerHit += HandlePlayerHit;
        }

        if (playerState == null)
        {
            Debug.LogError("【致命错误】playerState 是空的！请检查 Awake 里是否忘记写 playerState = GetComponent<PlayerState>();");
        }
        else if (playerState.health == null)
        {
            Debug.LogError("【致命错误】health 模块是空的！");
        }
        else
        {
            // 养成好习惯，先取消再订阅，防止重复绑定
            playerState.health.OnPlayerHit -= HandlePlayerHit;
            playerState.health.OnPlayerHit += HandlePlayerHit;
            Debug.Log("【系统正常】状态机已接通受击广播，就等挨打了！");
        }
    }
    void Update()
    {
        if (IsGrounded() && currentState != dashState && currentState != jumpState)
        {
            jumpCount = 0; // 修复的跳跃计数器清零
        }


        //Relay
        if (IsGrounded() && currentState != dashState && currentState != jumpState)
        {
            jumpCount = 0;
            hasJumpAnchor = false; // 落地销毁锚点
        }

        // 后台超时监控
        if (currentState != dashState)
        {
            dashSkill.UpdateTimeout();
        }

        if (currentState != slideState)
        {
            slideSkill.UpdateTimeout();
        }


        // 锚点图片自动销毁
        // 1. 如果冲刺连段归零（无论是因为超时，还是因为你传送完结算了）
        if (dashSkill.currentCombo == 0 && activeDashAnchor != null)Destroy(activeDashAnchor);
        // 2. 如果滑铲连段归零
        if (slideSkill.currentCombo == 0 && activeSlideAnchor != null) Destroy(activeSlideAnchor);
        // 3. 如果跳跃锚点开关被关闭（落地了，或者在空中传送用掉了）
        if (!hasJumpAnchor && activeJumpAnchor != null) Destroy(activeJumpAnchor);


        // 每一帧都让当前的卡带运行
        if (currentState != null)currentState.Update();
    }

    public void ChangeState(IState newState)
    {
        Debug.Log("切换中");
        if (currentState != null)
        {
            currentState.Exit();
        }

        currentState = newState;
        currentState.Enter();
    }

    // ==========================================
    //攻击
    // 1. 由动画事件调用：在挥剑结束的瞬间触发，开启派生窗口
    public void OpenComboWindow()
    {
        if (currentState == comboState)
        {
            comboState.isCancelable = true;

            // 顺便做一次立刻结算：万一玩家在这个事件触发前0.1秒按了键，立刻生效！
            GetComponent<ComboInputBuffer>().TryAdvanceCombo();
        }
    }

    // 2. 由动画事件调用：在收招即将结束时触发，关闭强制打断
    public void CloseComboWindow()
    {
        if (currentState == comboState)
        {
            comboState.isCancelable = false;
        }
    }

    // 3. 由动画事件调用：动画彻底播完了
    // 由动画事件调用：动画彻底播完了
    public void OnAttackAnimationEnd()
    {
        if (currentState == comboState)
        {
            ComboInputBuffer buffer = GetComponent<ComboInputBuffer>();
            buffer.StartGracePeriod();

            ComboNode lastNode = buffer.currentNode;
            bool isCurrentButtonHeld = false;

            if (lastNode != null)
            {
                // ==========================================
                // 侦探代码开始：监听所有核心数据
                // ==========================================
                Debug.Log($"[查案] 1. 当前刚放完的技能是: {lastNode.nodeName}");
                Debug.Log($"[查案] 2. 该技能配置里是否包含主攻击(Main): {lastNode.inputSequence.Contains(InputCmd.MainAttack)}");
                Debug.Log($"[查案] 3. 该技能配置里是否包含副攻击(Sub): {lastNode.inputSequence.Contains(InputCmd.SubAttack)}");
                Debug.Log($"[查案] 4. 控制器里的左键状态(isMainHeld): {playerController.isMainAttackHeld}");
                Debug.Log($"[查案] 5. 控制器里的右键状态(isSubHeld): {playerController.isSubAttackHeld}");
                // ==========================================

                // 根据刚放完的技能类型，去查对应的按键
                if (lastNode.inputSequence.Contains(InputCmd.MainAttack))
                {
                    isCurrentButtonHeld = playerController.isMainAttackHeld;
                }
                else if (lastNode.inputSequence.Contains(InputCmd.SubAttack))
                {
                    isCurrentButtonHeld = playerController.isSubAttackHeld;
                }
            }

            Debug.Log($"[查案] 6. 最终判定是否切入蓄力(isCurrentButtonHeld): {isCurrentButtonHeld}");

            // 核心流转
            if (isCurrentButtonHeld)
            {
                ChangeState(chargeState);
            }
            else if (Mathf.Abs(playerController.moveInput.x) > 0.1f)
            {
                ChangeState(runState);
            }
            else
            {
                ChangeState(idleState);
            }
        }
    }

    /*public void OnAttackAnimationEnd()
    {
        if (currentState == comboState)
        {
            ComboInputBuffer buffer = GetComponent<ComboInputBuffer>();
            buffer.StartGracePeriod();

            ComboNode lastNode = buffer.currentNode;
            bool isCurrentButtonHeld = false;

            if (lastNode != null)
            {
                if (lastNode.inputSequence.Contains(InputCmd.MainAttack))
                {
                    isCurrentButtonHeld = playerController.isMainAttackHeld;
                }
                else if (lastNode.inputSequence.Contains(InputCmd.SubAttack)) 
                {
                    isCurrentButtonHeld = playerController.isSubAttackHeld;
                }
            }

            if (isCurrentButtonHeld)
            {
                ChangeState(chargeState);
            }
            else if (Mathf.Abs(playerController.moveInput.x) > 0.1f)
            {
                ChangeState(runState);
            }
            else
            {
                ChangeState(idleState);
            }
        }
    }*/

    // ==========================================
    //远程

    public void SpawnProjectile()
    {
        if (projectilePrefab != null && firePoint != null)
        {
            Vector2 shootDirection = Vector2.zero;
            Vector2 inputDir = playerController.moveInput;

            // 1. 如果有按键输入，进行【八向吸附】
            if (inputDir.magnitude > 0.1f)
            {
                // 将输入向量转换为角度，然后吸附到 45 度的倍数
                float angle = Mathf.Atan2(inputDir.y, inputDir.x) * Mathf.Rad2Deg;
                angle = Mathf.Round(angle / 45f) * 45f;

                shootDirection = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));

                // 顺便同步转身 (如果是往左后方打，身体要转过去)
                if (shootDirection.x < -0.1f) playerController.SetFacingDirection(-1);
                else if (shootDirection.x > 0.1f) playerController.SetFacingDirection(1);
            }
            else
            {
                // 2. 如果没按方向键，默认朝正前方射击
                shootDirection = new Vector2(playerController.facingDirection, 0f);
            }

            // 生成子弹
            GameObject bullet = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
            Player_Projectile projScript = bullet.GetComponent<Player_Projectile>();
            if (projScript != null) projScript.Setup(shootDirection);
        }
    }

    // ==========================================
    //击飞
    private void HandlePlayerHit(Vector2 knockbackDirection)
    {
        hitState.SetKnockbackForce(knockbackDirection);
        ChangeState(hitState);

        // 2. 让 Controller 接管画面：开启闪烁（闪烁的时间与无敌时间严格一致）
        playerController.StartBlink(playerState.health.invulnerableDuration, blinkInterval);
    }

    void OnDestroy()
    {
        if (playerState != null && playerState.health != null)
        {
            playerState.health.OnPlayerHit -= HandlePlayerHit;
        }
    }



    // ==========================================
    //检测
    public bool IsGrounded()
    {
        if (groundCheck == null) return false;

        // 参数解释：中心点, 盒子大小(宽, 高), 旋转角度, 方向, 距离, 检测层
        // 这里我们向下发射一个高度为 0.2f 的小盒子
        RaycastHit2D hit = Physics2D.BoxCast(groundCheck.position, new Vector2(0.5f, 0.2f), 0f, Vector2.down, 0.1f, groundLayer);

        return hit.collider != null;
    }

    public void SetColliderHeight(bool isCrouching)
    {
        if (coll == null) return;

        if (isCrouching)
        {
            // --- 1. 处理原有的物理大框 ---
            float crouchHeightMultiplier = 0.6f;
            coll.size = new Vector2(originalColliderSize.x, originalColliderSize.y * crouchHeightMultiplier);
            float heightDifference = originalColliderSize.y - coll.size.y;
            coll.offset = new Vector2(originalColliderOffset.x, originalColliderOffset.y - (heightDifference * 0.5f));

            // --- 2. 新增：处理受击小框 (Hurtbox) ---
            if (hurtboxCore != null)
            {
                // 让小蓝点跟着物理框一起按比例下降
                hurtboxCore.localPosition = new Vector3(
                    originalHurtboxPos.x,
                    originalHurtboxPos.y - (heightDifference * 0.5f),
                    originalHurtboxPos.z
                );
            }
        }
        else
        {
            // --- 站立时：百分之百完美恢复 ---
            coll.size = originalColliderSize;
            coll.offset = originalColliderOffset;

            if (hurtboxCore != null)
            {
                hurtboxCore.localPosition = originalHurtboxPos; // 恢复小蓝点位置
            }
        }
    }

    public bool CanStand()
    {
        if (ceilingCheck == null) return true; // 如果没挂雷达，默认允许站起

        // 向上发射一个薄薄的盒子射线，检查是否有 Ground 层的障碍物
        return !Physics2D.BoxCast(ceilingCheck.position, new Vector2(0.4f, 0.1f), 0f, Vector2.up, 0.1f, groundLayer);
    }

}