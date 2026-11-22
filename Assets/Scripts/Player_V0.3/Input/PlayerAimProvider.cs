using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>瞄准来源</summary>
    public enum AimSource
    {
        /// <summary>鼠标位置</summary>
        Mouse,

        /// <summary>方向键八向吸附（原有行为）</summary>
        EightWay,

        /// <summary>自动：有鼠标输入就用鼠标，否则回退到八向（手柄玩家）</summary>
        Auto,
    }

    /// <summary>攻击时角色朝向的策略</summary>
    public enum FacingPolicy
    {
        /// <summary>朝向只跟方向键，攻击不改变朝向</summary>
        MovementOnly,

        /// <summary>攻击时转向瞄准方向，平时跟方向键（推荐）</summary>
        AimOnAttack,

        /// <summary>永远面朝瞄准方向（双摇杆射击风格）</summary>
        AlwaysAim,
    }

    /// <summary>
    /// 【瞄准提供者】—— 回答"玩家现在瞄向哪里"。
    ///
    /// ==========================================================
    /// 【为什么单独一个组件】
    ///
    /// 瞄准这件事现在有三个消费方：远程发射器、激光、以及朝向系统。
    /// 如果让每一方各自去算"鼠标在世界坐标的哪里"，
    /// 那么以后想加"辅助瞄准""锁定敌人""手柄右摇杆瞄准"时要改三处。
    ///
    /// 集中在这里之后，那些扩展都只改这一个文件。
    ///
    /// 比喻：从"每个人各自抬头看天猜方向"，改成"墙上挂一个统一的罗盘"。
    /// ==========================================================
    ///
    /// 【手柄降级】检测不到鼠标移动时自动回退到方向键八向吸附，
    /// 所以手柄玩家不会因为这次改动完全失去瞄准能力。
    /// </summary>
    public class PlayerAimProvider : MonoBehaviour
    {
        [Header("瞄准来源")]
        public AimSource aimSource = AimSource.Auto;

        // ==========================================================
        // 【已删除】mouseIdleTimeout —— "鼠标多久没动就算玩家换手柄了"。
        //
        // 它想回答的问题是对的（玩家现在用鼠标还是手柄），但用错了信号：
        // "鼠标没动"既可能是换了手柄，也可能是瞄好了在等时机 ——
        // 一个信号区分不了两件事，所以无论超时填多久都必然会误判。
        // 填 2 秒时子弹两秒后就跑偏，提到 10 秒只是让它更难复现，病没治。
        //
        // 现在改问 PlayerController.isUsingGamepad —— 那是记录下来的事实
        // （谁最后动过就是谁在用），不是靠沉默时长猜的。停着不动时维持
        // 上一个结论，所以"瞄好了不动"这个常态永远不会被误判。
        // ==========================================================

        [Header("朝向策略")]
        [Tooltip(
            "MovementOnly = 朝向只跟方向键\n" +
            "AimOnAttack  = 攻击时转向瞄准方向，平时跟方向键（推荐）\n" +
            "AlwaysAim    = 永远面朝瞄准方向（双摇杆射击风格）")]
        public FacingPolicy facingPolicy = FacingPolicy.AimOnAttack;

        [Header("引用")]
        [Tooltip("用于屏幕坐标转世界坐标。留空则自动取 Camera.main")]
        public Camera aimCamera;

        [Tooltip("瞄准起点。留空则用角色本体位置。一般拖枪口 FirePoint")]
        public Transform aimOrigin;

        [Header("Debug")]
        public bool drawGizmos = false;

        private PlayerController controller;
        private Camera cachedCamera;

        private Vector2 cachedAimDirection = Vector2.right;
        private Vector3 cachedAimWorldPoint;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
        }

        private Camera Cam
        {
            get
            {
                if (aimCamera != null) return aimCamera;
                if (cachedCamera == null) cachedCamera = Camera.main;
                return cachedCamera;
            }
        }

        private Vector3 Origin
            => aimOrigin != null ? aimOrigin.position : transform.position;

        private void Update()
        {
            // 【已删除】TrackMouseActivity —— 鼠标活跃度现在由 PlayerController
            // 在 PollPointer 里记录（见 isUsingGamepad），本组件不再自己追踪。
            Recalculate();

            if (facingPolicy == FacingPolicy.AlwaysAim) ApplyFacing();
        }

        // ==========================================================
        // 对外接口
        // ==========================================================

        /// <summary>当前瞄准方向（已归一化）</summary>
        public Vector2 AimDirection => cachedAimDirection;

        /// <summary>鼠标在世界坐标中的位置。八向模式下是从角色沿瞄准方向推出的一个点</summary>
        public Vector3 AimWorldPoint => cachedAimWorldPoint;

        /// <summary>当前是否在用鼠标瞄准（用于 UI 显示准星等）</summary>
        public bool IsUsingMouse => ResolveEffectiveSource() == AimSource.Mouse;

        /// <summary>
        /// 【不降级的指针方向】只要这台机器上有鼠标，就一定返回鼠标方向。
        ///
        /// ==========================================================
        /// 【为什么需要一条绕开 Auto 的路】
        ///
        /// AimDirection 走 Auto 时有一条隐式规则：鼠标 2 秒没动 → 判定玩家换了手柄
        /// → 改用方向键八向 → 而方向键没按时 ResolveEightWay 回退到【角色朝向】。
        ///
        /// 对持续瞄准（射击、激光）这套降级是合理的。但对 AA1/BB1 的位移方向
        /// 是灾难：蓄力时玩家瞄好了不动本来就是常态，两秒一到就被判成手柄，
        /// 于是松手那一刻的位移方向变成了"角色正面"——
        /// 表现出来就是「四向/八向几乎永远朝右」，而且复不复现取决于
        /// 你刚才动没动鼠标，看起来毫无规律。
        ///
        /// 比喻：柜台两秒没人说话就默认改说另一种语言。
        ///       聊天时还能纠正回来，但如果这时你只说一句话就走，那句必然被听错。
        ///
        /// 位移方向是【一次性决策】，没有"下一帧纠正回来"的机会，
        /// 所以它必须问一个不会自作主张的来源。
        /// ==========================================================
        /// </summary>
        /// <returns>拿到了鼠标方向返回 true；真的没有鼠标（纯手柄）返回 false</returns>
        public bool TryGetPointerDirection(out Vector2 dir)
        {
            dir = Vector2.zero;

            if (!TryGetPointerWorldPoint(out Vector3 world)) return false;

            Vector2 delta = (Vector2)(world - Origin);

            // 鼠标压在角色身上时方向没有意义
            if (delta.sqrMagnitude < 0.0004f) return false;

            dir = delta.normalized;
            return true;
        }

        /// <summary>
        /// 【不降级的指针落点】鼠标在世界坐标里的位置。
        ///
        /// 与 AimWorldPoint 的区别：那个在降级到八向时返回的是
        /// 「从角色沿方向推 5 格」的假点，只能表达方向、不能表达距离。
        /// 空中蓄力冲刺要「冲到鼠标那里」，需要真实落点，所以走这条路。
        /// </summary>
        public bool TryGetPointerWorldPoint(out Vector3 worldPoint)
        {
            worldPoint = Origin;

            if (controller == null || !controller.hasPointerDevice || Cam == null) return false;

            Vector3 screen = controller.screenAimPosition;
            screen.z = Mathf.Abs(Cam.transform.position.z - Origin.z);

            worldPoint = Cam.ScreenToWorldPoint(screen);
            worldPoint.z = Origin.z;
            return true;
        }

        /// <summary>
        /// 由攻击方在出招瞬间调用：按策略把角色转向瞄准方向。
        /// MovementOnly 策略下是空操作。
        /// </summary>
        public void FaceAimIfNeeded()
        {
            if (facingPolicy == FacingPolicy.MovementOnly) return;
            ApplyFacing();
        }

        // ==========================================================
        // 内部
        // ==========================================================

        /// <summary>
        /// Auto 模式下，这一帧到底按鼠标还是按八向算。
        ///
        /// 两道判断，都是【事实】而不是推测：
        ///   ① 这台机器上有没有鼠标 —— 纯手柄机器只能走八向
        ///   ② 玩家最后动的是鼠标还是手柄 —— 由 PlayerController 记录
        ///
        /// 关键差别是没有任何"超时"：玩家把鼠标停住瞄准时，
        /// 结论维持在"用鼠标"不变，不会自己漂移到八向去。
        /// 原先那条隐式降级正是子弹会突然朝角色正面飞的病根（见文档 6.6 同类）。
        /// </summary>
        private AimSource ResolveEffectiveSource()
        {
            if (aimSource != AimSource.Auto) return aimSource;

            if (controller == null || !controller.hasPointerDevice) return AimSource.EightWay;

            return controller.isUsingGamepad ? AimSource.EightWay : AimSource.Mouse;
        }

        private void Recalculate()
        {
            if (ResolveEffectiveSource() == AimSource.Mouse && Cam != null && controller != null)
            {
                Vector3 screen = controller.screenAimPosition;

                // 2D 正交相机：z 传相机到角色平面的距离
                screen.z = Mathf.Abs(Cam.transform.position.z - Origin.z);

                Vector3 world = Cam.ScreenToWorldPoint(screen);
                world.z = Origin.z;

                cachedAimWorldPoint = world;

                Vector2 delta = (Vector2)(world - Origin);

                // 鼠标压在角色身上时方向没有意义，维持上一帧，避免疯狂抖动
                if (delta.sqrMagnitude > 0.0004f)
                {
                    cachedAimDirection = delta.normalized;
                }
                return;
            }

            // ---- 八向吸附回退 ----
            cachedAimDirection = ResolveEightWay();
            cachedAimWorldPoint = Origin + (Vector3)(cachedAimDirection * 5f);
        }

        private Vector2 ResolveEightWay()
        {
            if (controller == null) return Vector2.right;

            Vector2 input = controller.moveInput;

            if (input.magnitude <= 0.1f)
            {
                return new Vector2(controller.facingDirection, 0f);
            }

            float angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;
            angle = Mathf.Round(angle / 45f) * 45f;

            return new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad),
                Mathf.Sin(angle * Mathf.Deg2Rad));
        }

        private void ApplyFacing()
        {
            if (controller == null) return;

            // 接近垂直时不翻转，避免鼠标在头顶来回微动导致角色左右抽搐
            if (Mathf.Abs(cachedAimDirection.x) < 0.15f) return;

            controller.SetFacingDirection(cachedAimDirection.x > 0f ? 1 : -1);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !Application.isPlaying) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(Origin, Origin + (Vector3)(cachedAimDirection * 3f));
            Gizmos.DrawWireSphere(cachedAimWorldPoint, 0.2f);
        }
    }
}
