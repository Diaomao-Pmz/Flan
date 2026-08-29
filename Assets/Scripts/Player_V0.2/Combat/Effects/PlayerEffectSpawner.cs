using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【特效生成器】—— 挂在角色身上，和 Animator 同一个 GameObject。
    ///
    /// ==========================================================
    /// 两条完全不同的特效来源：
    ///
    /// 1. 攻击特效 —— 由【动画事件】驱动
    ///    动画里在该出特效的帧插一个 PlayEffect 事件，
    ///    生成器读当前招式的 effectKey，从池里取，播完自动回池。
    ///    特效跟着招式数据走，不再跟着动画曲线走 ——
    ///    所以三段普攻可以共用同一个动画剪辑，特效照样不同。
    ///
    /// 2. 蓄力特效 —— 由【蓄力等级事件】驱动
    ///    订阅 ChargeState.OnChargeLevelChanged，
    ///    升到 1 级换 1 级光效、升到 2 级换 2 级，松手全部收掉。
    ///
    ///    蓄力这条【不能】用动画曲线做：
    ///    AA3 之后玩家可以无限期举着，逐帧曲线表达不了"循环直到松手"。
    ///    蓄力等级是逻辑状态，本来就不该由动画驱动。
    /// ==========================================================
    ///
    /// 【朝向自动处理】
    /// 特效挂成角色的子物体，而 SetFacingDirection 用的是 transform 旋转
    /// （不是 flipX），所以子物体会自动跟着翻转。
    /// 因此 offset 填【局部坐标】即可，不需要像判定框那样手动乘 facingDirection。
    /// </summary>
    public class PlayerEffectSpawner : MonoBehaviour
    {
        [Header("挂点")]
        [Tooltip("特效生成后挂在谁下面。留空则挂在本物体上。\n" +
                 "建议单独建一个空的 EffectRoot 子物体，方便统一调整偏移。")]
        public Transform effectRoot;

        [Header("默认值")]
        [Tooltip("招式没填 effectKey 时用这个。留空则不出特效")]
        public string defaultAttackEffectKey = "";

        [Tooltip("招式没单独指定时长时，攻击特效存活多久")]
        public float defaultEffectLifetime = 0.4f;

        [Header("Debug")]
        public bool verboseLog = false;

        private PlayerStateMachine sm;
        private PlayerChargeSystem chargeSystem;
        private ComboInputBuffer buffer;
        private WeaponLoadout weapons;

        // ==========================================================
        // 蓄力光效：【每只手各存一份】
        //
        // 原先只有一个 activeChargeEffect / activeChargeLevel，两只手共用 ——
        // 主手蓄满挂上光效，副手蓄满时会先把主手的回收掉再挂自己的，
        // 任意一只手结束时又把对方的清掉。表现出来就是"双手光效全丢"。
        //
        // 蓄力早就是每只手一个模块了，光效自然也该按槽位分开。
        // 索引就用事件带来的 WeaponSlot。
        // ==========================================================
        private readonly GameObject[] activeChargeEffects = new GameObject[2];
        private readonly int[] activeChargeLevels = new int[2];

        private Transform Root => effectRoot != null ? effectRoot : transform;

        private void Awake()
        {
            sm = GetComponent<PlayerStateMachine>();
            chargeSystem = GetComponent<PlayerChargeSystem>();
            buffer = GetComponent<ComboInputBuffer>();
            weapons = GetComponent<WeaponLoadout>();
        }

        private void OnEnable()
        {
            // ChargeState 在 PlayerStateMachine.Awake 里创建，所以这里可能还没有。
            // 用 Start 更稳妥 —— 但 OnEnable/OnDisable 成对更安全，
            // 因此两边都做判空。
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (chargeSystem != null)
            {
                chargeSystem.OnChargeLevelChanged -= HandleChargeLevelChanged;
            }

            // 组件被关掉时别把常驻特效落在场景里 —— 那就是池泄漏
            ClearAllChargeEffects();
        }

        private bool subscribed;

        private void TrySubscribe()
        {
            if (subscribed) return;
            if (chargeSystem == null) return;

            // 【P3 改动】蓄力不再是状态，等级事件改由 PlayerChargeSystem 广播。
            // 事件带上了槽位参数 —— 以后想让两只手各显示一条蓄力条，直接就能做。
            chargeSystem.OnChargeLevelChanged += HandleChargeLevelChanged;
            subscribed = true;
        }

        // ==========================================================
        // 1. 攻击特效（动画事件入口）
        // ==========================================================

        /// <summary>
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

            string key = !string.IsNullOrEmpty(node.effectKey)
                ? node.effectKey
                : defaultAttackEffectKey;

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
        }

        // ==========================================================
        // 2. 蓄力特效（等级事件驱动）
        // ==========================================================

        private void HandleChargeLevelChanged(WeaponSlot slot, int level)
        {
            int i = (int)slot;

            // 等级没变就不折腾（避免每帧重建特效）
            if (level == activeChargeLevels[i]) return;

            ClearChargeEffect(slot);
            activeChargeLevels[i] = level;

            if (level <= 0) return;

            // 蓄的是哪把武器？用它的光效
            WeaponMoveSet weapon = weapons != null ? weapons.GetWeapon(slot) : null;

            if (weapon == null) return;

            string key = weapon.GetChargeEffectKey(level);
            if (string.IsNullOrEmpty(key)) return;

            // 常驻：一直挂着直到升级换掉、或松手清掉
            activeChargeEffects[i] = Spawn(
                key, weapon.chargeEffectOffset, 0f, persistent: true,
                weapon.chargeEffectScale, weapon.chargeEffectRotation);

            if (verboseLog) Debug.Log($"[特效] {slot} 蓄力 {level} 级光效：{key}");
        }

        /// <summary>回收当前蓄力光效。松手、受击、换武器时都要调</summary>
        /// <summary>回收指定手的蓄力光效</summary>
        public void ClearChargeEffect(WeaponSlot slot)
        {
            int i = (int)slot;

            if (activeChargeEffects[i] != null)
            {
                ObjectPoolManager.Instance?.Recycle(activeChargeEffects[i]);
                activeChargeEffects[i] = null;
            }
            activeChargeLevels[i] = 0;
        }

        /// <summary>回收两只手的蓄力光效</summary>
        public void ClearAllChargeEffects()
        {
            ClearChargeEffect(WeaponSlot.Main);
            ClearChargeEffect(WeaponSlot.Sub);
        }

        // ==========================================================
        // 生成
        // ==========================================================

        private GameObject Spawn(
            string key, Vector2 localOffset, float lifetime, bool persistent,
            Vector2 scale, float rotation)
        {
            GameObject fx = ObjectPoolManager.Instance?.Get(key);

            // key 未注册时池会自己报 LogError，这里静默跳过避免刷屏
            if (fx == null) return null;

            // 挂成子物体 → 跟着角色移动，朝向也由父物体旋转自动处理
            fx.transform.SetParent(Root, false);
            fx.transform.localPosition = localOffset;

            // 旋转与缩放按招式配置。
            // 【为什么不用负 scale 做翻转】特效是角色的子物体，
            // 角色转身用的是 transform 旋转，子物体已经跟着翻了 ——
            // 再填负数会翻两次，等于没翻。所以这里只做纯粹的缩放。
            fx.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            fx.transform.localScale = new Vector3(
                Mathf.Abs(scale.x), scale.y, 1f);

            PooledEffect pe = fx.GetComponent<PooledEffect>();
            if (pe != null)
            {
                pe.Play(persistent ? 0f : lifetime);
            }
            else
            {
                Debug.LogWarning(
                    $"[特效] 池「{key}」取出的对象上没有 PooledEffect 组件，无法自动回收。", fx);
            }

            return fx;
        }
    }
}