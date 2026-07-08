using UnityEngine;
using UnityEngine.UI;

public class PlayerPortraitUI : MonoBehaviour
{
    [SerializeField] private Image portraitImage;
    [SerializeField] private SpriteRenderer playerSpriteRenderer;

    private void Awake()
    {
        if (portraitImage == null)
            portraitImage = GetComponent<Image>();
    }

    private void Update()
    {
        if (portraitImage == null || playerSpriteRenderer == null)
            return;

        portraitImage.sprite = playerSpriteRenderer.sprite;
    }
}