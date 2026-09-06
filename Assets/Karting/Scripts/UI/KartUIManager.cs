using KartGame.KartSystems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KartGame.UI
{
    /// <summary>
    /// UI Manager for ArcadeKart v2.0
    /// Displays:
    /// - Boost energy bar
    /// - Combo counter and points
    /// - Current speed
    /// - Drift status
    /// </summary>
    public class KartUIManager : MonoBehaviour
    {
        [SerializeField] private ArcadeKart m_KartController;

        [Header("Boost UI")]
        [SerializeField] private Image m_BoostBar;
        [SerializeField] private TextMeshProUGUI m_BoostText;
        [SerializeField] private Color m_BoostBarColorActive = Color.cyan;
        [SerializeField] private Color m_BoostBarColorInactive = Color.gray;

        [Header("Combo UI")]
        [SerializeField] private TextMeshProUGUI m_ComboMultiplierText;
        [SerializeField] private TextMeshProUGUI m_ComboPointsText;
        [SerializeField] private CanvasGroup m_ComboCanvasGroup;  // For fade in/out

        [Header("Speed UI")]
        [SerializeField] private TextMeshProUGUI m_SpeedText;
        [SerializeField] private Text m_SpeedBarFill;  // Legacy Image for gradient

        [Header("Drift Status")]
        [SerializeField] private TextMeshProUGUI m_DriftStatusText;
        [SerializeField] private Image m_DriftStatusIcon;
        [SerializeField] private Color m_DriftActiveColor = Color.yellow;
        [SerializeField] private Color m_DriftInactiveColor = Color.gray;

        [Header("Animation Settings")]
        [SerializeField] private float m_ComboFadeSpeed = 2f;
        [SerializeField] private float m_ComboAnimationScale = 1.2f;

        private float m_CurrentComboAlpha = 0f;
        private CanvasGroup m_ComboScaleAnimator;

        void Awake()
        {
            if (m_KartController == null)
                m_KartController = FindObjectOfType<ArcadeKart>();

            if (m_ComboCanvasGroup != null)
                m_ComboScaleAnimator = m_ComboCanvasGroup;
        }

        void Update()
        {
            UpdateBoostUI();
            UpdateComboUI();
            UpdateSpeedUI();
            UpdateDriftStatusUI();
        }

        void UpdateBoostUI()
        {
            if (m_BoostBar != null)
            {
                float boostPercent = m_KartController.CurrentBoostEnergy / m_KartController.MaxBoostEnergy;
                m_BoostBar.fillAmount = boostPercent;

                // Color change based on active/inactive
                if (m_KartController.IsBoostActive)
                {
                    m_BoostBar.color = Color.Lerp(m_BoostBar.color, m_BoostBarColorActive, Time.deltaTime * 5f);
                }
                else
                {
                    m_BoostBar.color = Color.Lerp(m_BoostBar.color, m_BoostBarColorInactive, Time.deltaTime * 5f);
                }
            }

            if (m_BoostText != null)
            {
                m_BoostText.text = $"{m_KartController.CurrentBoostEnergy:F0} / {m_KartController.MaxBoostEnergy:F0}";
            }
        }

        void UpdateComboUI()
        {
            var comboSystem = m_KartController.DriftComboSystem;

            // Fade in when active, fade out when inactive
            if (comboSystem.IsActive)
            {
                m_CurrentComboAlpha = Mathf.Lerp(m_CurrentComboAlpha, 1f, Time.deltaTime * m_ComboFadeSpeed);
            }
            else
            {
                m_CurrentComboAlpha = Mathf.Lerp(m_CurrentComboAlpha, 0f, Time.deltaTime * m_ComboFadeSpeed);
            }

            if (m_ComboCanvasGroup != null)
            {
                m_ComboCanvasGroup.alpha = m_CurrentComboAlpha;
            }

            // Update text
            if (m_ComboMultiplierText != null)
            {
                m_ComboMultiplierText.text = $"COMBO x{comboSystem.CurrentComboMultiplier}";
                
                // Pulse animation
                float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.1f;
                m_ComboMultiplierText.transform.localScale = Vector3.one * pulse;
            }

            if (m_ComboPointsText != null)
            {
                m_ComboPointsText.text = $"POINTS: {comboSystem.TotalPoints}";
            }
        }

        void UpdateSpeedUI()
        {
            float currentSpeed = m_KartController.Rigidbody.velocity.magnitude;
            float maxSpeed = m_KartController.GetMaxSpeed();
            float speedPercent = currentSpeed / maxSpeed;

            if (m_SpeedText != null)
            {
                m_SpeedText.text = $"{currentSpeed:F1} m/s";
            }

            if (m_SpeedBarFill != null)
            {
               // m_SpeedBarFill.fillAmount = Mathf.Clamp01(speedPercent);
            }
        }

        void UpdateDriftStatusUI()
        {
            if (m_DriftStatusText != null)
            {
                if (m_KartController.IsDrifting)
                {
                    m_DriftStatusText.text = "DRIFTING!";
                    m_DriftStatusText.color = m_DriftActiveColor;
                }
                else
                {
                    m_DriftStatusText.text = "Ready to Drift";
                    m_DriftStatusText.color = m_DriftInactiveColor;
                }
            }

            if (m_DriftStatusIcon != null)
            {
                m_DriftStatusIcon.color = m_KartController.IsDrifting ? m_DriftActiveColor : m_DriftInactiveColor;
            }
        }

        /// <summary>Display popup message (e.g., "DRIFT BOOST!")</summary>
        public void ShowMessage(string message, float duration = 1f)
        {
            StartCoroutine(ShowMessageCoroutine(message, duration));
        }

        System.Collections.IEnumerator ShowMessageCoroutine(string message, float duration)
        {
            // This is a placeholder - implement your own message display
            Debug.Log($"[KartUI] {message}");
            yield return new WaitForSeconds(duration);
        }
    }

    // ========================================
    // SIMPLE HUD DISPLAY (NO TEXTMESH PRO)
    // ========================================

    /// <summary>
    /// Simple HUD using legacy UI (compatible with all projects)
    /// </summary>
    public class KartSimpleHUD : MonoBehaviour
    {
        [SerializeField] private ArcadeKart m_KartController;

        [Header("UI Elements")]
        [SerializeField] private Text m_BoostBarText;
        [SerializeField] private Text m_ComboText;
        [SerializeField] private Text m_SpeedText;
        [SerializeField] private Text m_StatusText;

        void Update()
        {
            if (m_KartController == null) return;

            // Boost bar (using characters)
            if (m_BoostBarText != null)
            {
                float boostPercent = m_KartController.CurrentBoostEnergy / m_KartController.MaxBoostEnergy;
                int barLength = (int)(boostPercent * 20);
                string boostBar = new string('█', barLength) + new string('░', 20 - barLength);
                m_BoostBarText.text = $"BOOST: [{boostBar}]";
            }

            // Combo
            if (m_ComboText != null)
            {
                var combo = m_KartController.DriftComboSystem;
                m_ComboText.text = $"COMBO x{combo.CurrentComboMultiplier} | POINTS: {combo.TotalPoints}";
                m_ComboText.color = combo.IsActive ? Color.yellow : Color.gray;
            }

            // Speed
            if (m_SpeedText != null)
            {
                float speed = m_KartController.Rigidbody.velocity.magnitude;
                m_SpeedText.text = $"SPEED: {speed:F1}m/s";
            }

            // Status
            if (m_StatusText != null)
            {
                string status = "";
                if (m_KartController.IsBoostActive) status += "BOOSTING ";
                if (m_KartController.IsDrifting) status += "DRIFTING ";
                if (m_KartController.AirPercent > 0.1f) status += "AIRBORNE ";
                
                m_StatusText.text = status.Length > 0 ? status : "READY";
                m_StatusText.color = m_KartController.IsDrifting ? Color.yellow : Color.white;
            }
        }
    }

    // ========================================
    // MINI RADAR DISPLAY
    // ========================================

    /// <summary>
    /// Mini radar showing boost and combo status
    /// </summary>
    public class KartRadarDisplay : MonoBehaviour
    {
        [SerializeField] private ArcadeKart m_KartController;
        [SerializeField] private Image m_BoostRadial;  // Radial image for boost
        [SerializeField] private Image m_DriftRadial;  // Radial image for drift state
        [SerializeField] private float m_UpdateSpeed = 5f;

        void Update()
        {
            if (m_KartController == null) return;

            // Update boost radial
            if (m_BoostRadial != null)
            {
                float boostPercent = m_KartController.CurrentBoostEnergy / m_KartController.MaxBoostEnergy;
                m_BoostRadial.fillAmount = Mathf.Lerp(m_BoostRadial.fillAmount, boostPercent, Time.deltaTime * m_UpdateSpeed);
                m_BoostRadial.color = m_KartController.IsBoostActive ? Color.cyan : Color.gray;
            }

            // Update drift radial
            if (m_DriftRadial != null)
            {
                float driftValue = m_KartController.IsDrifting ? 1f : 0f;
                m_DriftRadial.fillAmount = Mathf.Lerp(m_DriftRadial.fillAmount, driftValue, Time.deltaTime * m_UpdateSpeed);
            }
        }
    }
}
