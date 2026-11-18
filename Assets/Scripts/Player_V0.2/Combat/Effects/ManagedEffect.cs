using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// EffectManager 池子里的一个特效（某个特效预制体的实例）。活到时间、或被叫停时由 EffectManager 收回。
    /// 由 EffectManager 在生成实例时自动挂上，不需要手动往预制体上加。
    ///
    /// 每次借出时：
    /// - Animator 倒回第 1 帧（池子里的物体是反复借用的，不倒带会从上次停下的地方接着播）；
    /// - 粒子先清掉上次残留的，再从头喷。
    /// 预制体里的 Animator / 粒子会自动找到，不用像旧的 PooledEffect 那样手动拖。
    /// </summary>
    [DisallowMultipleComponent]
    public class ManagedEffect : MonoBehaviour
    {
        /// <summary>第几次播放。每播一次 +1，用来核对"还是不是当初那一次"</summary>
        public int PlayId { get; private set; }

        public bool IsPlaying { get; private set; }

        /// <summary>传给 Begin 的 life 用这个值 = 播完特效自己的动画一遍（时长自动读）</summary>
        public const float PlayOnce = -1f;

        /// <summary>这一次实际要活多久（播完一遍时是读出来的动画长度；常驻时为 0）</summary>
        public float Lifetime => lifetime;

        /// <summary>招式被打断时不收（Fixed / Until Marker 模式、蓄力光效）</summary>
        public bool SurvivesInterrupt { get; private set; }

        /// <summary>常驻：不按时间收，等调用方叫停（蓄力光效）</summary>
        public bool IsPersistent { get; private set; }

        /// <summary>从哪个预制体生成的（回收时放回对应的池）</summary>
        public GameObject SourcePrefab { get; private set; }

        private Animator[] animators;
        private ParticleSystem[] particles;
        private float timer;
        private float lifetime;

        internal void Init(GameObject prefab)
        {
            SourcePrefab = prefab;
            animators = GetComponentsInChildren<Animator>(true);
            particles = GetComponentsInChildren<ParticleSystem>(true);
        }

        /// <param name="life">存活时长；0 = 常驻；PlayOnce（-1）= 播完自己的动画一遍</param>
        /// <param name="fit">调整动画速度，让它在 life 里正好播完一遍（常驻时不生效）</param>
        /// <param name="survivesInterrupt">招式被打断时不收</param>
        /// <param name="fallbackLife">要播完一遍、但预制体里既没有动画也没有粒子时用这个时长</param>
        internal void Begin(float life, bool fit, bool survivesInterrupt, float fallbackLife)
        {
            bool playOnce = life < 0f;
            if (playOnce) fit = false;   // 播完一遍就是原速，不用调

            PlayId++;
            IsPlaying = true;
            SurvivesInterrupt = survivesInterrupt;
            IsPersistent = life == 0f;
            timer = 0f;
            lifetime = life;

            // ---- 动画：倒回第 1 帧，速度恢复 1 倍；需要的话按长度算倍速 ----
            if (animators != null)
            {
                for (int i = 0; i < animators.Length; i++)
                {
                    Animator a = animators[i];
                    if (a == null || a.runtimeAnimatorController == null) continue;

                    a.speed = 1f;
                    a.Rebind();
                    a.Update(0f);

                    if (fit && !IsPersistent)
                    {
                        // 默认状态的动画长度 ÷ 想要的时长 = 播放速度
                        float clipLength = a.GetCurrentAnimatorStateInfo(0).length;
                        if (clipLength > 0.01f && life > 0.01f) a.speed = clipLength / life;
                    }
                }
            }

            // ---- 播完一遍：时长 = 动画长度（多个取最长）；没有动画就看粒子；都没有用兜底时长 ----
            if (playOnce) lifetime = NaturalLength(fallbackLife);

            // ---- 粒子：清掉上次残留的，从头喷 ----
            if (particles != null)
            {
                for (int i = 0; i < particles.Length; i++)
                {
                    ParticleSystem p = particles[i];
                    if (p == null) continue;
                    p.Clear(false);
                    p.Play(false);
                }
            }
        }

        /// <summary>特效自己播完一遍要多久（在 Animator 倒带之后调用）</summary>
        private float NaturalLength(float fallback)
        {
            float len = 0f;

            if (animators != null)
            {
                for (int i = 0; i < animators.Length; i++)
                {
                    Animator a = animators[i];
                    if (a == null || a.runtimeAnimatorController == null) continue;
                    len = Mathf.Max(len, a.GetCurrentAnimatorStateInfo(0).length);
                }
            }

            if (len <= 0.01f && particles != null)
            {
                for (int i = 0; i < particles.Length; i++)
                {
                    ParticleSystem p = particles[i];
                    if (p == null || p.main.loop) continue;
                    len = Mathf.Max(len, p.main.duration + p.main.startLifetime.constantMax);
                }
            }

            return len > 0.01f ? len : Mathf.Max(0.01f, fallback);
        }

        /// <returns>true = 时间到了，该收回</returns>
        internal bool Tick(float deltaTime)
        {
            if (!IsPlaying || IsPersistent) return false;
            timer += deltaTime;
            return timer >= lifetime;
        }

        internal void End()
        {
            IsPlaying = false;
            IsPersistent = false;

            if (animators != null)
                for (int i = 0; i < animators.Length; i++)
                    if (animators[i] != null) animators[i].speed = 1f;

            // 收回时也清一次，不让粒子留在场景里
            if (particles != null)
                for (int i = 0; i < particles.Length; i++)
                    if (particles[i] != null) particles[i].Clear(false);
        }
    }
}
