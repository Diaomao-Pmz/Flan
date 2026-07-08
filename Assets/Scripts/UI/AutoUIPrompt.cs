using UnityEngine;
using System.Collections;

[RequireComponent(typeof(CanvasGroup))]
public class AutoUIPrompt : MonoBehaviour
{
    [Header("UI 设置")]
    public float fadeDuration = 0.3f;

    private CanvasGroup uiGroup;
    private Collider2D parentTrigger;
    private Collider2D playerCollider; // 升级：直接获取玩家的整个身体碰撞框

    private bool isCurrentlyShowing = false;
    private Coroutine currentFade;

    private void Start()
    {
        uiGroup = GetComponent<CanvasGroup>();
        uiGroup.alpha = 0f;

        parentTrigger = GetComponentInParent<Collider2D>();

        // 自动寻找玩家
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            // 找到玩家后，顺便把她身上的碰撞体组件要过来
            playerCollider = playerObj.GetComponent<Collider2D>();
        }
        else
        {
            Debug.LogError("UI预制体找不到带有 'Player' 标签的物体！请检查主角最上方的 Tag！");
        }
    }

    private void Update()
    {
        if (parentTrigger == null || playerCollider == null) return;

        // 核心升级：判断【书本的绿框】和【芙兰的绿框】是否重叠交汇！
        // 只要芙兰的头发、手或任何碰撞边缘碰到了书的绿框，立刻返回 true
        bool isPlayerInRange = parentTrigger.bounds.Intersects(playerCollider.bounds);

        if (isPlayerInRange && !isCurrentlyShowing)
        {
            isCurrentlyShowing = true;
            StartFading(1f);
        }
        else if (!isPlayerInRange && isCurrentlyShowing)
        {
            isCurrentlyShowing = false;
            StartFading(0f);
        }
    }

    // ==========================================
    // 渐变引擎 (无需修改)
    // ==========================================
    private void StartFading(float targetAlpha)
    {
        if (currentFade != null) StopCoroutine(currentFade);
        currentFade = StartCoroutine(FadeRoutine(targetAlpha));
    }

    private IEnumerator FadeRoutine(float targetAlpha)
    {
        float startAlpha = uiGroup.alpha;
        float timer = 0f;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            uiGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, timer / fadeDuration);
            yield return null;
        }

        uiGroup.alpha = targetAlpha;
    }
}