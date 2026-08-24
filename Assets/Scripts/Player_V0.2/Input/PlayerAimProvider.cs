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

        //[Tooltip("鼠标多久没动就判定为"玩家在用手柄"，回退到八向吸附（秒）")]
        public float mouseIdleTimeout = 2f;

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

        private Vector2 lastScreenPos;
        private float lastMouseMoveTime = -999f;

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
            TrackMouseActivity();
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

        private void TrackMouseActivity()
        {
            if (controller == null) return;

            Vector2 screenPos = controller.screenAimPosition;

            if ((screenPos - lastScreenPos).sqrMagnitude > 1f)
            {
                lastScreenPos = screenPos;
                lastMouseMoveTime = Time.unscaledTime;
            }
        }

        private AimSource ResolveEffectiveSource()
        {
            if (aimSource != AimSource.Auto) return aimSource;

            // 鼠标最近动过 → 认为玩家在用鼠标；否则回退八向（手柄）
            bool mouseRecentlyActive =
                (Time.unscaledTime - lastMouseMoveTime) < mouseIdleTimeout;

            return mouseRecentlyActive ? AimSource.Mouse : AimSource.EightWay;
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
