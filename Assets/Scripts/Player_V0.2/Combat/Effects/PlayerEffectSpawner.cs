using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【特效生成器】—— 挂在角色身上，和 Animator 同一个 GameObject。
    ///
    /// 只做转交：招式特效、蓄力光效都交给场景里的 EffectManager 查表播放。
    /// 特效配在 EffectManager 的特效表里（预制体 + 参数），不再走对象池。
    /// </summary>
    public class PlayerEffectSpawner : MonoBehaviour
    {
        [Header("挂点")]
        [Tooltip("特效生成后挂在谁下面。留空则挂在本物体上。\n" +
                 "建议单独建一个空的 EffectRoot 子物体，方便统一调整偏移。")]
        public Transform effectRoot;

        [Header("Debug")]
        public bool verboseLog = false;

        private PlayerChargeSystem chargeSystem;
        private WeaponLoadout weapons;

        // ==========================================================
        // 蓄力光效：【每只手各存一份】
        // ==========================================================
        private readonly GameObject[] activeChargeEffects = new GameObject[2];
        private readonly int[] activeChargeLevels = new int[2];

        // 记下是第几次播放，收回时核对（防止收掉已被别人复用的物体）
        private readonly int[] activeChargePlayIds = new int[2];

        private Transform Root => effectRoot != null ? effectRoot : transform;

        private bool warnedNoManager;

        private void Awake()
        {
            chargeSystem = GetComponent<PlayerChargeSystem>();
            weapons = GetComponent<WeaponLoadout>();
        }

        private void OnEnable()
        {
            // PlayerChargeSystem 是同物体上的组件，Awake 顺序不保证，所以这里可能还没拿到。
            // OnEnable / Start 两边都试，用 subscribed 挡住重复订阅。
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (chargeSystem != null && subscribed)
            {
                chargeSystem.OnChargeLevelChanged -= HandleChargeLevelChanged;
            }
            subscribed = false;   // 重新启用时要能再订阅上

            // 组件被关掉时别把常驻特效落在场景里
            ClearAllChargeEffects();
        }

        private bool subscribed;

        private void TrySubscribe()
        {
            if (subscribed) return;
            if (chargeSystem == null) return;

            chargeSystem.OnChargeLevelChanged += HandleChargeLevelChanged;
            subscribed = true;
        }

        /// <summary>场景里的 EffectManager；没有时只警告一次</summary>
        private EffectManager Manager
        {
            get
            {
                EffectManager m = EffectManager.Instance;
                if (m == null && !warnedNoManager)
                {
                    warnedNoManager = true;
                    PlayerLog.Warn("[特效] 场景里没有 EffectManager，所有角色特效都不会播放。" +
                                   "请建一个空物体挂上 Effect Manager 组件。", this);
                }
                return m;
            }
        }

        // ==========================================================
        // 1. 攻击特效
        // ==========================================================

        /// <summary>
<<<<<<< Updated upstream
        /// 由动画事件调用：播放【当前招式】配置的特效。
        ///
        /// 在攻击动画里该出刀光的那一帧插一个 Animation Event，
        /// Function 选 PlayEffect，不需要参数。
        /// </summary>
        public void PlayEffect()
        {
            ComboNode node = buffer != null ? buffer.currentNode : null;

            if (node == null)
            {
                if (verboseLog) Debug.Log("[特效] PlayEffect 触发时没有当前招式，已跳过");
                return;
            }

            string key = node.effectKey;

            if (string.IsNullOrEmpty(key)) key = defaultAttackEffectKey;
            if (string.IsNullOrEmpty(key)) return;

            float life = node.effectLifetime > 0f ? node.effectLifetime : defaultEffectLifetime;

            Spawn(key, node.GetEffectOffset(), life, persistent: false,
                  node.effectScale, node.effectRotation);
        }

        /// <summary>
        /// 由动画事件调用：播放指定 key 的特效。
        /// 一招需要多个特效时用这个 —— Animation Event 的 String 参数填 key。
        /// </summary>
        public void PlayEffectByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            Vector2 offset = Vector2.zero;
            Vector2 scale = Vector2.one;
            float rot = 0f;

            ComboNode node = buffer != null ? buffer.currentNode : null;
            if (node != null)
            {
                offset = node.GetEffectOffset();
                scale = node.effectScale;
                rot = node.effectRotation;
            }

            Spawn(key, offset, defaultEffectLifetime, persistent: false, scale, rot);
=======
        /// 【空操作】动画事件 PlayEffect 的接收方法。
        /// 特效由 AttackTimelineRunner 按时间表调用 SpawnAttackEffect —— 时间点仍然取自动画里的这个事件，
        /// 所以动画上的事件【不要删】。保留本方法只是为了让 Unity 不报 "has no receiver"。
        /// </summary>
        public void PlayEffect() { }

        /// <summary>【空操作】同上，改由时间表调用 SpawnEffectByKey</summary>
        public void PlayEffectByKey(string key) { }

        /// <summary>
        /// 【空操作】"特效结束"时间戳。时间表出招时读它的时间，和前面的 PlayEffect / PlayEffectByKey 配对，
        /// 算出特效该活多久（只对存活模式为 Until Marker 的特效生效）。
        /// 字符串参数：留空 = 配 PlayEffect；填名字 = 配同名的 PlayEffectByKey。
        /// 保留本方法只是为了让 Unity 不报 "has no receiver"。
        /// </summary>
        public void EffectEnd(string key) { }

        /// <summary>播放这个招式在特效表里配的特效。返回生成的特效物体（没配则 null）</summary>
        /// <param name="activeRemain">距离判定段结束还有多久（没有攻击实例时传 -1）</param>
        /// <param name="attackRemain">距离整招结束还有多久（同上）</param>
        /// <param name="markerLife">距离配对的 EffectEnd 还有多久（没有则 -1）</param>
        public GameObject SpawnAttackEffect(ComboNode node, float activeRemain = -1f, float attackRemain = -1f,
            float markerLife = -1f)
        {
            if (node == null) return null;
            EffectManager m = Manager;
            return m != null ? m.Play(node, Root, activeRemain, attackRemain, markerLife) : null;
        }

        /// <summary>按名字播放（一招多个特效时用，对应动画事件 PlayEffectByKey 的 String 参数）</summary>
        public GameObject SpawnEffectByKey(string key, ComboNode node, float activeRemain = -1f, float attackRemain = -1f,
            float markerLife = -1f)
        {
            if (string.IsNullOrEmpty(key)) return null;
            EffectManager m = Manager;
            return m != null ? m.PlayByKey(key, node, Root, activeRemain, attackRemain, markerLife) : null;
>>>>>>> Stashed changes
        }

        // ==========================================================
        // 2. 蓄力光效（等级事件驱动）
        // ==========================================================

        private void HandleChargeLevelChanged(WeaponSlot slot, int level)
        {
            int i = (int)slot;

            // 等级没变就不折腾（避免每帧重建特效）
            if (level == activeChargeLevels[i]) return;

            ClearChargeEffect(slot);
            activeChargeLevels[i] = level;

            if (level <= 0) return;

            // 蓄的是哪把武器？查它这一级的光效
            WeaponMoveSet weapon = weapons != null ? weapons.GetWeapon(slot) : null;
            if (weapon == null) return;

            EffectManager m = Manager;
            if (m == null) return;

            // 常驻：一直挂着直到升级换掉、或松手清掉
            GameObject fx = m.PlayCharge(weapon, level, Root);
            if (fx == null) return;

            activeChargeEffects[i] = fx;
            activeChargePlayIds[i] = fx.GetComponent<ManagedEffect>().PlayId;

            if (verboseLog) PlayerLog.Info($"[特效] {slot} 蓄力 {level} 级光效：{fx.name}");
        }

        /// <summary>收回指定手的蓄力光效。松手、受击、换武器时都要调</summary>
        public void ClearChargeEffect(WeaponSlot slot)
        {
            int i = (int)slot;

            if (activeChargeEffects[i] != null
                && activeChargeEffects[i].TryGetComponent(out ManagedEffect me))
            {
                EffectManager.Instance?.Stop(me, activeChargePlayIds[i]);
            }

            activeChargeEffects[i] = null;
            activeChargeLevels[i] = 0;
        }

        /// <summary>收回两只手的蓄力光效</summary>
        public void ClearAllChargeEffects()
        {
            ClearChargeEffect(WeaponSlot.Main);
            ClearChargeEffect(WeaponSlot.Sub);
        }
    }
}