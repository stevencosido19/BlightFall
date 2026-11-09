using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealthUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Image healthFillImage;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI roleText;

    [Header("Role Bar Colors")]
    [SerializeField] private Color knightColor = new Color(0.2f, 0.6f, 1f);     // Steel Blue
    [SerializeField] private Color arbalistColor = new Color(1f, 0.65f, 0.2f);   // Amber Orange
    [SerializeField] private Color rogueColor = new Color(0.8f, 0.3f, 1f);      // Shadow Purple

    [Header("Smooth Animation")]
    [SerializeField] private float smoothSpeed = 6f;

    private PlayerHealth trackedPlayer;
    private float targetFill = 1f;

    private void OnEnable()
    {
        PlayerHealth.OnLocalPlayerHealthReady += BindLocalPlayer;

        // If local player already exists
        if (PlayerHealth.LocalPlayerInstance != null)
        {
            BindLocalPlayer(PlayerHealth.LocalPlayerInstance);
        }
    }

    private void OnDisable()
    {
        PlayerHealth.OnLocalPlayerHealthReady -= BindLocalPlayer;
        UnbindPlayer();
    }

    private void BindLocalPlayer(PlayerHealth player)
    {
        UnbindPlayer();

        trackedPlayer = player;
        trackedPlayer.CurrentHealth.OnValueChanged += HandleHealthChanged;

        // 1. Configure Role visuals
        if (roleText != null)
        {
            roleText.text = player.PlayerRole.ToString().ToUpper();
        }

        Color roleColor = player.PlayerRole switch
        {
            CharacterRole.Knight => knightColor,
            CharacterRole.Arbalist => arbalistColor,
            CharacterRole.Rogue => rogueColor,
            _ => Color.green
        };

        if (healthFillImage != null)
        {
            healthFillImage.color = roleColor;
        }

        // 2. Initial value
        UpdateHealthDisplay(trackedPlayer.CurrentHealth.Value);
    }

    private void UnbindPlayer()
    {
        if (trackedPlayer != null)
        {
            trackedPlayer.CurrentHealth.OnValueChanged -= HandleHealthChanged;
            trackedPlayer = null;
        }
    }

    private void HandleHealthChanged(int previous, int current)
    {
        UpdateHealthDisplay(current);
    }

    private void UpdateHealthDisplay(int currentHP)
    {
        if (trackedPlayer == null) return;

        int maxHP = trackedPlayer.MaxHealth;
        targetFill = Mathf.Clamp01((float)currentHP / maxHP);

        if (healthText != null)
        {
            healthText.text = $"{currentHP} / {maxHP}";
        }
    }

    private void Update()
    {
        // Smoothly animate the health bar fill
        if (healthSlider != null)
        {
            healthSlider.value = Mathf.Lerp(healthSlider.value, targetFill, Time.deltaTime * smoothSpeed);
        }
    }
}