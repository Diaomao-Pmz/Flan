using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    // 将引用从 EnemyState 改为 EntityBase
    public EntityBase targetEntity;
    public Image fillImage;
    public GameObject rootObject;

    private void OnEnable()
    {
        if (targetEntity != null)
            targetEntity.OnStatChanged += Refresh; // 继续监听事件
    }

    private void OnDisable()
    {
        if (targetEntity != null)
            targetEntity.OnStatChanged -= Refresh;
    }

    private void Start()
    {
        Refresh();
    }

    public void Refresh()
    {
        if (targetEntity == null || fillImage == null) return;

        fillImage.fillAmount = (float)targetEntity.currentHP / targetEntity.maxHP;

        if (rootObject != null)
            rootObject.SetActive(targetEntity.currentHP < targetEntity.maxHP);
    }
}