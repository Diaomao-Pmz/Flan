using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject inventoryPanel;
    public Button inventoryButton;
    public Image buttonImage;

    [Header("Sprites")]
    public Sprite normalSprite;
    public Sprite activeSprite;

    private bool isOpen = false;

    public void ToggleInventory()
    {
        isOpen = !isOpen;

        inventoryPanel.SetActive(isOpen);
        buttonImage.sprite = isOpen ? activeSprite : normalSprite;
    }
}