// UIManager.cs
// Controla la UI general: información del jugador, slider, inventario, prompt defensor, etc.
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("Refs UI")]
    public TextMeshProUGUI PlayerName;
    public TextMeshProUGUI tropasDisponiblesText;
    public Button cambiarFaseButton;
    public TextMeshProUGUI faseActualText;
    public TextMeshProUGUI tropasEnTerritorioText;
    public TextMeshProUGUI sliderValueText;
    public Slider tropasSlider;
    public Button colocarButton;
    public Button terminarTurnoButton;

    [Header("Backgrounds por fase")]
    public Camera mainCamera;
    public Color colorRefuerzo = Color.green;
    public Color colorAtaque = Color.red;
    public Color colorReagrupacion = Color.blue;

    [Header("Inventario de Cartas")]
    public GameObject inventarioPanel;
    public GameObject cartaPrefab;
    public Transform contenedorCartas;
    public Button inventarioToggleButton;
    public Button confirmarIntercambioButton;

    [Header("Defense Prompt UI")]
    public GameObject defensePromptPanel;
    public Slider defensaSlider;
    public TextMeshProUGUI defensaSliderValueText;
    public TextMeshProUGUI defensaPromptTitle;

    [Header("Panels adicionales")]
    public GameObject eliminatedPanel; // panel para cuando el jugador es eliminado

    private Territory selectedTerritorio;
    private bool inventarioAbierto = false;
    private int pendingAttackIdForUI = -1;
    private int pendingAtacanteIdxForUI = -1;
    private int pendingDefensorIdxForUI = -1;
    private int pendingTropasAtqForUI = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        RefrescarUI();
        if (colocarButton != null) colocarButton.onClick.AddListener(OnColocarTropas);
        if (terminarTurnoButton != null) terminarTurnoButton.onClick.AddListener(OnTerminarTurno);
        if (tropasSlider != null) tropasSlider.onValueChanged.AddListener(delegate { OnSliderChanged(); });
        if (cambiarFaseButton != null) cambiarFaseButton.onClick.AddListener(OnCambiarFase);
        if (inventarioToggleButton != null) inventarioToggleButton.onClick.AddListener(ToggleInventario);
        if (confirmarIntercambioButton != null) confirmarIntercambioButton.onClick.AddListener(OnConfirmarIntercambio);

        defensePromptPanel?.SetActive(false);
        inventarioPanel?.SetActive(false);

        if (BoardManager.Instance != null)
        {
            BoardManager.Instance.currentPlayerIndex.OnValueChanged += (_, __) => RefrescarUI();
            BoardManager.Instance.currentPhase.OnValueChanged += (_, __) => { ActualizarBackground(); RefrescarUI(); };
        }
    }

    public void SeleccionarTerritorio(Territory terr)
    {
        selectedTerritorio = terr;
        RefrescarUI();
    }

    // Actualizar datos visibles en UI (protegiendo nulls)
    public void RefrescarUI()
    {
        var jugador = BoardManager.Instance?.GetJugadorLocal();
        if (jugador == null) return;

        if (PlayerName != null) PlayerName.text = jugador.Alias.Value.ToString();
        if (tropasDisponiblesText != null) tropasDisponiblesText.text = $"Tropas disponibles: {jugador.TropasDisponibles.Value}";
        if (faseActualText != null) faseActualText.text = $"Fase actual: {BoardManager.Instance.currentPhase.Value}";

        // Si hay un territorio seleccionado y es del jugador, actualizar slider según fase
        if (selectedTerritorio != null && jugador.ComprobarTerritorio(selectedTerritorio))
        {
            if (tropasEnTerritorioText != null) tropasEnTerritorioText.text = $"Tropas en territorio: {selectedTerritorio.Soldiers.Value}";

            if (tropasSlider == null)
            {
                Debug.LogWarning("RefrescarUI: tropasSlider no asignado.");
                return;
            }

            switch (BoardManager.Instance.currentPhase.Value)
            {
                case BoardManager.Phase.Reinforcement:
                    tropasSlider.minValue = 1;
                    tropasSlider.maxValue = Mathf.Max(1, jugador.TropasDisponibles.Value);
                    break;
                case BoardManager.Phase.Attack:
                    int maxT = Mathf.Max(0, selectedTerritorio.Soldiers.Value - 1);
                    tropasSlider.minValue = maxT > 0 ? 1 : 0;
                    tropasSlider.maxValue = Mathf.Min(3, maxT);
                    break;
                case BoardManager.Phase.Regroup:
                    int maxT2 = Mathf.Max(0, selectedTerritorio.Soldiers.Value - 1);
                    tropasSlider.minValue = maxT2 > 0 ? 1 : 0;
                    tropasSlider.maxValue = maxT2;
                    break;
            }

            if (tropasSlider.maxValue < tropasSlider.minValue) tropasSlider.value = 0;
            else tropasSlider.value = tropasSlider.minValue;
            OnSliderChanged();
        }
        else
        {
            if (tropasEnTerritorioText != null) tropasEnTerritorioText.text = "Tropas en territorio: -";
            if (sliderValueText != null) sliderValueText.text = "";
        }
    }

    public void ActualizarBackground()
    {
        if (BoardManager.Instance == null || mainCamera == null) return;
        switch (BoardManager.Instance.currentPhase.Value)
        {
            case BoardManager.Phase.Reinforcement: mainCamera.backgroundColor = colorRefuerzo; break;
            case BoardManager.Phase.Attack: mainCamera.backgroundColor = colorAtaque; break;
            case BoardManager.Phase.Regroup: mainCamera.backgroundColor = colorReagrupacion; break;
        }
    }

    private void OnSliderChanged()
    {
        if (sliderValueText != null && tropasSlider != null) sliderValueText.text = $"{(int)tropasSlider.value}";
        if (defensaSliderValueText != null && defensaSlider != null) defensaSliderValueText.text = $"{(int)defensaSlider.value}";
    }

    private void OnCambiarFase()
    {
        if (BoardManager.Instance != null) BoardManager.Instance.CambiarFaseServerRpc();
        RefrescarUI();
    }

    private void OnColocarTropas()
    {
        var jugador = BoardManager.Instance.GetJugadorLocal();
        if (jugador == null || selectedTerritorio == null) return;
        int cantidad = tropasSlider != null ? (int)tropasSlider.value : 1;

        // Llamar al servidor a través de NetworkPlayer
        jugador.ColocarTropasRequest(selectedTerritorio.Idx, cantidad);
    }

    private void OnTerminarTurno()
    {
        if (BoardManager.Instance != null) BoardManager.Instance.TerminarTurnoServerRpc();
        selectedTerritorio = null;
        if (tropasEnTerritorioText != null) tropasEnTerritorioText.text = "Tropas en territorio: -";
        if (sliderValueText != null) sliderValueText.text = "";
        RefrescarUI();
    }

    #region Defense prompt
    public void ShowDefensePrompt(int attackId, int atacanteIdx, int defensorIdx, int tropasAtq, int maxDefPossible)
    {
        if (defensePromptPanel == null || defensaSlider == null || defensaSliderValueText == null || defensaPromptTitle == null)
        {
            Debug.LogWarning("ShowDefensePrompt: UI references no asignadas.");
            return;
        }

        pendingAttackIdForUI = attackId;
        pendingAtacanteIdxForUI = atacanteIdx;
        pendingDefensorIdxForUI = defensorIdx;
        pendingTropasAtqForUI = tropasAtq;

        defensePromptPanel.SetActive(true);
        defensaSlider.minValue = 1;
        defensaSlider.maxValue = Mathf.Max(1, maxDefPossible);
        defensaSlider.value = defensaSlider.minValue;
        OnSliderChanged();
        defensaPromptTitle.text = $"Atacan desde {atacanteIdx} con {tropasAtq} tropas. Elige tropas para defender:";
    }

    public void OnConfirmDefenseButton()
    {
        if (pendingAttackIdForUI < 0)
        {
            Debug.LogWarning("No hay ataque pendiente en UI.");
            return;
        }
        int chosen = (int)defensaSlider.value;
        BoardManager.Instance?.DefenderResponseServerRpc(pendingAttackIdForUI, chosen);
        defensePromptPanel.SetActive(false);
        pendingAttackIdForUI = -1;
    }
    #endregion

    #region Inventory / cards UI
    private void ToggleInventario()
    {
        inventarioAbierto = !inventarioAbierto;
        if (inventarioPanel != null) inventarioPanel.SetActive(inventarioAbierto);
        if (inventarioAbierto) ActualizarInventario(BoardManager.Instance.GetJugadorLocal());
    }


    #region Attack result handling
    public void HandleAttackResult(BoardManager.AttackResultSummary summary)
    {
        if (summary == null) return;
        string s = $"Ataque {summary.attackId}: {summary.atacanteIdx} vs {summary.defensorIdx}\n" +
                   $"Dados Atq: {string.Join(",", summary.attRolls ?? new int[0])}  Dados Def: {string.Join(",", summary.defRolls ?? new int[0])}\n" +
                   $"Pérdidas Atq: {summary.attackerLosses}  Pérdidas Def: {summary.defenderLosses}\n" +
                   (summary.conquered ? $"¡Conquistado! Tropas movidas: {summary.tropasMovidas}" : "Territorio defendido");
        Debug.Log("[UI] Resultado de ataque:\n" + s);
        RefrescarUI();
    }
    #endregion

    // Mostrar pantalla local de eliminación
    public void ShowEliminatedScreen()
    {
        if (eliminatedPanel != null) eliminatedPanel.SetActive(true);
        else Debug.Log("[UIManager] ShowEliminatedScreen llamado (sin panel asignado).");
    }

    // Mostrar fin de juego
    public void ShowEndGame(ulong winnerClientId, string winnerName)
    {
        Debug.Log($"[UI] EndGame: ganador={winnerName} ({winnerClientId})");
        // Aquí puedes mostrar un panel, etc.
    }
}
