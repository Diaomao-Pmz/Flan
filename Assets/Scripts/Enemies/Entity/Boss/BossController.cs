using TMPro;
using UnityEngine;

// 继承 EntityBase，自动获得了血量、受伤和死亡的逻辑
public class BossController : EntityBase
{
    public Animator animator { get; private set; }
    [SerializeField] Transform playerTransform;

    // 你的各个组件
    public BossAIDecider AI { get; private set; }
    public BossCombatState CombatState { get; private set; }
    public BossBulletEmitter BulletEmitter { get; private set; }

    [Header("UI For Testing")]
    public TextMeshProUGUI bossStatusText;

    public IState CurrentState { get; private set; }

    // 黑板数据
    public float DistanceToPlayer => Vector2.Distance(transform.position, playerTransform.position);

    // 注意这里要用 override 并且调用 base.Awake()
    protected override void Awake()
    {
        base.Awake(); // 初始化基类的血量

        animator = GetComponent<Animator>();
        AI = GetComponent<BossAIDecider>();
        BulletEmitter = GetComponent<BossBulletEmitter>();

        CombatState = new(this);
    }

    void Start()
    {
        if (BulletEmitter != null) BulletEmitter.Init(playerTransform);
        ChangeState(CombatState);
    }

    void Update()
    {
        if (isDead) return; // 如果死了，状态机直接停转
        CurrentState?.Update();
    }

    public void ChangeState(IState state)
    {
        if (CurrentState != null) CurrentState.Exit();
        CurrentState = state;
        CurrentState?.Enter();
    }

    // 重写 Boss 专属的死亡逻辑（比如播放慢动作、大爆炸、爆极品装备）
    protected override void Die()
    {
        Debug.Log("Boss被击败了！触发二阶段或者死亡演出！");
        if (CurrentState != null) CurrentState.Exit();
        // 播放死亡动画，禁用碰撞体等...
        // Destroy(gameObject, 3f); // 延迟销毁
    }
}