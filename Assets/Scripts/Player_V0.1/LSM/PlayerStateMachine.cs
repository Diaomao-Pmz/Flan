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
    public ComboState comboState;
    public RangeAttackState rangeAttackState;

    public Animator anim;
    public Rigidbody2D rb;
    public TrailRenderer dashTrail;         // 尾迹
    public PlayerController playerController;

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

    //远程
    [Header("Ranged Attack Settings")]
    public GameObject projectilePrefab; // 魔法/子弹的预制体
    public Transform firePoint;

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
        comboState = new ComboState(this);
        rangeAttackState = new RangeAttackState(this);

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

    // ==========================================
    //攻击
    public void OnAttackAnimationEnd()
    {

        // 只有当芙兰确实在攻击状态时，动画播完了才帮她恢复状态
        if (currentState == comboState)
        {
            // 恢复状态的智能化细节：如果播完动画时玩家还按着跑动键，直接无缝切入跑步，否则进待机
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D))
            {
                ChangeState(runState);
            }
            else
            {
                ChangeState(idleState);
            }
        }
    }

    // ==========================================
    //远程
    public void TryEnterRangeAttack()
    {
        // 把以前在 Update 里的判断条件，原封不动搬到这里
        if ((currentState == idleState || currentState == runState) && IsGrounded())
        {
            ChangeState(rangeAttackState);
        }
        else
        {
            Debug.Log("当前状态或未落地，拒绝进入远程攻击");
        }
    }

/*    public void SpawnProjectileEightDirection()
    {
        if (projectilePrefab != null && firePoint != null)
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            GameObject bullet = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
            Projectile projScript = bullet.GetComponent<Projectile>();
            if (projScript != null)
            {
                projScript.Setup(new Vector2(h,v));
            }
        }
    }*/

    public void SpawnProjectile()
    {
        if (projectilePrefab != null && firePoint != null)
        {
            // 1. 获取鼠标的真实世界坐标
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0f;

            // 2. 判定朝向：鼠标在玩家中心点的左边还是右边？
            bool isAimingLeft = mousePos.x < transform.position.x;

            // 3. 翻转贴图
            GetComponent<SpriteRenderer>().flipX = isAimingLeft;

            // 4. 【核心修复】：动态翻转 FirePoint 的位置！
            // 取出当前 FirePoint X坐标的绝对值，然后根据朝向赋予正负号
            float absoluteX = Mathf.Abs(firePoint.localPosition.x);
            firePoint.localPosition = new Vector3(
                isAimingLeft ? -absoluteX : absoluteX,
                firePoint.localPosition.y,
                firePoint.localPosition.z
            );

            // 5. 现在 FirePoint 已经在正确的枪口位置了，计算真正的射击方向
            Vector2 shootDirection = (mousePos - firePoint.position);

            // 6. 生成子弹并输送动力
            GameObject bullet = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
            Projectile projScript = bullet.GetComponent<Projectile>();
            if (projScript != null)
            {
                projScript.Setup(shootDirection);
            }
        }
    }

    public void OnRangeAttackEnd()
    {
        if (currentState == rangeAttackState)
        {
            ChangeState(idleState);
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