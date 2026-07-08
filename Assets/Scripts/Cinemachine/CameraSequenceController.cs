using UnityEngine;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class CameraSequenceController : MonoBehaviour
{
    [Header("参演摄像机")]
    public CinemachineCamera wideCameraA;
    public CinemachineCamera closeCameraB;
    public CinemachineCamera followCamera;

    [Header("像素完美组件")]
    public PixelPerfectCamera pixelPerfectCamera;

    [Header("时间轴设置")]
    public float waitTimeAtA = 1.0f;
    public float waitTimeAtB = 3.0f;

    [Header("全黑幕布设置")]
    public Image fadeImage;
    public float fadeTime = 0.5f;

    [Header("返回跟随分支接口")]
    [Tooltip("【核心开关】展示结束后，切回主角时是否需要黑屏过渡？勾选则全黑瞬切；不勾选则不黑屏，镜头平滑摇回主角。")]
    public bool fadeOnReturn = true;

    private bool hasTriggered = false;
    private GameObject player;

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player");
    }

    public void StartSequence()
    {
        if (!hasTriggered)
        {
            hasTriggered = true;
            StartCoroutine(PlayCameraSequence());
        }
    }

    private IEnumerator PlayCameraSequence()
    {
        // ==========================================
        // 步骤一：定身主角
        // ==========================================
        if (player != null)
        {
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;

            MonoBehaviour stateMachine = player.GetComponent<MonoBehaviour>(); // 记得换成你实际的状态机脚本名
            if (stateMachine != null) stateMachine.enabled = false;
        }

        // ==========================================
        // 步骤二：首次渐暗（拉上黑幕）
        // ==========================================
        yield return StartCoroutine(FadeTo(1f));

        // ==========================================
        // 步骤三：黑屏中瞬间就位（防开场滑行穿帮）
        // ==========================================
        if (pixelPerfectCamera != null) pixelPerfectCamera.enabled = false;

        wideCameraA.Priority = 20;
        closeCameraB.Priority = 0;
        followCamera.Priority = 0;

        CinemachineBrain camBrain = Camera.main.GetComponent<CinemachineBrain>();
        if (camBrain != null)
        {
            camBrain.enabled = false;
            yield return null;
            camBrain.enabled = true;
        }

        // ==========================================
        // 步骤四：渐亮（拉开黑幕，全亮展示场景运镜）
        // ==========================================
        yield return StartCoroutine(FadeTo(0f));

        // 此时画面全亮，玩家看着机位 A
        yield return new WaitForSeconds(waitTimeAtA);

        // 镜头开始在亮屏状态下，平滑推向机位 B（满足你的场景展示）
        closeCameraB.Priority = 21;
        yield return new WaitForSeconds(waitTimeAtB);

        // ==========================================
        // 步骤五：【分支处理】决定返回主角时是否黑屏
        // ==========================================
        if (fadeOnReturn)
        {
            // 模式 A：需要黑屏返回（全黑屏 -> 瞬切 -> 渐亮）
            yield return StartCoroutine(FadeTo(1f));

            wideCameraA.Priority = 0;
            closeCameraB.Priority = 0;
            followCamera.Priority = 25;

            // 黑屏状态下，再次清空一次阻尼，防止穿帮
            if (camBrain != null)
            {
                camBrain.enabled = false;
                yield return null;
                camBrain.enabled = true;
            }

            if (pixelPerfectCamera != null) pixelPerfectCamera.enabled = true;

            yield return StartCoroutine(FadeTo(0f));
        }
        else
        {
            // 模式 B：【你的新需求】不需要黑屏返回！
            // 直接将过场相机的优先级降下去，让跟随相机的优先级升上来
            wideCameraA.Priority = 0;
            closeCameraB.Priority = 0;
            followCamera.Priority = 25;

            // 恢复像素完美相机
            if (pixelPerfectCamera != null) pixelPerfectCamera.enabled = true;

            // 💡 大厂体验细节：因为没黑屏，镜头正在慢慢滑回主角身边。
            // 此时我们可以选择让玩家立刻能动（看镜头滑回来），也可以选择让玩家等镜头安全滑回身边后再动。
            // 这里我们等待 1 秒钟（你可以根据你 Cinemachine 的 Default Blend 时间调整），让镜头基本滑回主角后再恢复控制。
            yield return new WaitForSeconds(1.0f);
        }

        // ==========================================
        // 步骤六：解除定身，还政于民
        // ==========================================
        if (player != null)
        {
            MonoBehaviour stateMachine = player.GetComponent<MonoBehaviour>();
            if (stateMachine != null) stateMachine.enabled = true;
        }

        hasTriggered = false;
    }

    // 控制黑幕渐变的小协程
    private IEnumerator FadeTo(float targetAlpha)
    {
        if (fadeImage == null) yield break;

        fadeImage.gameObject.SetActive(true);
        Color color = fadeImage.color;
        float startAlpha = color.a;
        float timer = 0f;

        while (timer < fadeTime)
        {
            timer += Time.deltaTime;
            color.a = Mathf.Lerp(startAlpha, targetAlpha, timer / fadeTime);
            fadeImage.color = color;
            yield return null;
        }

        color.a = targetAlpha;
        fadeImage.color = color;

        if (targetAlpha == 0f) fadeImage.gameObject.SetActive(false);
    }
}