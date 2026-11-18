using System.Collections;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【远程发射器】—— 由动画事件调用，走对象池。
    /// </summary>
    public class PlayerWeaponEmitter : MonoBehaviour
    {
        [Header("场景引用")]
        [Tooltip("子弹发射的枪口位置")]
        public Transform firePoint;

        [Header("兜底配置 (未装备武器时使用)")]
        [Tooltip("没有 WeaponLoadout、或武器未配 pool key 时使用的池 key")]
        public string fallbackProjectilePoolKey = "";

        [Tooltip("兜底是否使用八向瞄准")]
        public bool fallbackEightWayAiming = true;

        [Header("Debug")]
        public bool verboseLog = false;

        private PlayerController controller;
        private ComboInputBuffer comboBuffer;
        private PlayerAimProvider aim;

<<<<<<< Updated upstream
        private Coroutine burstRoutine;
=======
        // ==========================================================
        // 【实验：平a/蓄力重做】连射改为「多份并行 + 时间表驱动」
        // ==========================================================

        /// <summary>一轮连射</summary>
        public class BurstRun
        {
            public ComboNode node;
            public WeaponMoveSet weapon;
            public string poolKey;
            public int count;
            public float interval;
            public int fired;
            public float timer;
            public Vector2 dir;
            public bool recomputeAim;
            public bool done;
        }

        private readonly List<BurstRun> activeBursts = new List<BurstRun>();
>>>>>>> Stashed changes

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            comboBuffer = GetComponent<ComboInputBuffer>();
            aim = GetComponent<PlayerAimProvider>();
        }

        // ==========================================================
        // 动画事件入口
        // ==========================================================

        /// <summary>
        /// 由动画事件调用：打出本招配置的一整轮弹幕。
        ///
        /// 单发招式（count = 1）会立刻同步射出，不起协程 ——
        /// 避免为了一发子弹产生一次协程分配。
        /// </summary>
        public void SpawnProjectile()
        {
            if (firePoint == null)
            {
                Debug.LogWarning("[发射器] 未指定枪口位置 firePoint，本次发射已跳过。", this);
                return;
            }

            ComboNode node = comboBuffer != null ? comboBuffer.currentNode : null;
            WeaponMoveSet weapon = comboBuffer != null ? comboBuffer.ActiveWeapon : null;

            string poolKey = ResolvePoolKey(node, weapon);
            if (string.IsNullOrEmpty(poolKey))
            {
                Debug.LogWarning(
                    "[发射器] 没找到可用的子弹池 key，本次发射已跳过。\n" +
                    "请在武器资产上填 Projectile Pool Key，或在招式节点上填覆盖值。", this);
                return;
            }

            int count = node != null ? Mathf.Max(1, node.projectileCount) : 1;
            float interval = node != null ? node.projectileInterval : 0f;

            // 单发或零间隔 → 同步射完，不必起协程
            if (count == 1 || interval <= 0f)
            {
                Vector2 dir = ResolveDirection(weapon);
                for (int i = 0; i < count; i++) FireOne(poolKey, node, dir);
                return;
            }

            // 上一轮还没打完就被新的一轮顶掉（连招推进很快时会发生）
            CancelBurst();
            burstRoutine = StartCoroutine(BurstRoutine(poolKey, node, weapon, count, interval));
        }

        private IEnumerator BurstRoutine(
            string poolKey, ComboNode node, WeaponMoveSet weapon, int count, float interval)
        {
            bool recomputeAim = node == null || node.recomputeAimPerShot;
            Vector2 dir = ResolveDirection(weapon);

            for (int i = 0; i < count; i++)
            {
                if (recomputeAim) dir = ResolveDirection(weapon);

                FireOne(poolKey, node, dir);

                // 最后一发之后不用再等
                if (i < count - 1) yield return new WaitForSeconds(interval);
            }

            burstRoutine = null;
        }

        /// <summary>
        /// 中断连射。招式被打断时必须调用，
        /// 否则玩家已经被击飞了，枪还在原地继续突突。
        /// </summary>
        public void CancelBurst()
        {
            if (burstRoutine != null)
            {
                StopCoroutine(burstRoutine);
                burstRoutine = null;
            }
        }

        // ==========================================================
        // 单发
        // ==========================================================

        private void FireOne(string poolKey, ComboNode node, Vector2 direction)
        {
            // key 未注册时池会自己报 LogError，这里拿到 null 就静默跳过避免刷屏
            GameObject bullet = ObjectPoolManager.Instance?.Get(poolKey);
            if (bullet == null) return;

            bullet.transform.position = firePoint.position;

            Player_Projectile proj = bullet.GetComponent<Player_Projectile>();
            if (proj == null)
            {
                Debug.LogWarning($"[发射器] 池「{poolKey}」取出的对象上没有 Player_Projectile。", bullet);
                return;
            }

            // 先套用倍率与覆盖，再 Setup 定方向。
            // 这些改动会在子弹回池时由 OnDespawn 还原，不会污染池子。
            if (node != null)
            {
                proj.Configure(
                    node.projectileSpeedMultiplier,
                    node.projectileScaleMultiplier,
                    node.projectileDamageOverride);
            }

            proj.Setup(direction);

<<<<<<< Updated upstream
            if (verboseLog) Debug.Log($"[发射器] 射出 {poolKey}，方向 {direction}");
=======
            if (verboseLog) PlayerLog.Info($"[发射器] 射出 {poolKey}，方向 {direction}");
            return bullet;
>>>>>>> Stashed changes
        }

        // ==========================================================
        // 解析
        // ==========================================================

        /// <summary>招式覆盖 &gt; 武器默认 &gt; 组件兜底</summary>
        private string ResolvePoolKey(ComboNode node, WeaponMoveSet weapon)
        {
            if (node != null && !string.IsNullOrEmpty(node.projectilePoolKeyOverride))
                return node.projectilePoolKeyOverride;

            if (weapon != null && weapon.HasProjectile)
                return weapon.projectilePoolKey;

            return fallbackProjectilePoolKey;
        }

        /// <summary>
        /// 瞄准方向统一问 PlayerAimProvider 要 ——
        /// 鼠标瞄准 / 八向回退 / 以后的辅助瞄准都在那一个组件里，
        /// 本文件不需要知道玩家用的是鼠标还是手柄。
        /// </summary>
        private Vector2 ResolveDirection(WeaponMoveSet weapon)
        {
            if (aim != null)
            {
                // 开火时按策略转向瞄准方向（默认策略：攻击时转，平时不转）
                aim.FaceAimIfNeeded();
                return aim.AimDirection;
            }

            // ---- 没挂瞄准组件时的兜底：沿用旧的八向逻辑 ----
            if (controller == null) return Vector2.right;

            bool useEightWay = (weapon != null) ? weapon.useEightWayAiming : fallbackEightWayAiming;
            Vector2 inputDir = controller.moveInput;

            if (!useEightWay || inputDir.magnitude <= 0.1f)
            {
                return new Vector2(controller.facingDirection, 0f);
            }

            float angle = Mathf.Atan2(inputDir.y, inputDir.x) * Mathf.Rad2Deg;
            angle = Mathf.Round(angle / 45f) * 45f;

            Vector2 dir = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad),
                Mathf.Sin(angle * Mathf.Deg2Rad));

            if (dir.x < -0.1f) controller.SetFacingDirection(-1);
            else if (dir.x > 0.1f) controller.SetFacingDirection(1);

            return dir;
        }
    }
}
