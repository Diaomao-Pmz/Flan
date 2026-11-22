using System.Collections.Generic;
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

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            comboBuffer = GetComponent<ComboInputBuffer>();
            aim = GetComponent<PlayerAimProvider>();
        }

        // ==========================================================
        // 动画事件入口（已改为空操作）
        // ==========================================================

        /// <summary>
        /// 【已改为空操作】原先由动画事件触发发射。
        /// 现在由 AttackTimelineRunner 按时间表调用 BeginBurst —— 时间点仍然取自动画里的这个事件，
        /// 所以动画上的事件【不要删】。保留本方法只是为了让 Unity 不报 "has no receiver"。
        /// </summary>
        public void SpawnProjectile() { }

        // ==========================================================
        // 连射（由时间表驱动）
        // ==========================================================

        /// <summary>
        /// 打出一招配置的一整轮弹幕。
        /// 单发 / 零间隔会立刻同步射完并返回 null；否则返回一份 BurstRun，由调用方每帧 TickBurst。
        /// </summary>
        /// <param name="allowFace">这一招还是不是当前动画的主人。不是的话开火不再让角色转向</param>
        /// <param name="bulletSink">射出的子弹记到这里（R5 撤掉整发时回收用），可为 null</param>
        public BurstRun BeginBurst(ComboNode node, WeaponMoveSet weapon, bool allowFace, List<GameObject> bulletSink)
        {
            if (firePoint == null)
            {
                Debug.LogWarning("[发射器] 未指定枪口位置 firePoint，本次发射已跳过。", this);
                return null;
            }

            string poolKey = ResolvePoolKey(node, weapon);
            if (string.IsNullOrEmpty(poolKey))
            {
                Debug.LogWarning(
                    "[发射器] 没找到可用的子弹池 key，本次发射已跳过。\n" +
                    "请在武器资产上填 Projectile Pool Key，或在招式节点上填覆盖值。", this);
                return null;
            }

            int count = node != null ? Mathf.Max(1, node.projectileCount) : 1;
            float interval = node != null ? node.projectileInterval : 0f;

            Vector2 dir = ResolveDirection(weapon, allowFace);

            // 单发或零间隔 → 同步射完
            if (count == 1 || interval <= 0f)
            {
                for (int i = 0; i < count; i++) Track(bulletSink, FireOne(poolKey, node, dir, i));
                return null;
            }

            BurstRun run = new BurstRun
            {
                node = node,
                weapon = weapon,
                poolKey = poolKey,
                count = count,
                interval = interval,
                dir = dir,
                recomputeAim = node == null || node.recomputeAimPerShot,
            };

            // 第一发立刻射出
            Track(bulletSink, FireOne(poolKey, node, dir, 0));
            run.fired = 1;

            activeBursts.Add(run);
            return run;
        }

        /// <summary>推进一轮连射</summary>
        public void TickBurst(BurstRun run, float dt, bool allowFace, List<GameObject> bulletSink)
        {
            if (run == null || run.done) return;

            run.timer += dt;

            while (run.fired < run.count && run.timer >= run.interval)
            {
                run.timer -= run.interval;

                if (run.recomputeAim) run.dir = ResolveDirection(run.weapon, allowFace);

                Track(bulletSink, FireOne(run.poolKey, run.node, run.dir, run.fired));
                run.fired++;
            }

            if (run.fired >= run.count) EndBurst(run);
        }

        /// <summary>结束一轮连射</summary>
        public void EndBurst(BurstRun run)
        {
            if (run == null) return;
            run.done = true;
            activeBursts.Remove(run);
        }

        /// <summary>中断所有连射</summary>
        public void CancelBurst()
        {
            for (int i = 0; i < activeBursts.Count; i++) activeBursts[i].done = true;
            activeBursts.Clear();
        }

        private static void Track(List<GameObject> sink, GameObject go)
        {
            if (sink != null && go != null) sink.Add(go);
        }

        // ==========================================================
        // 单发
        // ==========================================================

        /// <param name="shotIndex">这一轮里的第几颗（从 0 数），用来分子弹总伤害</param>
        private GameObject FireOne(string poolKey, ComboNode node, Vector2 direction, int shotIndex)
        {
            // key 未注册时池会自己报 LogError，这里拿到 null 就静默跳过避免刷屏
            GameObject bullet = ObjectPoolManager.Instance?.Get(poolKey);
            if (bullet == null) return null;

            bullet.transform.position = firePoint.position;

            Player_Projectile proj = bullet.GetComponent<Player_Projectile>();
            if (proj == null)
            {
                Debug.LogWarning($"[发射器] 池「{poolKey}」取出的对象上没有 Player_Projectile。", bullet);
                return null;
            }

            // 先套用倍率与覆盖，再 Setup 定方向。
            // 这些改动会在子弹回池时由 OnDespawn 还原，不会污染池子。
            if (node != null)
            {
                proj.Configure(node.projectileSpeedMultiplier, node.projectileScaleMultiplier, 0);

                // 子弹总伤害按发数平分；没填就用预制体伤害
                int shotDamage = node.ProjectileDamageFor(shotIndex);
                if (shotDamage >= 0) proj.SetDamage(shotDamage);
                proj.SetCancelsBullets(node.CancelsBulletsBy(BulletCancelMode.Projectile));
            }

            proj.Setup(direction);

            if (verboseLog) PlayerLog.Info($"[发射器] 射出 {poolKey}，方向 {direction}");
            return bullet;
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
        private Vector2 ResolveDirection(WeaponMoveSet weapon, bool allowFace = true)
        {
            if (aim != null)
            {
                // 开火时按策略转向瞄准方向（默认策略：攻击时转，平时不转）。
                // 动画已被别的招顶掉、只剩逻辑在跑的连射不转向 —— 否则会把新招的朝向抢走
                if (allowFace) aim.FaceAimIfNeeded();
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

            if (allowFace)
            {
                if (dir.x < -0.1f) controller.SetFacingDirection(-1);
                else if (dir.x > 0.1f) controller.SetFacingDirection(1);
            }

            return dir;
        }
    }
}
